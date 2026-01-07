using UnityEngine;
using System.Collections.Generic;

namespace DroneLogistics
{
    /// <summary>
    /// Drone landing pad / relay - spawns, charges, and manages drones
    /// Now supports power-based charging with sci-fi models
    /// </summary>
    public class DronePadController : MonoBehaviour
    {
        public int PadId { get; private set; }
        private static int nextPadId = 0;

        // Configuration
        public int MaxDrones => DroneLogisticsPlugin.MaxDronesPerPad.Value;
        public DroneRelayType RelayType { get; private set; } = DroneRelayType.Standard;

        // Power system
        public bool RequiresPower { get; set; } = false;
        public int PowerDraw { get; set; } = 50;
        public bool IsPowered { get; private set; } = true;
        public bool IsCharging => chargingDrones.Count > 0 && (IsPowered || !RequiresPower);
        public float ChargeMultiplier => RelayType switch
        {
            DroneRelayType.FastCharge => 2f,
            DroneRelayType.DockingBay => 1.5f,
            _ => 1f
        };
        public int MaxChargingSlots => RelayType switch
        {
            DroneRelayType.DockingBay => 4,
            DroneRelayType.FastCharge => 2,
            _ => 1
        };

        // Managed drones
        private List<DroneController> drones = new List<DroneController>();
        private List<DroneController> chargingDrones = new List<DroneController>();
        private Queue<DroneController> chargingQueue = new Queue<DroneController>();
        public IReadOnlyList<DroneController> Drones => drones;
        public int DroneCount => drones.Count;
        public int AvailableDroneCount => drones.FindAll(d => d != null && d.IsAvailable).Count;
        public int ChargingCount => chargingDrones.Count;
        public int QueuedForCharging => chargingQueue.Count;

        // Docking positions for multi-drone bays
        private Vector3[] dockingPositions;

        // Queued requests
        private Queue<DeliveryRequest> requestQueue = new Queue<DeliveryRequest>();
        public int QueuedRequests => requestQueue.Count;

        // Visual
        private Light padLight;
        private LineRenderer rangeIndicator;

        public void Initialize()
        {
            Initialize(DroneRelayType.Standard);
        }

        public void Initialize(DroneRelayType type)
        {
            PadId = nextPadId++;
            RelayType = type;

            SetupDockingPositions();
            SetupVisuals();

            DroneLogisticsPlugin.Log($"Drone Relay {PadId} ({type}) initialized - {MaxChargingSlots} charging slots");
        }

        private void SetupDockingPositions()
        {
            // Create docking positions based on relay type
            int slots = MaxChargingSlots;
            dockingPositions = new Vector3[slots];

            float radius = RelayType == DroneRelayType.DockingBay ? 2f : 1f;
            float height = 0.5f;

            for (int i = 0; i < slots; i++)
            {
                float angle = (i * 360f / slots) * Mathf.Deg2Rad;
                dockingPositions[i] = new Vector3(
                    Mathf.Cos(angle) * radius,
                    height,
                    Mathf.Sin(angle) * radius
                );
            }
        }

        private void SetupVisuals()
        {
            // Add landing light
            var lightObj = new GameObject("PadLight");
            lightObj.transform.SetParent(transform);
            lightObj.transform.localPosition = Vector3.up * 0.5f;

            padLight = lightObj.AddComponent<Light>();
            padLight.type = LightType.Point;
            padLight.range = 10f;
            padLight.intensity = 2f;
            padLight.color = new Color(0.3f, 0.5f, 1f);

            // Add range indicator
            CreateRangeIndicator();
        }

        private void CreateRangeIndicator()
        {
            var rangeObj = new GameObject("RangeIndicator");
            rangeObj.transform.SetParent(transform);
            rangeObj.transform.localPosition = Vector3.up * 0.1f;

            rangeIndicator = rangeObj.AddComponent<LineRenderer>();
            rangeIndicator.useWorldSpace = false;
            rangeIndicator.startWidth = 0.1f;
            rangeIndicator.endWidth = 0.1f;
            rangeIndicator.material = DroneLogisticsPlugin.GetEffectMaterial(new Color(0.3f, 0.5f, 1f, 0.3f));
            rangeIndicator.loop = true;

            // Draw circle
            int segments = 64;
            rangeIndicator.positionCount = segments + 1;
            float range = DroneLogisticsPlugin.DroneRange.Value;

            for (int i = 0; i <= segments; i++)
            {
                float angle = i * (360f / segments) * Mathf.Deg2Rad;
                float x = Mathf.Cos(angle) * range;
                float z = Mathf.Sin(angle) * range;
                rangeIndicator.SetPosition(i, new Vector3(x, 0, z));
            }

            // Start hidden
            rangeIndicator.enabled = false;
        }

