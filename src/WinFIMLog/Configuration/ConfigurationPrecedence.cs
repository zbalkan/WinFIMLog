namespace WinFIMLog.Configuration
{
    internal static class ConfigurationPrecedence
    {
        public static object? Resolve(object? policy, object? preference, object? legacyPreference) =>
            policy ?? preference ?? legacyPreference;
    }
}
