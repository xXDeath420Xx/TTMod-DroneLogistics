using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace DroneLogistics
{
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInDependency("com.equinox.EquinoxsModUtils", BepInDependency.DependencyFlags.HardDependency)]
    public class DroneLogisticsPlugin : BaseUnityPlugin
    {
        public const string GUID = "com.certifried.dronelogistics";
        public const string NAME = "DroneLogistics";
        public const string VERSION = "1.1.0";

        private static DroneLogisticsPlugin instance;
        private Harmony harmony;

        // Configuration
        public static ConfigEntry<int> MaxDronesPerPad;
        public static ConfigEntry<float> DroneSpeed;
        public static ConfigEntry<float> DroneRange;
        public static ConfigEntry<int> DroneCapacity;
        public static ConfigEntry<bool> UseCargoCrates;
        public static ConfigEntry<float> ChargingTime;
        public static ConfigEntry<bool> EnableBiofuel;
        public static ConfigEntry<int> RelayPowerDraw;
        public static ConfigEntry<float> DroneChargeRate;
        public static ConfigEntry<float> DroneBatteryCapacity;

        // Asset Bundles
        private static Dictionary<string, AssetBundle> loadedBundles = new Dictionary<string, AssetBundle>();
        private static Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();

        // Active systems
        public static List<DronePadController> ActivePads = new List<DronePadController>();
        public static List<DroneController> ActiveDrones = new List<DroneController>();
        public static List<PackingStationController> ActivePackingStations = new List<PackingStationController>();

        // Material cache for URP compatibility
        private static Material cachedMaterial;
        private static Material cachedEffectMaterial;

        // Route management
        public static RouteManager Routes { get; private set; }

        void Awake()
        {
            instance = this;
            Logger.LogInfo($"{NAME} v{VERSION} loading...");

            InitializeConfig();
            LoadAssetBundles();

            harmony = new Harmony(GUID);
            harmony.PatchAll();

            Routes = new RouteManager();

            Logger.LogInfo($"{NAME} loaded successfully!");
        }

        private void InitializeConfig()
        {
            MaxDronesPerPad = Config.Bind("Drones", "MaxDronesPerPad", 4,
                "Maximum number of drones that can operate from a single pad");

            DroneSpeed = Config.Bind("Drones", "DroneSpeed", 15f,
                "Base drone flight speed (m/s)");

            DroneRange = Config.Bind("Drones", "DroneRange", 200f,
                "Maximum drone operating range from pad (meters)");

            DroneCapacity = Config.Bind("Drones", "DroneCapacity", 1,
                "Number of stacks/crates a drone can carry");

            UseCargoCrates = Config.Bind("Advanced", "UseCargoCrates", false,
                "Enable cargo crate system (requires packing stations). When disabled, drones carry items directly.");

            ChargingTime = Config.Bind("Drones", "ChargingTime", 10f,
                "Time in seconds for a drone to fully recharge");

            EnableBiofuel = Config.Bind("Advanced", "EnableBiofuel", false,
                "Enable biofuel-powered drones (requires BioProcessing mod)");

            RelayPowerDraw = Config.Bind("Power", "RelayPowerDraw", 50,
                "Power draw (kW) when a relay is actively charging drones");

            DroneChargeRate = Config.Bind("Power", "DroneChargeRate", 10f,
                "Battery charge per second when docked at a powered relay");

            DroneBatteryCapacity = Config.Bind("Power", "DroneBatteryCapacity", 100f,
                "Maximum battery capacity for drones");
        }

        private void LoadAssetBundles()
        {
            string bundlePath = Path.Combine(Path.GetDirectoryName(Info.Location), "Bundles");

            if (!Directory.Exists(bundlePath))
            {
                Logger.LogWarning($"Bundles folder not found at {bundlePath}");
                return;
            }

            string[] bundleNames = {
                "drones_voodooplay", "drones_scifi", "drones_simple", "robot_sphere",
                "scifi_machines", "icons_skymon"  // Charging stations and battery icons
            };

            foreach (var bundleName in bundleNames)
            {
                string fullPath = Path.Combine(bundlePath, bundleName);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        var bundle = AssetBundle.LoadFromFile(fullPath);
                        if (bundle != null)
                        {
                            loadedBundles[bundleName] = bundle;
                            Logger.LogInfo($"Loaded bundle: {bundleName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to load bundle {bundleName}: {ex.Message}");
                    }
                }
            }
        }

        void Update()
        {
            // Clean up destroyed objects
            ActivePads.RemoveAll(p => p == null);
            ActiveDrones.RemoveAll(d => d == null);
            ActivePackingStations.RemoveAll(s => s == null);
        }

        void OnDestroy()
        {
            harmony?.UnpatchSelf();

            foreach (var bundle in loadedBundles.Values)
            {
                bundle?.Unload(true);
            }
            loadedBundles.Clear();
        }

        #region Asset Loading

        public static GameObject GetPrefab(string bundleName, string prefabName)
        {
            string key = $"{bundleName}/{prefabName}";

            if (prefabCache.TryGetValue(key, out var cached))
                return cached;

            if (!loadedBundles.TryGetValue(bundleName, out var bundle))
            {
                instance?.Logger.LogWarning($"Bundle not loaded: {bundleName}");
                return null;
            }

            // Try exact name
            var prefab = bundle.LoadAsset<GameObject>(prefabName);

            // Try with .prefab extension
            if (prefab == null)
                prefab = bundle.LoadAsset<GameObject>(prefabName + ".prefab");

            // Search for partial match in asset names
            if (prefab == null)
            {
                foreach (var assetName in bundle.GetAllAssetNames())
                {
                    if (assetName.ToLower().Contains(prefabName.ToLower()) && assetName.EndsWith(".prefab"))
                    {
                        prefab = bundle.LoadAsset<GameObject>(assetName);
                        if (prefab != null)
                        {
                            instance?.Logger.LogInfo($"Found prefab {prefabName} as {assetName}");
                            break;
                        }
                    }
                }
            }

            if (prefab != null)
            {
                prefabCache[key] = prefab;
            }

            return prefab;
        }

        /// <summary>
        /// Fix materials on imported prefabs to be URP compatible
        /// Preserves albedo, normal, metallic, emission, and other texture maps
        /// </summary>
        public static void FixPrefabMaterials(GameObject obj)
        {
            if (cachedMaterial == null)
            {
                // Find a valid URP material from the game
                var gameRenderers = FindObjectsOfType<Renderer>();
                foreach (var r in gameRenderers)
                {
                    if (r.material != null && r.material.shader != null &&
                        r.material.shader.name.Contains("Universal"))
                    {
                        cachedMaterial = r.material;
                        break;
                    }
                }
            }

            if (cachedMaterial == null) return;

            var renderers = obj.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var materials = renderer.materials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var oldMat = materials[i];
                    if (oldMat != null && oldMat.shader != null)
                    {
                        // Check if shader is broken (not URP compatible)
                        if (!oldMat.shader.name.Contains("Universal") &&
                            !oldMat.shader.name.Contains("URP"))
                        {
                            // Extract all texture properties from old material
                            Color originalColor = Color.white;
                            Texture mainTex = null;
                            Texture normalMap = null;
                            Texture metallicMap = null;
                            Texture emissionMap = null;
                            Texture occlusionMap = null;
                            Color emissionColor = Color.black;
                            float metallic = 0f;
                            float smoothness = 0.5f;
                            float normalScale = 1f;

                            // Preserve color
                            if (oldMat.HasProperty("_Color"))
                                originalColor = oldMat.GetColor("_Color");
                            else if (oldMat.HasProperty("_BaseColor"))
                                originalColor = oldMat.GetColor("_BaseColor");
                            else
                                originalColor = oldMat.color;

                            // Preserve albedo texture
                            if (oldMat.HasProperty("_MainTex"))
                                mainTex = oldMat.GetTexture("_MainTex");
                            else if (oldMat.HasProperty("_BaseMap"))
                                mainTex = oldMat.GetTexture("_BaseMap");
                            else if (oldMat.HasProperty("_Albedo"))
                                mainTex = oldMat.GetTexture("_Albedo");

                            // Preserve normal map
                            if (oldMat.HasProperty("_BumpMap"))
                                normalMap = oldMat.GetTexture("_BumpMap");
                            else if (oldMat.HasProperty("_NormalMap"))
                                normalMap = oldMat.GetTexture("_NormalMap");
                            if (oldMat.HasProperty("_BumpScale"))
                                normalScale = oldMat.GetFloat("_BumpScale");

                            // Preserve metallic/smoothness
                            if (oldMat.HasProperty("_MetallicGlossMap"))
                                metallicMap = oldMat.GetTexture("_MetallicGlossMap");
                            else if (oldMat.HasProperty("_MetallicMap"))
                                metallicMap = oldMat.GetTexture("_MetallicMap");
                            if (oldMat.HasProperty("_Metallic"))
                                metallic = oldMat.GetFloat("_Metallic");
                            if (oldMat.HasProperty("_Glossiness"))
                                smoothness = oldMat.GetFloat("_Glossiness");
                            else if (oldMat.HasProperty("_Smoothness"))
                                smoothness = oldMat.GetFloat("_Smoothness");

                            // Preserve emission
                            if (oldMat.HasProperty("_EmissionMap"))
                                emissionMap = oldMat.GetTexture("_EmissionMap");
                            if (oldMat.HasProperty("_EmissionColor"))
                                emissionColor = oldMat.GetColor("_EmissionColor");

                            // Preserve occlusion
                            if (oldMat.HasProperty("_OcclusionMap"))
                                occlusionMap = oldMat.GetTexture("_OcclusionMap");

                            // Create new URP material
                            var newMat = new Material(cachedMaterial);
                            newMat.color = originalColor;

                            // Apply color
                            if (newMat.HasProperty("_BaseColor"))
                                newMat.SetColor("_BaseColor", originalColor);
                            if (newMat.HasProperty("_Color"))
                                newMat.SetColor("_Color", originalColor);

                            // Apply albedo
                            if (mainTex != null)
                            {
                                if (newMat.HasProperty("_MainTex"))
                                    newMat.SetTexture("_MainTex", mainTex);
                                if (newMat.HasProperty("_BaseMap"))
                                    newMat.SetTexture("_BaseMap", mainTex);
                            }

                            // Apply normal map
                            if (normalMap != null && newMat.HasProperty("_BumpMap"))
                            {
                                newMat.SetTexture("_BumpMap", normalMap);
                                newMat.SetFloat("_BumpScale", normalScale);
                                newMat.EnableKeyword("_NORMALMAP");
                            }

                            // Apply metallic/smoothness
                            if (metallicMap != null && newMat.HasProperty("_MetallicGlossMap"))
                            {
                                newMat.SetTexture("_MetallicGlossMap", metallicMap);
                                newMat.EnableKeyword("_METALLICGLOSSMAP");
                            }
                            if (newMat.HasProperty("_Metallic"))
                                newMat.SetFloat("_Metallic", metallic);
                            if (newMat.HasProperty("_Smoothness"))
                                newMat.SetFloat("_Smoothness", smoothness);
                            if (newMat.HasProperty("_Glossiness"))
                                newMat.SetFloat("_Glossiness", smoothness);

                            // Apply emission
                            if (emissionMap != null || emissionColor != Color.black)
                            {
                                if (newMat.HasProperty("_EmissionMap") && emissionMap != null)
                                    newMat.SetTexture("_EmissionMap", emissionMap);
                                if (newMat.HasProperty("_EmissionColor"))
                                {
                                    newMat.SetColor("_EmissionColor", emissionColor);
                                    if (emissionColor != Color.black || emissionMap != null)
                                        newMat.EnableKeyword("_EMISSION");
                                }
                            }

                            // Apply occlusion
                            if (occlusionMap != null && newMat.HasProperty("_OcclusionMap"))
                            {
                                newMat.SetTexture("_OcclusionMap", occlusionMap);
                                newMat.EnableKeyword("_OCCLUSIONMAP");
                            }

                            materials[i] = newMat;
                        }
                    }
                }
                renderer.materials = materials;
            }
        }

        public static Material GetEffectMaterial(Color color)
        {
            if (cachedEffectMaterial == null)
            {
                // Try to use cached material shader first (URP compatible)
                if (cachedMaterial != null)
                {
                    cachedEffectMaterial = new Material(cachedMaterial);
                }
                else
                {
                    // Find a URP shader from game
                    var gameRenderers = FindObjectsOfType<Renderer>();
                    foreach (var r in gameRenderers)
                    {
                        if (r.material != null && r.material.shader != null &&
                            (r.material.shader.name.Contains("Universal") || r.material.shader.name.Contains("URP")))
                        {
                            cachedEffectMaterial = new Material(r.material);
                            break;
                        }
                    }
                    // Last resort fallback
                    if (cachedEffectMaterial == null)
                    {
                        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
                        if (shader != null)
                            cachedEffectMaterial = new Material(shader);
                        else
                            cachedEffectMaterial = new Material(Shader.Find("Sprites/Default"));
                    }
                }
            }

            var mat = new Material(cachedEffectMaterial);
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            return mat;
        }

        /// <summary>
        /// Create a fallback primitive drone when prefab loading fails
        /// </summary>
        private static GameObject CreatePrimitiveDrone(DroneType type)
        {
            GameObject drone = new GameObject($"FallbackDrone_{type}");

            Color droneColor = type switch
            {
                DroneType.Scout => new Color(0.3f, 0.5f, 1f),      // Blue
                DroneType.Cargo => new Color(0.8f, 0.6f, 0.2f),    // Orange
                DroneType.HeavyLifter => new Color(0.4f, 0.4f, 0.5f), // Gray
                DroneType.Combat => new Color(1f, 0.3f, 0.3f),     // Red
                _ => new Color(0.5f, 0.5f, 0.5f)
            };

            // Body - sphere
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.transform.SetParent(drone.transform);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(0.8f, 0.4f, 0.8f);
            body.GetComponent<Renderer>().material = GetEffectMaterial(droneColor);
            UnityEngine.Object.Destroy(body.GetComponent<Collider>());

            // Top dome
            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.transform.SetParent(drone.transform);
            dome.transform.localPosition = Vector3.up * 0.15f;
            dome.transform.localScale = Vector3.one * 0.4f;
            dome.GetComponent<Renderer>().material = GetEffectMaterial(droneColor * 1.2f);
            UnityEngine.Object.Destroy(dome.GetComponent<Collider>());

            // Propeller arms (4 arms)
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f * Mathf.Deg2Rad;
                Vector3 armPos = new Vector3(Mathf.Cos(angle) * 0.5f, 0, Mathf.Sin(angle) * 0.5f);

                var arm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                arm.transform.SetParent(drone.transform);
                arm.transform.localPosition = armPos * 0.5f;
                arm.transform.localScale = new Vector3(0.1f, 0.25f, 0.1f);
                arm.transform.localRotation = Quaternion.Euler(0, 0, 90f) * Quaternion.Euler(0, i * 90f, 0);
                arm.GetComponent<Renderer>().material = GetEffectMaterial(new Color(0.3f, 0.3f, 0.3f));
                UnityEngine.Object.Destroy(arm.GetComponent<Collider>());

                // Rotor
                var rotor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                rotor.transform.SetParent(drone.transform);
                rotor.transform.localPosition = armPos;
                rotor.transform.localScale = new Vector3(0.3f, 0.02f, 0.3f);
                rotor.GetComponent<Renderer>().material = GetEffectMaterial(new Color(0.2f, 0.2f, 0.2f, 0.5f));
                UnityEngine.Object.Destroy(rotor.GetComponent<Collider>());
            }

            // Add light
            var lightObj = new GameObject("DroneLight");
            lightObj.transform.SetParent(drone.transform);
            lightObj.transform.localPosition = Vector3.down * 0.2f;
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 5f;
            light.intensity = 1f;
            light.color = droneColor;

            return drone;
        }

        /// <summary>
        /// Create a fallback primitive relay when prefab loading fails
        /// </summary>
        private static GameObject CreatePrimitiveRelay(DroneRelayType type)
        {
            GameObject relay = new GameObject($"FallbackRelay_{type}");

            Color relayColor = type switch
            {
                DroneRelayType.Standard => new Color(0.2f, 0.4f, 0.8f),     // Blue
                DroneRelayType.FastCharge => new Color(1f, 0.8f, 0.2f),     // Yellow (fast/power)
                DroneRelayType.DockingBay => new Color(0.3f, 0.7f, 0.4f),   // Green
                DroneRelayType.Hub => new Color(0.6f, 0.3f, 0.8f),          // Purple
                _ => new Color(0.4f, 0.4f, 0.6f)
            };

            // Base platform
            var baseObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseObj.transform.SetParent(relay.transform);
            baseObj.transform.localPosition = Vector3.up * 0.1f;
            baseObj.transform.localScale = new Vector3(3f, 0.2f, 3f);
            baseObj.GetComponent<Renderer>().material = GetEffectMaterial(new Color(0.3f, 0.3f, 0.35f));
            UnityEngine.Object.Destroy(baseObj.GetComponent<Collider>());

            // Central column
            var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            column.transform.SetParent(relay.transform);
            column.transform.localPosition = Vector3.up * 0.8f;
            column.transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);
            column.GetComponent<Renderer>().material = GetEffectMaterial(relayColor);
            UnityEngine.Object.Destroy(column.GetComponent<Collider>());

            // Top emitter
            var emitter = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            emitter.transform.SetParent(relay.transform);
            emitter.transform.localPosition = Vector3.up * 1.5f;
            emitter.transform.localScale = Vector3.one * 0.6f;
            emitter.GetComponent<Renderer>().material = GetEffectMaterial(relayColor * 1.5f);
            UnityEngine.Object.Destroy(emitter.GetComponent<Collider>());

            // Charging pads around base (for DockingBay type, add more)
            int padCount = type == DroneRelayType.DockingBay ? 4 : 2;
            for (int i = 0; i < padCount; i++)
            {
                float angle = (i / (float)padCount) * Mathf.PI * 2f;
                Vector3 padPos = new Vector3(Mathf.Cos(angle) * 1.2f, 0.15f, Mathf.Sin(angle) * 1.2f);

                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.transform.SetParent(relay.transform);
                pad.transform.localPosition = padPos;
                pad.transform.localScale = new Vector3(0.5f, 0.1f, 0.5f);
                pad.GetComponent<Renderer>().material = GetEffectMaterial(relayColor * 0.7f);
                UnityEngine.Object.Destroy(pad.GetComponent<Collider>());
            }

            // Add light
            var lightObj = new GameObject("RelayLight");
            lightObj.transform.SetParent(relay.transform);
            lightObj.transform.localPosition = Vector3.up * 1.5f;
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 10f;
            light.intensity = 2f;
            light.color = relayColor;

            return relay;
        }

        #endregion

        #region Spawning

        public static DroneController SpawnDrone(DroneType type, Vector3 position, DronePadController homePad)
        {
            string bundleName = "drones_simple";
            string prefabName = "drone";

            switch (type)
            {
                case DroneType.Scout:
                    bundleName = "drones_simple";
                    prefabName = "drone blue";
                    break;
                case DroneType.Cargo:
                    bundleName = "drones_voodooplay";
                    prefabName = "Drone";
                    break;
                case DroneType.HeavyLifter:
                    bundleName = "drones_scifi";
                    prefabName = "Robot_Collector";
                    break;
                case DroneType.Combat:
                    bundleName = "drones_scifi";
                    prefabName = "Robot_Guardian";
                    break;
            }

            GameObject droneObj = null;
            var prefab = GetPrefab(bundleName, prefabName);

            if (prefab != null)
            {
                droneObj = Instantiate(prefab, position, Quaternion.identity);
                FixPrefabMaterials(droneObj);
            }
            else
            {
                // Fallback: create primitive drone
                instance?.Logger.LogWarning($"Could not find drone prefab: {bundleName}/{prefabName} - using fallback");
                droneObj = CreatePrimitiveDrone(type);
                droneObj.transform.position = position;
            }

            droneObj.name = $"Drone_{type}_{ActiveDrones.Count}";

            var drone = droneObj.AddComponent<DroneController>();
            drone.Initialize(type, homePad);

            ActiveDrones.Add(drone);

            return drone;
        }

        public static DronePadController SpawnDronePad(Vector3 position, DroneRelayType relayType = DroneRelayType.Standard)
        {
            GameObject padObj = null;

            // Try to load proper sci-fi model based on relay type
            string bundleName = "scifi_machines";
            string prefabName = relayType switch
            {
                DroneRelayType.Standard => "Battery",          // Standard charging station
                DroneRelayType.FastCharge => "Battery_big",    // Fast charging, higher power
                DroneRelayType.DockingBay => "torpedo_docking_station_1_side", // Multi-drone bay
                DroneRelayType.Hub => "computer_station",      // Control hub
                _ => "Battery"
            };

            var prefab = GetPrefab(bundleName, prefabName);
            if (prefab != null)
            {
                padObj = Instantiate(prefab, position, Quaternion.identity);
                FixPrefabMaterials(padObj);
            }
            else
            {
                // Fallback to better primitive if bundle not loaded
                instance?.Logger.LogWarning($"Could not find relay prefab: {bundleName}/{prefabName} - using fallback");
                padObj = CreatePrimitiveRelay(relayType);
            }

            padObj.transform.position = position;
            padObj.name = $"DroneRelay_{relayType}_{ActivePads.Count}";

            // Add collider if not present
            if (padObj.GetComponent<Collider>() == null)
            {
                var box = padObj.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(2f, 3f, 2f);
            }

            var pad = padObj.AddComponent<DronePadController>();
            pad.Initialize(relayType);

            ActivePads.Add(pad);

            return pad;
        }

        /// <summary>
        /// Spawn a drone relay connected to a power network
        /// </summary>
        public static DronePadController SpawnPoweredRelay(Vector3 position, DroneRelayType relayType = DroneRelayType.Standard)
        {
            var relay = SpawnDronePad(position, relayType);
            if (relay != null)
            {
                relay.RequiresPower = true;
                relay.PowerDraw = relayType switch
                {
                    DroneRelayType.Standard => RelayPowerDraw.Value,
                    DroneRelayType.FastCharge => RelayPowerDraw.Value * 2,
                    DroneRelayType.DockingBay => RelayPowerDraw.Value * 3,
                    DroneRelayType.Hub => RelayPowerDraw.Value / 2, // Hub is control, less power
                    _ => RelayPowerDraw.Value
                };
            }
            return relay;
        }

        #endregion

        #region Cargo System

        /// <summary>
        /// Pack items into a cargo crate (if UseCargoCrates enabled)
        /// Otherwise just return the items as-is for direct transport
        /// </summary>
        public static CargoData PackItems(ResourceInfo resource, int quantity)
        {
            var cargo = new CargoData
            {
                ResourceType = resource,
                Quantity = quantity,
                IsCrate = UseCargoCrates.Value
            };

            if (UseCargoCrates.Value)
            {
                // Crates hold 2 stacks worth
                cargo.MaxQuantity = resource.maxStackCount * 2;
            }
            else
            {
                // Direct transport uses 1 stack
                cargo.MaxQuantity = resource.maxStackCount;
            }

            cargo.Quantity = Mathf.Min(quantity, cargo.MaxQuantity);

            return cargo;
        }

        #endregion

        #region Utility

        public static DronePadController FindNearestPad(Vector3 position)
        {
            DronePadController nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var pad in ActivePads)
            {
                if (pad == null) continue;
                float dist = Vector3.Distance(position, pad.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = pad;
                }
            }

            return nearest;
        }

        public static DroneController FindAvailableDrone(Vector3 position, float maxRange = -1)
        {
            if (maxRange < 0) maxRange = DroneRange.Value;

            foreach (var drone in ActiveDrones)
            {
                if (drone == null || !drone.IsAvailable) continue;

                float dist = Vector3.Distance(position, drone.transform.position);
                if (dist <= maxRange)
                {
                    return drone;
                }
            }

            return null;
        }

        public static void Log(string message)
        {
            instance?.Logger.LogInfo(message);
        }

        public static void LogWarning(string message)
        {
            instance?.Logger.LogWarning(message);
        }

        #endregion
    }

    #region Data Structures

    public enum DroneType
    {
        Scout,      // Fast, low capacity, short range
        Cargo,      // Balanced for transport
        HeavyLifter,// Slow, high capacity, long range
        Combat      // For TurretDefense integration - ammo resupply
    }

    public enum DroneRelayType
    {
        Standard,   // Basic charging station (Battery model)
        FastCharge, // Fast charging, higher power draw (Battery_big model)
        DockingBay, // Multi-drone docking (torpedo_docking_station model)
        Hub         // Control/routing hub (computer_station model)
    }

    public enum DroneState
    {
        Idle,
        Departing,
        Traveling,
        Loading,
        Unloading,
        Returning,
        Charging,
        Disabled
    }

    public class CargoData
    {
        public ResourceInfo ResourceType;
        public int Quantity;
        public int MaxQuantity;
        public bool IsCrate;

        public bool IsFull => Quantity >= MaxQuantity;
        public bool IsEmpty => Quantity <= 0;
    }

    public class DeliveryRequest
    {
        public Vector3 PickupLocation;
        public Vector3 DeliveryLocation;
        public ResourceInfo Resource;
        public int Quantity;
        public int Priority;
        public float TimeRequested;

        public DeliveryRequest(Vector3 pickup, Vector3 delivery, ResourceInfo resource, int qty, int priority = 0)
        {
            PickupLocation = pickup;
            DeliveryLocation = delivery;
            Resource = resource;
            Quantity = qty;
            Priority = priority;
            TimeRequested = Time.time;
        }
    }

    #endregion
}