        void Update()
        {
            // Clean up destroyed drones
            drones.RemoveAll(d => d == null);
            chargingDrones.RemoveAll(d => d == null);

            // Process charging
            ProcessCharging();

            // Process queued requests
            ProcessQueue();

            // Update visuals
            UpdateVisuals();
        }

        private void ProcessCharging()
        {
            // Only charge if powered (or power not required)
            if (RequiresPower && !IsPowered)
                return;

            float chargeAmount = DroneLogisticsPlugin.DroneChargeRate.Value * ChargeMultiplier * Time.deltaTime;
            float batteryCapacity = DroneLogisticsPlugin.DroneBatteryCapacity.Value;

            // Process drones currently in charging slots
            for (int i = chargingDrones.Count - 1; i >= 0; i--)
            {
                var drone = chargingDrones[i];
                if (drone == null)
                {
                    chargingDrones.RemoveAt(i);
                    continue;
                }

                // Charge the drone
                drone.AddCharge(chargeAmount / batteryCapacity);

                // Check if fully charged
                if (drone.Charge >= 1f)
                {
                    drone.OnChargingComplete();
                    chargingDrones.RemoveAt(i);
                    DroneLogisticsPlugin.Log($"Drone {drone.DroneId} fully charged at Relay {PadId}");
                }
            }

            // Fill empty charging slots from queue
            while (chargingDrones.Count < MaxChargingSlots && chargingQueue.Count > 0)
            {
                var nextDrone = chargingQueue.Dequeue();
                if (nextDrone != null)
                {
                    int slotIndex = chargingDrones.Count;
                    chargingDrones.Add(nextDrone);
                    nextDrone.OnDockingAssigned(GetDockingPosition(slotIndex));
                    DroneLogisticsPlugin.Log($"Drone {nextDrone.DroneId} assigned to charging slot {slotIndex} at Relay {PadId}");
                }
            }
        }

        public Vector3 GetDockingPosition(int slotIndex)
        {
            if (dockingPositions == null || slotIndex >= dockingPositions.Length)
                return transform.position + Vector3.up * 0.5f;

            return transform.position + dockingPositions[slotIndex];
        }

        private void ProcessQueue()
        {
            while (requestQueue.Count > 0)
            {
                // Find available drone
                var available = drones.Find(d => d != null && d.IsAvailable);

                if (available == null)
                    break; // No drones available

                var request = requestQueue.Peek();

                if (available.AssignDelivery(request))
                {
                    requestQueue.Dequeue();
                }
                else
                {
                    break; // Request couldn't be assigned (too far, etc.)
                }
            }
        }

        private void UpdateVisuals()
        {
            if (padLight != null)
            {
                // Pulse when drones are charging
                bool hasCharging = drones.Exists(d => d != null && d.Charge < 1f);
                if (hasCharging)
                {
                    padLight.intensity = 1.5f + Mathf.Sin(Time.time * 3f) * 0.5f;
                    padLight.color = new Color(1f, 0.8f, 0.3f);
                }
                else
                {
                    padLight.intensity = 2f;
                    padLight.color = new Color(0.3f, 0.5f, 1f);
                }
            }
        }

        #region Charging Management

        /// <summary>
        /// Request a charging slot for a drone. Returns true if queued/assigned.
        /// </summary>
        public bool RequestCharging(DroneController drone)
        {
            if (drone == null) return false;

            // Already charging or queued?
            if (chargingDrones.Contains(drone) || chargingQueue.Contains(drone))
                return true;

            // If slot available, assign immediately
            if (chargingDrones.Count < MaxChargingSlots)
            {
                int slotIndex = chargingDrones.Count;
                chargingDrones.Add(drone);
                drone.OnDockingAssigned(GetDockingPosition(slotIndex));
                DroneLogisticsPlugin.Log($"Drone {drone.DroneId} immediately assigned to charging slot {slotIndex}");
                return true;
            }

            // Otherwise queue
            chargingQueue.Enqueue(drone);
            DroneLogisticsPlugin.Log($"Drone {drone.DroneId} queued for charging (queue: {chargingQueue.Count})");
            return true;
        }

