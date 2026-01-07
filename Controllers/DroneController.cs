using UnityEngine;
using System.Collections.Generic;

namespace DroneLogistics
{
    /// <summary>
    /// Controls individual drone behavior - navigation, cargo handling, charging
    /// </summary>
    public class DroneController : MonoBehaviour
    {
        // Identity
        public DroneType DroneType { get; private set; }
        public DronePadController HomePad { get; set; }
        public int DroneId { get; private set; }
        private static int nextDroneId = 0;

        // State
        public DroneState State { get; private set; } = DroneState.Idle;
        public bool IsAvailable => State == DroneState.Idle && charge >= 0.2f;

        // Stats (modified by type)
        private float speed;
        private int capacity;
        private float range;
        private float chargeRate;

        // Current values
        private float charge = 1f;
        public float Charge => charge;
        public float ChargePercent => charge * 100f;

        // Charging state
        private Vector3 assignedDockingPosition;
        private bool hasDockingAssignment = false;

        // Cargo
        private List<CargoData> cargo = new List<CargoData>();
        public IReadOnlyList<CargoData> Cargo => cargo;
        public bool HasCargo => cargo.Count > 0;
        public int CargoCount => cargo.Count;

        // Navigation
        private Vector3 targetPosition;
        private DeliveryRequest currentDelivery;
        private float hoverHeight = 5f;
        private float arrivalThreshold = 2f;

        // Movement
        public Vector3 Velocity { get; private set; }
        private Vector3 smoothVelocity;

        // Visual
        private Light droneLight;
        private ParticleSystem thrusterParticles;
        private float bobOffset;
        private float bobSpeed = 2f;
        private float bobAmount = 0.3f;

        public void Initialize(DroneType type, DronePadController pad)
        {
            DroneType = type;
            HomePad = pad;
            DroneId = nextDroneId++;

            // Set stats based on type
            switch (type)
            {
                case DroneType.Scout:
                    speed = DroneLogisticsPlugin.DroneSpeed.Value * 1.5f;
                    capacity = 1;
                    range = DroneLogisticsPlugin.DroneRange.Value * 0.5f;
                    chargeRate = 1f / (DroneLogisticsPlugin.ChargingTime.Value * 0.5f);
                    break;

                case DroneType.Cargo:
                    speed = DroneLogisticsPlugin.DroneSpeed.Value;
                    capacity = DroneLogisticsPlugin.DroneCapacity.Value;
                    range = DroneLogisticsPlugin.DroneRange.Value;
                    chargeRate = 1f / DroneLogisticsPlugin.ChargingTime.Value;
                    break;

                case DroneType.HeavyLifter:
                    speed = DroneLogisticsPlugin.DroneSpeed.Value * 0.6f;
                    capacity = DroneLogisticsPlugin.DroneCapacity.Value * 4;
                    range = DroneLogisticsPlugin.DroneRange.Value * 1.5f;
                    chargeRate = 1f / (DroneLogisticsPlugin.ChargingTime.Value * 2f);
                    break;

                case DroneType.Combat:
                    speed = DroneLogisticsPlugin.DroneSpeed.Value * 1.2f;
                    capacity = 2; // Ammo crates
                    range = DroneLogisticsPlugin.DroneRange.Value;
                    chargeRate = 1f / DroneLogisticsPlugin.ChargingTime.Value;
                    break;
            }

            SetupVisuals();
        }

