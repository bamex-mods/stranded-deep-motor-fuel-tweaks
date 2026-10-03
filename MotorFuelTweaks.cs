using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using Bamex.StrandedDeep.ModSettings;
using UnityEngine;

namespace StrandedDeepMotorFuelTweaks
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("com.bamex.strandeddeep.modsettings", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class MotorFuelTweaksPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.bamex.strandeddeep.motorfueltweaks";
        public const string PluginName = "Stranded Deep Motor Fuel Tweaks";
        public const string PluginVersion = "0.2.1";

        private const float DefaultConsumptionMultiplier = 0.50f;
        private const float MinimumConsumptionMultiplier = 0.00f;
        private const string ModSettingsModId = "motorfueltweaks";
        private const float MotorScanIntervalSeconds = 2.0f;
        private const float FuelEpsilon = 0.000001f;

        private sealed class MotorState
        {
            public Beam.MotorVehicleMovement Motor;
            public int InstanceId;
            public float LastFuel;
            public bool WasRunning;
        }

        private readonly List<MotorState> _motors = new List<MotorState>();
        private readonly HashSet<int> _seenMotorIds = new HashSet<int>();

        private ConfigEntry<float> _consumptionMultiplier;
        private FieldInfo _fuelField;
        private MethodInfo _engineIsRunningMethod;
        private PropertyInfo _fuelCapacityProperty;
        private float _nextMotorScanAt;
        private float _effectiveMultiplier;
        private bool _reflectionReady;

        private void Awake()
        {
            _consumptionMultiplier = Config.Bind(
                "BoatMotor",
                "FuelConsumptionMultiplier",
                DefaultConsumptionMultiplier,
                "Boat Motor fuel consumption multiplier. 1.0 = vanilla, 0.5 = half consumption. Values are clamped to 0.0..1.0. 0.0 means no fuel consumption."
            );

            RefreshMultiplier();
            PrepareReflection();
            RegisterModSettings();

            Logger.LogInfo(PluginName + " v" + PluginVersion + " loaded.");
            Logger.LogInfo("Boat Motor fuel consumption multiplier: " + _effectiveMultiplier.ToString("0.###"));

            _nextMotorScanAt = 0.0f;
        }

        private void OnEnable()
        {
            if (_consumptionMultiplier != null)
            {
                _consumptionMultiplier.SettingChanged += OnMultiplierChanged;
            }
        }

        private void OnDisable()
        {
            if (_consumptionMultiplier != null)
            {
                _consumptionMultiplier.SettingChanged -= OnMultiplierChanged;
            }
        }

        private void OnMultiplierChanged(object sender, EventArgs e)
        {
            RefreshMultiplier();
            Logger.LogInfo("Boat Motor fuel consumption multiplier changed to " + _effectiveMultiplier.ToString("0.###"));
        }

        private void RefreshMultiplier()
        {
            float value = DefaultConsumptionMultiplier;
            if (_consumptionMultiplier != null)
            {
                value = _consumptionMultiplier.Value;
            }

            if (value < MinimumConsumptionMultiplier)
            {
                value = MinimumConsumptionMultiplier;
            }
            else if (value > 1.0f)
            {
                value = 1.0f;
            }

            _effectiveMultiplier = value;
        }


        private void RegisterModSettings()
        {
            bool registered = ModSettingsClient.RegisterMod(
                ModSettingsModId,
                "\u041C\u041E\u0422\u041E\u0420",
                600);

            if (!registered)
            {
                Logger.LogInfo("Stranded Deep Mod Settings not available; using BepInEx config only.");
                return;
            }

            bool sliderRegistered = ModSettingsClient.AddSlider(
                ModSettingsModId,
                "fuel-consumption",
                "\u0420\u0430\u0441\u0445\u043E\u0434 \u0442\u043E\u043F\u043B\u0438\u0432\u0430",
                100,
                MinimumConsumptionMultiplier,
                1.0f,
                0.05f,
                100.0f,
                "%",
                0,
                GetMenuConsumptionMultiplier,
                SetMenuConsumptionMultiplier);

            if (sliderRegistered)
            {
                Logger.LogInfo("Registered Mod Settings slider: Boat Motor fuel consumption.");
            }
            else
            {
                Logger.LogWarning("Mod Settings host was found, but the fuel consumption slider could not be registered.");
            }
        }

        private float GetMenuConsumptionMultiplier()
        {
            if (_consumptionMultiplier == null)
            {
                return DefaultConsumptionMultiplier;
            }

            return Mathf.Clamp(_consumptionMultiplier.Value, MinimumConsumptionMultiplier, 1.0f);
        }

        private void SetMenuConsumptionMultiplier(float value)
        {
            if (_consumptionMultiplier == null)
            {
                return;
            }

            _consumptionMultiplier.Value = Mathf.Clamp(value, MinimumConsumptionMultiplier, 1.0f);
        }

        private void PrepareReflection()
        {
            try
            {
                Type motorType = typeof(Beam.MotorVehicleMovement);
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                _fuelField = motorType.GetField("_fuel", flags);
                _engineIsRunningMethod = motorType.GetMethod("EngineIsRunning", flags, null, Type.EmptyTypes, null);
                _fuelCapacityProperty = motorType.GetProperty("FuelCapacity", flags);

                if (_fuelField == null)
                {
                    Logger.LogError("Beam.MotorVehicleMovement._fuel was not found. Mod disabled.");
                    _reflectionReady = false;
                    return;
                }

                if (_engineIsRunningMethod == null)
                {
                    Logger.LogError("Beam.MotorVehicleMovement.EngineIsRunning() was not found. Mod disabled.");
                    _reflectionReady = false;
                    return;
                }

                _reflectionReady = true;
                Logger.LogInfo("Motor reflection ready: _fuel + EngineIsRunning().");
            }
            catch (Exception ex)
            {
                _reflectionReady = false;
                Logger.LogError("Failed to prepare Boat Motor reflection: " + ex);
            }
        }

        private void LateUpdate()
        {
            if (!_reflectionReady)
            {
                return;
            }

            if (Time.unscaledTime >= _nextMotorScanAt)
            {
                ScanForMotors();
                _nextMotorScanAt = Time.unscaledTime + MotorScanIntervalSeconds;
            }

            ApplyFuelMultiplier();
        }

        private void ScanForMotors()
        {
            try
            {
                Beam.MotorVehicleMovement[] found = Resources.FindObjectsOfTypeAll<Beam.MotorVehicleMovement>();
                _seenMotorIds.Clear();

                int i;
                for (i = 0; i < found.Length; i++)
                {
                    Beam.MotorVehicleMovement motor = found[i];
                    if (!IsLiveMotor(motor))
                    {
                        continue;
                    }

                    int id = motor.GetInstanceID();
                    _seenMotorIds.Add(id);

                    if (FindMotorState(id) != null)
                    {
                        continue;
                    }

                    float fuel;
                    if (!TryGetFuel(motor, out fuel))
                    {
                        continue;
                    }

                    MotorState state = new MotorState();
                    state.Motor = motor;
                    state.InstanceId = id;
                    state.LastFuel = fuel;
                    state.WasRunning = SafeEngineIsRunning(motor);
                    _motors.Add(state);

                    Logger.LogInfo("Tracking Boat Motor id=" + id.ToString() + " fuel=" + fuel.ToString("0.######"));
                }

                for (i = _motors.Count - 1; i >= 0; i--)
                {
                    MotorState state = _motors[i];
                    if (state.Motor == null || !_seenMotorIds.Contains(state.InstanceId) || !IsLiveMotor(state.Motor))
                    {
                        _motors.RemoveAt(i);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Boat Motor scan failed: " + ex.Message);
            }
        }

        private MotorState FindMotorState(int instanceId)
        {
            int i;
            for (i = 0; i < _motors.Count; i++)
            {
                if (_motors[i].InstanceId == instanceId)
                {
                    return _motors[i];
                }
            }

            return null;
        }

        private bool IsLiveMotor(Beam.MotorVehicleMovement motor)
        {
            if (motor == null || motor.gameObject == null)
            {
                return false;
            }

            if (!motor.gameObject.activeInHierarchy)
            {
                return false;
            }

            try
            {
                if (!motor.gameObject.scene.IsValid() || !motor.gameObject.scene.isLoaded)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            string objectName = motor.gameObject.name;
            if (string.IsNullOrEmpty(objectName))
            {
                return false;
            }

            return objectName.IndexOf("VEHICLE_MOTOR", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyFuelMultiplier()
        {
            int i;
            for (i = _motors.Count - 1; i >= 0; i--)
            {
                MotorState state = _motors[i];
                Beam.MotorVehicleMovement motor = state.Motor;

                if (!IsLiveMotor(motor))
                {
                    _motors.RemoveAt(i);
                    continue;
                }

                float currentFuel;
                if (!TryGetFuel(motor, out currentFuel))
                {
                    continue;
                }

                bool running = SafeEngineIsRunning(motor);

                if (_effectiveMultiplier < 1.0f && currentFuel + FuelEpsilon < state.LastFuel && (running || state.WasRunning))
                {
                    float vanillaDrain = state.LastFuel - currentFuel;
                    float refund = vanillaDrain * (1.0f - _effectiveMultiplier);
                    float adjustedFuel = currentFuel + refund;
                    float capacity = GetFuelCapacitySafe(motor);

                    if (capacity > 0.0f && adjustedFuel > capacity)
                    {
                        adjustedFuel = capacity;
                    }

                    if (adjustedFuel < 0.0f)
                    {
                        adjustedFuel = 0.0f;
                    }

                    if (TrySetFuel(motor, adjustedFuel))
                    {
                        currentFuel = adjustedFuel;
                    }
                }

                state.LastFuel = currentFuel;
                state.WasRunning = running;
            }
        }

        private bool TryGetFuel(Beam.MotorVehicleMovement motor, out float fuel)
        {
            fuel = 0.0f;

            try
            {
                object value = _fuelField.GetValue(motor);
                if (value is float)
                {
                    fuel = (float)value;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Failed to read Boat Motor fuel: " + ex.Message);
            }

            return false;
        }

        private bool TrySetFuel(Beam.MotorVehicleMovement motor, float fuel)
        {
            try
            {
                _fuelField.SetValue(motor, fuel);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Failed to write Boat Motor fuel: " + ex.Message);
                return false;
            }
        }

        private bool SafeEngineIsRunning(Beam.MotorVehicleMovement motor)
        {
            try
            {
                object result = _engineIsRunningMethod.Invoke(motor, null);
                if (result is bool)
                {
                    return (bool)result;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("EngineIsRunning() failed: " + ex.Message);
            }

            return false;
        }

        private float GetFuelCapacitySafe(Beam.MotorVehicleMovement motor)
        {
            if (_fuelCapacityProperty == null)
            {
                return 4.0f;
            }

            try
            {
                object value = _fuelCapacityProperty.GetValue(motor, null);
                if (value is float)
                {
                    return (float)value;
                }
            }
            catch
            {
            }

            return 4.0f;
        }
    }
}