        /// <summary>
        /// Cancel a charging request
        /// </summary>
        public void CancelCharging(DroneController drone)
        {
            if (chargingDrones.Contains(drone))
            {
                chargingDrones.Remove(drone);
            }

            // Remove from queue
            var newQueue = new Queue<DroneController>();
            while (chargingQueue.Count > 0)
            {
                var d = chargingQueue.Dequeue();
                if (d != drone)
                    newQueue.Enqueue(d);
            }
            chargingQueue = newQueue;
        }

        /// <summary>
        /// Check if this relay has available charging capacity
        /// </summary>
        public bool HasChargingCapacity => chargingDrones.Count < MaxChargingSlots || chargingQueue.Count < 10;

        /// <summary>
        /// Get estimated wait time for charging (in seconds)
        /// </summary>
        public float EstimatedWaitTime
        {
            get
            {
                if (chargingDrones.Count < MaxChargingSlots)
                    return 0f;

                // Estimate based on queue position and average charge time
                float avgChargeTime = DroneLogisticsPlugin.DroneBatteryCapacity.Value /
                                     (DroneLogisticsPlugin.DroneChargeRate.Value * ChargeMultiplier);
                return chargingQueue.Count * avgChargeTime / MaxChargingSlots;
            }
        }

        #endregion

        #region Drone Management

        public DroneController SpawnDrone(DroneType type)
        {
            if (DroneCount >= MaxDrones)
            {
                DroneLogisticsPlugin.LogWarning($"Pad {PadId} at max capacity ({MaxDrones} drones)");
                return null;
            }

            Vector3 spawnPos = transform.position + Vector3.up * 2f;
            spawnPos += Random.insideUnitSphere * 1f;
            spawnPos.y = transform.position.y + 2f;

            var drone = DroneLogisticsPlugin.SpawnDrone(type, spawnPos, this);
            if (drone != null)
            {
                drones.Add(drone);
                DroneLogisticsPlugin.Log($"Pad {PadId} spawned drone (total: {DroneCount})");
            }

            return drone;
        }

        public void RemoveDrone(DroneController drone)
        {
            if (drones.Contains(drone))
            {
                drones.Remove(drone);
                if (drone != null)
                {
                    Destroy(drone.gameObject);
                }
            }
        }

        public void RecallAllDrones()
        {
            foreach (var drone in drones)
            {
                if (drone != null)
                {
                    drone.RecallToBase();
                }
            }
        }

        #endregion

        #region Delivery Requests

        public bool RequestDelivery(DeliveryRequest request)
        {
            // Check if within range
            float pickupDist = Vector3.Distance(transform.position, request.PickupLocation);
            float deliveryDist = Vector3.Distance(request.PickupLocation, request.DeliveryLocation);

            if (pickupDist > DroneLogisticsPlugin.DroneRange.Value ||
                deliveryDist > DroneLogisticsPlugin.DroneRange.Value)
            {
                return false; // Out of range
            }

            // Try to assign immediately
            var available = drones.Find(d => d != null && d.IsAvailable);
            if (available != null && available.AssignDelivery(request))
            {
                return true;
            }

            // Queue for later
            requestQueue.Enqueue(request);
            DroneLogisticsPlugin.Log($"Pad {PadId} queued delivery (queue: {QueuedRequests})");
            return true;
        }

        public void CancelRequest(DeliveryRequest request)
        {
            // Convert to list, remove, convert back
            var list = new List<DeliveryRequest>(requestQueue);
            list.Remove(request);
            requestQueue = new Queue<DeliveryRequest>(list);
        }

        public void ClearQueue()
        {
            requestQueue.Clear();
        }

        #endregion

        #region UI Helpers

        public void ShowRange(bool show)
        {
            if (rangeIndicator != null)
            {
                rangeIndicator.enabled = show;
            }
        }

        public string GetStatusText()
        {
            int available = AvailableDroneCount;
            int working = drones.FindAll(d => d != null && d.State != DroneState.Idle && d.State != DroneState.Charging).Count;

            string powerStatus = RequiresPower ? (IsPowered ? "Powered" : "No Power!") : "";
            string chargingStatus = $"{ChargingCount}/{MaxChargingSlots} charging";
            if (QueuedForCharging > 0)
                chargingStatus += $" (+{QueuedForCharging} queued)";

            return $"Relay {PadId} ({RelayType}): {available}/{DroneCount} available, {working} working, {chargingStatus} {powerStatus}";
        }

        #endregion

        void OnDestroy()
        {
            // Recall all drones before destroying
            foreach (var drone in drones)
            {
                if (drone != null)
                {
                    drone.HomePad = null;
                    drone.RecallToBase();
                }
            }

            DroneLogisticsPlugin.ActivePads.Remove(this);
        }
    }
}