        private void SetupVisuals()
        {
            // Add a light to the drone
            var lightObj = new GameObject("DroneLight");
            lightObj.transform.SetParent(transform);
            lightObj.transform.localPosition = Vector3.down * 0.5f;

            droneLight = lightObj.AddComponent<Light>();
            droneLight.type = LightType.Point;
            droneLight.range = 8f;
            droneLight.intensity = 1f;
            droneLight.color = GetDroneColor();

            // Add simple thruster particles
            var particleObj = new GameObject("Thrusters");
            particleObj.transform.SetParent(transform);
            particleObj.transform.localPosition = Vector3.down * 0.3f;

            thrusterParticles = particleObj.AddComponent<ParticleSystem>();
            var main = thrusterParticles.main;
            main.startLifetime = 0.3f;
            main.startSpeed = 3f;
            main.startSize = 0.2f;
            main.startColor = new Color(0.5f, 0.8f, 1f, 0.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = thrusterParticles.emission;
            emission.rateOverTime = 20f;

            var shape = thrusterParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 15f;
            shape.radius = 0.1f;

            var renderer = particleObj.GetComponent<ParticleSystemRenderer>();
            renderer.material = DroneLogisticsPlugin.GetEffectMaterial(new Color(0.5f, 0.8f, 1f, 0.5f));
        }

        private Color GetDroneColor()
        {
            switch (DroneType)
            {
                case DroneType.Scout: return new Color(0.3f, 0.8f, 1f);
                case DroneType.Cargo: return new Color(0.3f, 1f, 0.5f);
                case DroneType.HeavyLifter: return new Color(1f, 0.8f, 0.3f);
                case DroneType.Combat: return new Color(1f, 0.3f, 0.3f);
                default: return Color.white;
            }
        }

        void Update()
        {
            switch (State)
            {
                case DroneState.Idle:
                    UpdateIdle();
                    break;
                case DroneState.Departing:
                    UpdateDeparting();
                    break;
                case DroneState.Traveling:
                    UpdateTraveling();
                    break;
                case DroneState.Loading:
                    UpdateLoading();
                    break;
                case DroneState.Unloading:
                    UpdateUnloading();
                    break;
                case DroneState.Returning:
                    UpdateReturning();
                    break;
                case DroneState.Charging:
                    UpdateCharging();
                    break;
            }

            UpdateVisuals();
            DrainCharge();
        }

        #region State Updates

        private void UpdateIdle()
        {
            // Hover in place at home pad
            if (HomePad != null)
            {
                Vector3 hoverPos = HomePad.transform.position + Vector3.up * hoverHeight;
                hoverPos.y += Mathf.Sin(Time.time * bobSpeed + bobOffset) * bobAmount;

                transform.position = Vector3.Lerp(transform.position, hoverPos, Time.deltaTime * 2f);
            }

            // Check if we need to charge
            if (charge < 0.3f)
            {
                RequestCharging();
            }
        }

        private void RequestCharging()
        {
            if (HomePad != null && HomePad.RequestCharging(this))
            {
                State = DroneState.Charging;
            }
            else
            {
                // No home pad or can't charge - try to find another relay
                var nearestPad = DroneLogisticsPlugin.FindNearestPad(transform.position);
                if (nearestPad != null && nearestPad.RequestCharging(this))
                {
                    State = DroneState.Charging;
                }
            }
        }

        private void UpdateDeparting()
        {
            // Rise up before traveling
            Vector3 riseTarget = transform.position;
            riseTarget.y = hoverHeight + 2f;

            if (MoveToward(riseTarget, speed * 0.5f))
            {
                State = DroneState.Traveling;
            }
        }

        private void UpdateTraveling()
        {
            if (currentDelivery == null)
            {
                State = DroneState.Returning;
                return;
            }

            Vector3 target = HasCargo ? currentDelivery.DeliveryLocation : currentDelivery.PickupLocation;
            target.y = hoverHeight;

            if (MoveToward(target, speed))
            {
                State = HasCargo ? DroneState.Unloading : DroneState.Loading;
            }
        }

        private void UpdateLoading()
        {
            // Descend to pickup point
            Vector3 loadPos = currentDelivery.PickupLocation;
            loadPos.y += 1f;

            if (MoveToward(loadPos, speed * 0.3f))
            {
                // Simulate loading time
                // In real implementation, interact with inventory system
                var cargoData = DroneLogisticsPlugin.PackItems(currentDelivery.Resource, currentDelivery.Quantity);
                cargo.Add(cargoData);

                DroneLogisticsPlugin.Log($"Drone {DroneId} loaded {cargoData.Quantity}x {currentDelivery.Resource.displayName}");

                State = DroneState.Departing;
            }
        }

        private void UpdateUnloading()
        {
            // Descend to delivery point
            Vector3 unloadPos = currentDelivery.DeliveryLocation;
            unloadPos.y += 1f;

            if (MoveToward(unloadPos, speed * 0.3f))
            {
                // Simulate unloading
                if (cargo.Count > 0)
                {
                    var delivered = cargo[0];
                    cargo.RemoveAt(0);

                    DroneLogisticsPlugin.Log($"Drone {DroneId} delivered {delivered.Quantity}x {currentDelivery.Resource.displayName}");
                }

                currentDelivery = null;
                State = DroneState.Returning;
            }
        }

        private void UpdateReturning()
        {
            if (HomePad == null)
            {
                // Find nearest pad
                HomePad = DroneLogisticsPlugin.FindNearestPad(transform.position);
                if (HomePad == null)
                {
                    State = DroneState.Disabled;
                    return;
                }
            }

            Vector3 homePos = HomePad.transform.position + Vector3.up * hoverHeight;

            if (MoveToward(homePos, speed))
            {
                // Request charging if low on battery
                if (charge < 0.5f)
                {
                    RequestCharging();
                }
                else
                {
                    State = DroneState.Idle;
                }
            }
        }

        private void UpdateCharging()
        {
            // Move to assigned docking position (or hover at pad if waiting)
            Vector3 targetPos;

            if (hasDockingAssignment)
            {
                targetPos = assignedDockingPosition;
            }
            else if (HomePad != null)
            {
                // Waiting for slot - hover near pad
                targetPos = HomePad.transform.position + Vector3.up * (hoverHeight * 0.5f);
                targetPos.x += Mathf.Sin(Time.time + DroneId) * 1.5f;
                targetPos.z += Mathf.Cos(Time.time + DroneId) * 1.5f;
            }
            else
            {
                // No pad - just hover in place
                targetPos = transform.position;
            }

            MoveToward(targetPos, speed * 0.3f);

            // Note: Actual charging is handled by the DronePadController
            // We just wait here until OnChargingComplete is called
        }

        #endregion

        #region Movement

        private bool MoveToward(Vector3 target, float moveSpeed)
        {
            Vector3 direction = target - transform.position;
            float distance = direction.magnitude;

            if (distance < arrivalThreshold)
            {
                Velocity = Vector3.zero;
                return true;
            }

            Vector3 desiredVelocity = direction.normalized * moveSpeed;
            Velocity = Vector3.SmoothDamp(Velocity, desiredVelocity, ref smoothVelocity, 0.3f);

            transform.position += Velocity * Time.deltaTime;

            // Face movement direction
            if (Velocity.sqrMagnitude > 0.1f)
            {
                Vector3 flatVel = Velocity;
                flatVel.y = 0;
                if (flatVel.sqrMagnitude > 0.1f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(flatVel);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 5f);
                }
            }

            return false;
        }

