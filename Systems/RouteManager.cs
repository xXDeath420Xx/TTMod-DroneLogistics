using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace DroneLogistics
{
    /// <summary>
    /// Manages drone delivery routes and optimizes logistics
    /// </summary>
    public class RouteManager
    {
        // Registered pickup/delivery points
        private Dictionary<int, LogisticsNode> nodes = new Dictionary<int, LogisticsNode>();
        private int nextNodeId = 0;

        // Active routes
        private List<DeliveryRoute> routes = new List<DeliveryRoute>();

        // Request handling
        private List<DeliveryRequest> pendingRequests = new List<DeliveryRequest>();
        private float lastOptimizeTime;
        private float optimizeInterval = 5f;

        public RouteManager()
        {
            DroneLogisticsPlugin.Log("RouteManager initialized");
        }

        public void Update()
        {
            // Periodically optimize routes
            if (Time.time - lastOptimizeTime > optimizeInterval)
            {
                OptimizeRoutes();
                lastOptimizeTime = Time.time;
            }

            // Process pending requests
            ProcessPendingRequests();
        }

        #region Node Registration

        public int RegisterNode(Vector3 position, LogisticsNodeType type, string name = null)
        {
            var node = new LogisticsNode
            {
                Id = nextNodeId++,
                Position = position,
                Type = type,
                Name = name ?? $"{type}_{nextNodeId}"
            };

            nodes[node.Id] = node;
            DroneLogisticsPlugin.Log($"Registered logistics node: {node.Name} at {position}");

            return node.Id;
        }

        public void UnregisterNode(int nodeId)
        {
            if (nodes.ContainsKey(nodeId))
            {
                nodes.Remove(nodeId);

                // Remove routes involving this node
                routes.RemoveAll(r => r.SourceNodeId == nodeId || r.DestinationNodeId == nodeId);
            }
        }

        public LogisticsNode GetNode(int nodeId)
        {
            return nodes.TryGetValue(nodeId, out var node) ? node : null;
        }

        public List<LogisticsNode> GetNodesInRange(Vector3 position, float range)
        {
            return nodes.Values
                .Where(n => Vector3.Distance(n.Position, position) <= range)
                .ToList();
        }

        #endregion

        #region Route Management

        public DeliveryRoute CreateRoute(int sourceId, int destId, ResourceInfo resource, int quantity, int priority = 0)
        {
            if (!nodes.ContainsKey(sourceId) || !nodes.ContainsKey(destId))
            {
                DroneLogisticsPlugin.LogWarning($"Invalid node IDs for route: {sourceId} -> {destId}");
                return null;
            }

            var route = new DeliveryRoute
            {
                SourceNodeId = sourceId,
                DestinationNodeId = destId,
                Resource = resource,
                QuantityPerTrip = quantity,
                Priority = priority,
                IsActive = true
            };

            routes.Add(route);

            DroneLogisticsPlugin.Log($"Created route: {nodes[sourceId].Name} -> {nodes[destId].Name} ({resource.displayName})");

            return route;
        }

        public void RemoveRoute(DeliveryRoute route)
        {
            routes.Remove(route);
        }

        public void SetRouteActive(DeliveryRoute route, bool active)
        {
            route.IsActive = active;
        }

        public List<DeliveryRoute> GetRoutesForNode(int nodeId)
        {
            return routes.Where(r => r.SourceNodeId == nodeId || r.DestinationNodeId == nodeId).ToList();
        }

        #endregion

        #region Request Processing

        public void RequestDelivery(Vector3 pickup, Vector3 delivery, ResourceInfo resource, int quantity, int priority = 0)
        {
            var request = new DeliveryRequest(pickup, delivery, resource, quantity, priority);
            pendingRequests.Add(request);

            // Sort by priority (higher first)
            pendingRequests.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        private void ProcessPendingRequests()
        {
            if (pendingRequests.Count == 0)
                return;

            // Try to assign requests to nearby pads
            for (int i = pendingRequests.Count - 1; i >= 0; i--)
            {
                var request = pendingRequests[i];

                // Find best pad for this request
                var pad = FindBestPadForRequest(request);
                if (pad != null && pad.RequestDelivery(request))
                {
                    pendingRequests.RemoveAt(i);
                }
            }

            // Clean up old requests
            float maxAge = 300f; // 5 minutes
            pendingRequests.RemoveAll(r => Time.time - r.TimeRequested > maxAge);
        }

        private DronePadController FindBestPadForRequest(DeliveryRequest request)
        {
            DronePadController bestPad = null;
            float bestScore = float.MaxValue;

            foreach (var pad in DroneLogisticsPlugin.ActivePads)
            {
                if (pad == null)
                    continue;

                float pickupDist = Vector3.Distance(pad.transform.position, request.PickupLocation);
                float deliveryDist = Vector3.Distance(pad.transform.position, request.DeliveryLocation);

                // Must be in range
                if (pickupDist > DroneLogisticsPlugin.DroneRange.Value)
                    continue;
                if (deliveryDist > DroneLogisticsPlugin.DroneRange.Value)
                    continue;

                // Score based on distance and availability
                float score = pickupDist + deliveryDist;
                score += (pad.DroneCount - pad.AvailableDroneCount) * 50f; // Penalty for busy pads
                score += pad.QueuedRequests * 20f; // Penalty for queue

                if (score < bestScore)
                {
                    bestScore = score;
                    bestPad = pad;
                }
            }

            return bestPad;
        }

        #endregion

        #region Route Optimization

        private void OptimizeRoutes()
        {
            // Check active routes and create delivery requests
            foreach (var route in routes)
            {
                if (!route.IsActive)
                    continue;

                var sourceNode = GetNode(route.SourceNodeId);
                var destNode = GetNode(route.DestinationNodeId);

                if (sourceNode == null || destNode == null)
                    continue;

                // Check if destination needs resources
                // (In real implementation, check inventory levels)

                // Don't flood with requests
                if (route.PendingDeliveries >= 3)
                    continue;

                // Create request
                var request = new DeliveryRequest(
                    sourceNode.Position,
                    destNode.Position,
                    route.Resource,
                    route.QuantityPerTrip,
                    route.Priority
                );

                // Find pad and assign
                var pad = FindBestPadForRequest(request);
                if (pad != null && pad.RequestDelivery(request))
                {
                    route.PendingDeliveries++;
                    route.TotalDeliveries++;
                }
            }
        }

        #endregion

        #region Statistics

        public int TotalNodes => nodes.Count;
        public int TotalRoutes => routes.Count;
        public int ActiveRoutes => routes.Count(r => r.IsActive);
        public int PendingRequests => pendingRequests.Count;

        public string GetStatsSummary()
        {
            return $"Nodes: {TotalNodes}, Routes: {ActiveRoutes}/{TotalRoutes}, Pending: {PendingRequests}";
        }

        #endregion
    }

    #region Data Classes

    public enum LogisticsNodeType
    {
        Storage,        // Container/chest
        Machine,        // Production machine
        Packing,        // Packing station
        Unpacking,      // Unpacking station
        TurretAmmo,     // Turret ammo supply point
        Custom          // User-defined
    }

    public class LogisticsNode
    {
        public int Id;
        public Vector3 Position;
        public LogisticsNodeType Type;
        public string Name;
        public bool IsActive = true;

        // Filters
        public List<ResourceInfo> AllowedResources = new List<ResourceInfo>();
        public bool AllowAll => AllowedResources.Count == 0;
    }

    public class DeliveryRoute
    {
        public int SourceNodeId;
        public int DestinationNodeId;
        public ResourceInfo Resource;
        public int QuantityPerTrip;
        public int Priority;
        public bool IsActive;

        // Stats
        public int TotalDeliveries;
        public int PendingDeliveries;
    }

    #endregion
}
