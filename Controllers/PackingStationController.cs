using UnityEngine;
using System.Collections.Generic;

namespace DroneLogistics
{
    /// <summary>
    /// Packing station - packs items into cargo crates for efficient drone transport
    /// When cargo crates are disabled, acts as a simple buffer
    /// </summary>
    public class PackingStationController : MonoBehaviour
    {
        public int StationId { get; private set; }
        private static int nextStationId = 0;

        // Configuration
        public float PackingTime = 2f;
        public int MaxOutputCrates = 8;

        // State
        public bool IsProcessing { get; private set; }
        private float processTimer;

        // Inventory
        private Dictionary<ResourceInfo, int> inputBuffer = new Dictionary<ResourceInfo, int>();
        private List<CargoData> outputBuffer = new List<CargoData>();

        // Current packing operation
        private ResourceInfo currentResource;
        private int currentQuantity;

        // Visual
        private Light statusLight;

        void Awake()
        {
            StationId = nextStationId++;
            SetupVisuals();
        }

        private void SetupVisuals()
        {
            // Status light
            var lightObj = new GameObject("StatusLight");
            lightObj.transform.SetParent(transform);
            lightObj.transform.localPosition = Vector3.up * 1.5f;

            statusLight = lightObj.AddComponent<Light>();
            statusLight.type = LightType.Point;
            statusLight.range = 5f;
            statusLight.intensity = 1f;
            statusLight.color = Color.green;
        }

        void Update()
        {
            if (IsProcessing)
            {
                processTimer -= Time.deltaTime;

                if (processTimer <= 0)
                {
                    FinishPacking();
                }
            }
            else
            {
                // Try to start packing
                TryStartPacking();
            }

            UpdateVisuals();
        }

        #region Packing Logic

        private void TryStartPacking()
        {
            if (outputBuffer.Count >= MaxOutputCrates)
                return; // Output full

            // Find resource with enough to pack
            foreach (var kvp in inputBuffer)
            {
                if (kvp.Value >= kvp.Key.maxStackCount)
                {
                    StartPacking(kvp.Key, kvp.Key.maxStackCount);
                    return;
                }
            }
        }

        private void StartPacking(ResourceInfo resource, int quantity)
        {
            currentResource = resource;
            currentQuantity = quantity;

            // Remove from input
            inputBuffer[resource] -= quantity;
            if (inputBuffer[resource] <= 0)
                inputBuffer.Remove(resource);

            IsProcessing = true;
            processTimer = PackingTime;

            DroneLogisticsPlugin.Log($"Station {StationId} packing {quantity}x {resource.displayName}");
        }

        private void FinishPacking()
        {
            // Create cargo
            var cargo = DroneLogisticsPlugin.PackItems(currentResource, currentQuantity);
            outputBuffer.Add(cargo);

            IsProcessing = false;
            currentResource = null;
            currentQuantity = 0;

            DroneLogisticsPlugin.Log($"Station {StationId} packed cargo ({outputBuffer.Count}/{MaxOutputCrates})");
        }

        #endregion

        #region Input/Output

        public bool AddItems(ResourceInfo resource, int quantity)
        {
            if (!inputBuffer.ContainsKey(resource))
                inputBuffer[resource] = 0;

            inputBuffer[resource] += quantity;
            return true;
        }

        public CargoData TakeCargo()
        {
            if (outputBuffer.Count == 0)
                return null;

            var cargo = outputBuffer[0];
            outputBuffer.RemoveAt(0);
            return cargo;
        }

        public bool HasOutput => outputBuffer.Count > 0;
        public int OutputCount => outputBuffer.Count;
        public int InputTypes => inputBuffer.Count;

        public int GetInputCount(ResourceInfo resource)
        {
            return inputBuffer.TryGetValue(resource, out int count) ? count : 0;
        }

        #endregion

        #region Visuals

        private void UpdateVisuals()
        {
            if (statusLight == null) return;

            if (IsProcessing)
            {
                statusLight.color = Color.yellow;
                statusLight.intensity = 1f + Mathf.Sin(Time.time * 5f) * 0.3f;
            }
            else if (outputBuffer.Count >= MaxOutputCrates)
            {
                statusLight.color = Color.red;
                statusLight.intensity = 1f;
            }
            else if (outputBuffer.Count > 0)
            {
                statusLight.color = Color.green;
                statusLight.intensity = 1f;
            }
            else
            {
                statusLight.color = new Color(0.5f, 0.5f, 0.5f);
                statusLight.intensity = 0.5f;
            }
        }

        #endregion

        void OnDestroy()
        {
            DroneLogisticsPlugin.ActivePackingStations.Remove(this);
        }
    }

    /// <summary>
    /// Unpacking station - unpacks cargo crates back into items
    /// When cargo crates are disabled, acts as a simple buffer
    /// </summary>
    public class UnpackingStationController : MonoBehaviour
    {
        public int StationId { get; private set; }
        private static int nextStationId = 0;

        // Configuration
        public float UnpackingTime = 1f;
        public int MaxInputCrates = 8;

        // State
        public bool IsProcessing { get; private set; }
        private float processTimer;

        // Inventory
        private List<CargoData> inputBuffer = new List<CargoData>();
        private Dictionary<ResourceInfo, int> outputBuffer = new Dictionary<ResourceInfo, int>();

        // Current unpacking operation
        private CargoData currentCargo;

        void Awake()
        {
            StationId = nextStationId++;
        }

        void Update()
        {
            if (IsProcessing)
            {
                processTimer -= Time.deltaTime;

                if (processTimer <= 0)
                {
                    FinishUnpacking();
                }
            }
            else
            {
                TryStartUnpacking();
            }
        }

        private void TryStartUnpacking()
        {
            if (inputBuffer.Count == 0)
                return;

            currentCargo = inputBuffer[0];
            inputBuffer.RemoveAt(0);

            IsProcessing = true;
            processTimer = UnpackingTime;
        }

        private void FinishUnpacking()
        {
            if (currentCargo != null && currentCargo.ResourceType != null)
            {
                if (!outputBuffer.ContainsKey(currentCargo.ResourceType))
                    outputBuffer[currentCargo.ResourceType] = 0;

                outputBuffer[currentCargo.ResourceType] += currentCargo.Quantity;
            }

            IsProcessing = false;
            currentCargo = null;
        }

        public bool AddCargo(CargoData cargo)
        {
            if (inputBuffer.Count >= MaxInputCrates)
                return false;

            inputBuffer.Add(cargo);
            return true;
        }

        public int TakeItems(ResourceInfo resource, int maxQuantity)
        {
            if (!outputBuffer.TryGetValue(resource, out int available))
                return 0;

            int toTake = Mathf.Min(available, maxQuantity);
            outputBuffer[resource] -= toTake;

            if (outputBuffer[resource] <= 0)
                outputBuffer.Remove(resource);

            return toTake;
        }

        public bool HasResource(ResourceInfo resource)
        {
            return outputBuffer.ContainsKey(resource) && outputBuffer[resource] > 0;
        }

        public int GetAvailable(ResourceInfo resource)
        {
            return outputBuffer.TryGetValue(resource, out int count) ? count : 0;
        }
    }
}