        private void DrainCharge()
        {
            if (State != DroneState.Charging && State != DroneState.Idle)
            {
                // Moving drains charge
                float drainRate = 0.01f; // 1% per second base
                if (HasCargo) drainRate *= 1.5f; // Carrying cargo drains faster

                charge -= drainRate * Time.deltaTime;

                if (charge <= 0)
                {
                    charge = 0;
                    State = DroneState.Disabled;
                    DroneLogisticsPlugin.LogWarning($"Drone {DroneId} ran out of charge!");
                }
            }
        }

        #endregion

        #region Commands

        public bool AssignDelivery(DeliveryRequest delivery)
        {
            if (!IsAvailable)
                return false;

            // Check range
            float pickupDist = Vector3.Distance(transform.position, delivery.PickupLocation);
            float deliveryDist = Vector3.Distance(delivery.PickupLocation, delivery.DeliveryLocation);
            float returnDist = Vector3.Distance(delivery.DeliveryLocation, HomePad?.transform.position ?? transform.position);

            float totalDist = pickupDist + deliveryDist + returnDist;
            if (totalDist > range)
            {
                DroneLogisticsPlugin.LogWarning($"Delivery too far for drone {DroneId} ({totalDist:F0}m > {range:F0}m)");
                return false;
            }

            currentDelivery = delivery;
            State = DroneState.Departing;

            DroneLogisticsPlugin.Log($"Drone {DroneId} assigned delivery: {delivery.Resource.displayName}");
            return true;
        }

        public void RecallToBase()
        {
            if (State != DroneState.Disabled)
            {
                currentDelivery = null;
                cargo.Clear();
                State = DroneState.Returning;
            }
        }

        public void EmergencyStop()
        {
            Velocity = Vector3.zero;
            State = DroneState.Idle;
        }

        #endregion

        #region Charging Callbacks

        /// <summary>
        /// Called by DronePadController when a docking slot is assigned
        /// </summary>
        public void OnDockingAssigned(Vector3 dockingPosition)
        {
            assignedDockingPosition = dockingPosition;
            hasDockingAssignment = true;
            DroneLogisticsPlugin.Log($"Drone {DroneId} assigned docking position");
        }

        /// <summary>
        /// Called by DronePadController to add charge
        /// </summary>
        public void AddCharge(float amount)
        {
            charge = Mathf.Clamp01(charge + amount);
        }

        /// <summary>
        /// Called by DronePadController when charging is complete
        /// </summary>
        public void OnChargingComplete()
        {
            charge = 1f;
            hasDockingAssignment = false;
            State = DroneState.Idle;
            DroneLogisticsPlugin.Log($"Drone {DroneId} charging complete, returning to idle");
        }

        /// <summary>
        /// Cancel current charging and return to idle/returning
        /// </summary>
        public void CancelCharging()
        {
            if (State == DroneState.Charging)
            {
                hasDockingAssignment = false;
                HomePad?.CancelCharging(this);
                State = DroneState.Idle;
            }
        }

        #endregion

        #region Visuals

        private void UpdateVisuals()
        {
            // Adjust light based on state
            if (droneLight != null)
            {
                Color baseColor = GetDroneColor();

                switch (State)
                {
                    case DroneState.Charging:
                        droneLight.color = Color.Lerp(Color.red, baseColor, charge);
                        droneLight.intensity = 0.5f + charge * 0.5f;
                        break;
                    case DroneState.Disabled:
                        droneLight.color = Color.red;
                        droneLight.intensity = 0.3f;
                        break;
                    case DroneState.Loading:
                    case DroneState.Unloading:
                        droneLight.color = Color.yellow;
                        droneLight.intensity = 1.5f;
                        break;
                    default:
                        droneLight.color = baseColor;
                        droneLight.intensity = 1f;
                        break;
                }
            }

            // Adjust thruster particles
            if (thrusterParticles != null)
            {
                var emission = thrusterParticles.emission;

                if (State == DroneState.Charging || State == DroneState.Disabled)
                {
                    emission.rateOverTime = 5f;
                }
                else if (Velocity.magnitude > 1f)
                {
                    emission.rateOverTime = 30f + Velocity.magnitude * 2f;
                }
                else
                {
                    emission.rateOverTime = 15f;
                }
            }
        }

        #endregion

        void OnDestroy()
        {
            DroneLogisticsPlugin.ActiveDrones.Remove(this);
        }
    }
}
