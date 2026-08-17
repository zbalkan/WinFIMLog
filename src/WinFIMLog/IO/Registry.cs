using System;
using System.Linq;
using Microsoft.Win32;
using WinFIMLog.Configuration;

namespace WinFIMLog.IO
{
    /// <summary>
    ///     Static class to access Registry.
    /// </summary>
    internal static class Registry
    {
        private const string LegacyPreferenceKeyName = @"SOFTWARE\WinFIMLog";
        private const string PolicyKeyName = @"SOFTWARE\Policies\WinFIMLog";
        private const string PreferenceKeyName = @"SOFTWARE\WinFIMLog";
        public static RegistryHive Hive => RegistryHive.LocalMachine;

        /// <summary>
        ///     Root object for Registry operations
        /// </summary>
        /// <exception cref="System.Security.SecurityException" accessor="get">
        /// </exception>
        /// <exception cref="UnauthorizedAccessException" accessor="get">
        /// </exception>
        /// <exception cref="System.IO.IOException" accessor="get">
        /// </exception>
        public static RegistryKey Root => Microsoft.Win32.Registry.LocalMachine.CreateSubKey(PreferenceKeyName, true);

        public static string RootName => Root.Name.Substring(Root.Name.IndexOf('\\') + 1);

        public static bool EffectiveValueExists(string value)
        {
            using var policy = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(PolicyKeyName, false);
            using var legacy = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(LegacyPreferenceKeyName, false);
            return policy?.GetValue(value, null, RegistryValueOptions.DoNotExpandEnvironmentNames) != null ||
                   Root.GetValue(value, null, RegistryValueOptions.DoNotExpandEnvironmentNames) != null ||
                   legacy?.GetValue(value, null, RegistryValueOptions.DoNotExpandEnvironmentNames) != null;
        }

        /// <summary> Translates the Registry value data in Dword to Int32 </summary> <param
        /// name="value">Name of the Registry value</param> <returns><see cref="int"></returns>
        /// <exception cref="ArgumentException"></exception> <exception
        /// cref="System.Security.SecurityException"></exception> <exception
        /// cref="System.IO.IOException"></exception> <exception cref="UnauthorizedAccessException"></exception>
        public static int ReadDwordValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Value cannot be null or empty.", nameof(value));
            }

            var valueData = ReadEffectiveValue(value);

            if (valueData == null)
            {
                return -1;
            }

            if (int.TryParse(valueData.ToString(), out var valueDataAsInt))
            {
                return valueDataAsInt;
            }

            return 0;
        }

        /// <summary> Translates the Registry value data from Multistring to <see cref="string[]">
        /// </summary> <param name="value">Name of the Registry value</param> <returns><see
        /// cref="string[]"></returns> <exception cref="ArgumentException"></exception> <exception
        /// cref="System.Security.SecurityException"></exception> <exception
        /// cref="System.IO.IOException"></exception> <exception cref="UnauthorizedAccessException"></exception>
        public static string[] ReadMultiStringValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Value cannot be null or empty.", nameof(value));
            }

            var valueData = ReadEffectiveValue(value);

            return valueData == null || valueData is not string[] multiStringValue
                ? []
                : multiStringValue.Where(path => !string.IsNullOrEmpty(path)).ToArray();
        }

        /// <summary> Translates the Registry value data from string to <see cref="string[]">
        /// </summary> <param name="value">Name of the Registry value</param> <returns><see
        /// cref="string"></returns> <exception cref="ArgumentException"></exception> <exception
        /// cref="System.Security.SecurityException"></exception> <exception
        /// cref="System.IO.IOException"></exception> <exception cref="UnauthorizedAccessException"></exception>
        public static string ReadStringValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Value cannot be null or empty.", nameof(value));
            }

            var valueData = ReadEffectiveValue(value);

            if (valueData == null || valueData is not string stringValue)
            {
                return string.Empty;
            }
            else
            {
                return stringValue;
            }
        }

        /// <summary> Save the <see cref="int"> value as Dword in Registry </summary> <param
        /// name="value">name of the Registry value</param> <param name="valueData">The <see
        /// cref="int"> value to be saved</param> <exception cref="ArgumentException"></exception>
        /// <exception cref="System.Security.SecurityException"></exception> <exception
        /// cref="System.IO.IOException"></exception> <exception cref="UnauthorizedAccessException"></exception>
        public static void WriteDwordValue(string value, int valueData)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Value cannot be null or empty.", nameof(value));
            }

            Root.SetValue(value, valueData, RegistryValueKind.DWord);
        }

        /// <summary> Save the <see cref="string[]"> value as MultiString in Registry </summary>
        /// <param name="value">name of the Registry value</param> <param name="valueData">The <see
        /// cref="string[]"> value to be saved</param> <exception
        /// cref="ArgumentException"></exception> <exception
        /// cref="System.Security.SecurityException"></exception> <exception
        /// cref="System.IO.IOException"></exception> <exception cref="UnauthorizedAccessException"></exception>
        public static void WriteMultiStringValue(string value, string[] valueData)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Value cannot be null or empty.", nameof(value));
            }

            ArgumentNullException.ThrowIfNull(valueData);

            Root.SetValue(value, valueData);
        }

        /// <summary> Save the <see cref="string"> value as string in Registry </summary> <param
        /// name="value">name of the Registry value</param> <param name="valueData">The <see
        /// cref="string"> value to be saved</param> <exception
        /// cref="ArgumentException"></exception> <exception
        /// cref="System.Security.SecurityException"></exception> <exception
        /// cref="System.IO.IOException"></exception> <exception cref="UnauthorizedAccessException"></exception>
        public static void WriteStringValue(string value, string valueData)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Value cannot be null or empty.", nameof(value));
            }

            ArgumentNullException.ThrowIfNull(valueData);

            Root.SetValue(value, valueData);
        }

        private static object? ReadEffectiveValue(string value)
        {
            using var policy = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(PolicyKeyName, false);
            using var legacy = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(LegacyPreferenceKeyName, false);
            return ConfigurationPrecedence.Resolve(
                policy?.GetValue(value, null, RegistryValueOptions.DoNotExpandEnvironmentNames),
                Root.GetValue(value, null, RegistryValueOptions.DoNotExpandEnvironmentNames),
                legacy?.GetValue(value, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        }
    }
}
