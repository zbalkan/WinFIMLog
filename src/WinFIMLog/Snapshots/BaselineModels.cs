using System;
using NUlid;

namespace WinFIMLog.Snapshots
{
    public enum BaselineApplicability
    { Current, Superseded }

    public enum BaselineSource
    { FileSystem, Registry }

    /// <summary>Stable persisted baseline algorithm identifiers.</summary>
    public enum BaselineAlgorithm : int
    {
        Sha256 = 1,
        RegistryV2 = 2,
        TpmRsaPssSha256 = 3,
        VssMftPerDrive = 4
    }

    /// <summary>DWORD-backed policy and preference values for optional TPM integrity hardening.</summary>
    public enum TpmIntegrityMode : int
    {
        Disabled = 0,
        PlatformRsaPssSha256 = 1
    }

    public enum BaselineStatus
    { Building, Reconciling, Complete, Invalid }

    public enum EvidenceAvailability
    { Available, AccessDenied, Vanished, Failed }

    public enum HashEvidenceState
    { Hashed, SkippedBySizeCap, Locked, AccessDenied, Vanished, Failed, NotApplicable }

    public enum ReconciliationChange
    { Created, Changed, Deleted }

    public enum SnapshotNodeType
    { File, Directory, ReparsePoint, RegistryKey, RegistryValue }

    public sealed class BaselineMember
    {
        public string AclEvidence { get; set; } = string.Empty;
        public EvidenceAvailability AclState { get; set; }
        public string BaselineId { get; set; } = string.Empty;
        public string? ContentHash { get; set; }

        public string Fingerprint => string.Join("|", NodeType, ContentHash, HashState,
            AclState, AclEvidence, string.Join("\u001f", StreamNames), LinkCount,
            IsSystem, IsSparse, IsTemporary, IsOffline,
            RegistryValueKind, RegistryValueData is null ? "" : Convert.ToBase64String(RegistryValueData));

        public HashEvidenceState HashState { get; set; }
        public string Id { get; set; } = Ulid.NewUlid().ToString();

        // Phase 4 D4 deliberately uses normalised path identity.
        public string Identity { get; set; } = string.Empty;

        public bool IsOffline { get; set; }
        public bool IsSparse { get; set; }
        public bool IsSystem { get; set; }
        public bool IsTemporary { get; set; }
        public int? LinkCount { get; set; }
        public SnapshotNodeType NodeType { get; set; }
        public string Path { get; set; } = string.Empty;
        public byte[]? RegistryValueData { get; set; }
        public string? RegistryValueKind { get; set; }
        public string[] StreamNames { get; set; } = [];
    }

    public sealed class BaselineMetadata
    {
        public BaselineAlgorithm Algorithm { get; set; } = BaselineAlgorithm.Sha256;
        public BaselineApplicability Applicability { get; set; } = BaselineApplicability.Current;
        public DateTimeOffset? CompletedAt { get; set; }
        public string ConsistencyMethod { get; set; } = string.Empty;
        public string? EndCursor { get; set; }
        public string Id { get; set; } = Ulid.NewUlid().ToString();
        public string? InvalidReason { get; set; }
        public BaselineAlgorithm? IntegrityAlgorithm { get; set; }
        public byte[]? IntegrityManifestHash { get; set; }
        public byte[]? IntegrityPublicKey { get; set; }
        public byte[]? IntegritySignature { get; set; }
        public long ItemCount { get; set; }
        public int ObservationPasses { get; set; }
        public int SchemaVersion { get; set; } = 1;
        public string ScopeHash { get; set; } = string.Empty;
        public BaselineSource Source { get; set; }
        public string SourceIdentity { get; set; } = string.Empty;
        public string? StartCursor { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public BaselineStatus Status { get; set; }
    }

    public sealed class ReconciliationResult
    {
        public string BaselineId { get; set; } = string.Empty;
        public ReconciliationChange Change { get; set; }
        public DateTimeOffset? DeliveredAt { get; set; }
        public int DeliveryAttempts { get; set; }
        public DateTimeOffset DetectedAt { get; set; }
        public string Id { get; set; } = Ulid.NewUlid().ToString();
        public string Identity { get; set; } = string.Empty;
        public string? NewPath { get; set; }
        public string? OldPath { get; set; }
        public string? PreviousBaselineId { get; set; }
    }
}
