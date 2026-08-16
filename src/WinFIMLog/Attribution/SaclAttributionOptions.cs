using System;

namespace WinFIMLog.Attribution
{
    /// <summary>Opt-in Security-audit attribution. It never controls Tier 0 snapshots.</summary>
    public sealed class SaclAttributionOptions
    {
        public bool Enabled { get; set; }
        public string[] FileScopes { get; set; } = Array.Empty<string>();
        public string[] RegistryScopes { get; set; } = Array.Empty<string>();

        public void Validate()
        {
            var count = FileScopes.Length + RegistryScopes.Length;
            if (!Enabled) return;
            if (count == 0) throw new InvalidOperationException("SACL attribution requires an explicit scope.");
            if (count > 64) throw new InvalidOperationException("SACL attribution is limited to 64 explicit scopes.");
            foreach (var scope in FileScopes)
                if (scope.Contains('*')) throw new InvalidOperationException($"SACL file scope must not contain wildcards: {scope}");
            foreach (var scope in RegistryScopes)
                if (scope.Contains('*')) throw new InvalidOperationException($"SACL registry scope must not contain wildcards: {scope}");
        }
    }
}
