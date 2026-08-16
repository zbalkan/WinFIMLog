using System;
using NUlid;

namespace WinFIMLog.Snapshots
{
    public enum BaselineStatus { Building, Reconciling, Complete, Invalid }
    public enum BaselineSource { FileSystem, Registry }
    public enum EvidenceAvailability { Available, AccessDenied, Vanished, Failed }
    public enum HashEvidenceState { Hashed, SkippedBySizeCap, Locked, AccessDenied, Vanished, Failed, NotApplicable }
    public enum SnapshotNodeType { File, Directory, ReparsePoint, RegistryKey, RegistryValue }
    public enum ReconciliationChange { Created, Changed, Deleted }

    public sealed class BaselineMetadata
    {
        public string Id { get; set; } = Ulid.NewUlid().ToString();
        public BaselineSource Source { get; set; }
        public string ScopeHash { get; set; } = string.Empty;
        public string SourceIdentity { get; set; } = string.Empty;
        public int SchemaVersion { get; set; } = 1;
        public string AlgorithmVersion { get; set; } = "sha256-v1";
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public long ItemCount { get; set; }
        public BaselineStatus Status { get; set; }
        public string? StartCursor { get; set; }
        public string? EndCursor { get; set; }
        public string? InvalidReason { get; set; }
    }

    public sealed class BaselineMember
    {
        public string Id { get; set; } = Ulid.NewUlid().ToString();
        public string BaselineId { get; set; } = string.Empty;
        // Phase 4 D4 deliberately uses normalised path identity.
        public string Identity { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public SnapshotNodeType NodeType { get; set; }
        public string? ContentHash { get; set; }
        public HashEvidenceState HashState { get; set; }
        public string AclEvidence { get; set; } = string.Empty;
        public EvidenceAvailability AclState { get; set; }
        public string[] StreamNames { get; set; } = Array.Empty<string>();
        public int? LinkCount { get; set; }
        public string? RegistryValueKind { get; set; }
        public byte[]? RegistryValueData { get; set; }

        public string Fingerprint => string.Join("|", NodeType, ContentHash, HashState,
            AclState, AclEvidence, string.Join("\u001f", StreamNames), LinkCount,
            RegistryValueKind, RegistryValueData is null ? "" : Convert.ToBase64String(RegistryValueData));
    }

    public sealed class ReconciliationResult
    {
        public string Id { get; set; } = Ulid.NewUlid().ToString();
        public string BaselineId { get; set; } = string.Empty;
        public string? PreviousBaselineId { get; set; }
        public string Identity { get; set; } = string.Empty;
        public ReconciliationChange Change { get; set; }
        public string? OldPath { get; set; }
        public string? NewPath { get; set; }
        public DateTimeOffset DetectedAt { get; set; }
    }
}
