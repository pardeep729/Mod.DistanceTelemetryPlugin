using HarmonyLib;
using System;
using System.Reflection;

namespace DistanceTelemetryPlugin
{
    [HarmonyPatch]
    public static class Accessors
    {
        /// <summary>
        /// Get a private field from a type using reflection.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="fieldName"></param>
        /// <returns>The field value</returns>
        private static FieldInfo SafeGetField(Type type, string fieldName)
        {
            FieldInfo field = AccessTools.Field(type, fieldName);
            if (field == null)
            {
                Mod.Log?.LogError($"[Harmony Accessor] Failed to find private field '{fieldName}' on type '{type.FullName}'");
            }
            return field;
        }

        /// <summary>
        /// Get a private property from a type using reflection.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="type"></param>
        /// <param name="name"></param>
        /// <param name="instance"></param>
        /// <returns>The property value</returns>
        public static T SafeGetProperty<T>(Type type, string name, object instance)
        {
            var prop = AccessTools.Property(type, name);
            if (prop == null)
            {
                Mod.Log.LogError($"[Harmony Accessor] Failed to find property '{name}' on type '{type.Name}'");
                return default;
            }

            return (T)prop.GetValue(instance, null);
        }

        /// <summary>
        /// Get the CarLogic instance from a LocalPlayerControlledCar instance.
        /// This is a private field, so we use reflection to access it.
        /// </summary>
        /// <param name="car">LocalPlayerControlledCar instance</param>
        /// <returns>The field value</returns>
        public static CarLogic GetCarLogic(LocalPlayerControlledCar car)
        {
            FieldInfo field = SafeGetField(typeof(LocalPlayerControlledCar), "carLogic_");
            return field != null ? (CarLogic)field.GetValue(car) : null;
        }

        /// <summary>
        /// Get the CarStats instance from a LocalPlayerControlledCar instance.
        /// This is a private field, so we use reflection to access it.
        /// </summary>
        /// <param name="car">LocalPlayerControlledCar instance</param>
        /// <returns>The field value</returns>
        public static CarStats GetCarStats(LocalPlayerControlledCar car)
        {
            FieldInfo field = SafeGetField(typeof(LocalPlayerControlledCar), "carStats_");
            return field != null ? (CarStats)field.GetValue(car) : null;
        }

        /// <summary>
        /// Get the CarState instance from a LocalPlayerControlledCar instance.
        /// This is a private property, so we use reflection to access it.
        /// </summary>
        /// <param name="car">LocalPlayerControlledCar instance</param>
        /// <returns>The property value</returns>
        public static bool GetWingsEnabled(LocalPlayerControlledCar car)
        {
            return SafeGetProperty<bool>(typeof(LocalPlayerControlledCar), "WingsEnabled_", car);
        }
    }
}
