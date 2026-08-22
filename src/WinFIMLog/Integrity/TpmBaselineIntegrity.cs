using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Integrity
{
    /// <summary>
    /// Creates a non-exportable machine RSA key through the Microsoft Platform Crypto Provider and
    /// signs complete baseline manifests. The component uses binary signature material and stable
    /// enum identifiers; HMAC is intentionally not used because it requires a shared secret.
    /// </summary>
    public sealed class TpmBaselineIntegrity : ITpmBaselineIntegrity
    {
        public const BaselineAlgorithm Algorithm = BaselineAlgorithm.TpmRsaPssSha256;
        private const string KeyName = "WinFIMLog.BaselineIntegrity.v1";
        private const int RsaKeyLength = 2048;
        private readonly Settings settings;

        public TpmBaselineIntegrity(Settings settings) => this.settings = settings;

        public bool TryPrepare(out string reason)
        {
            try
            {
                using var rsa = OpenOrCreateRsa(out var publicKey);
                if (!MatchesOrStoresPublicKey(publicKey, out reason))
                {
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (IsTpmOrCngFailure(exception))
            {
                reason = $"TPM-backed integrity is unavailable: {exception.GetType().Name}.";
                return false;
            }
        }

        public bool TrySeal(BaselineMetadata baseline, IReadOnlyCollection<BaselineMember> members, out string reason)
        {
            ArgumentNullException.ThrowIfNull(baseline);
            ArgumentNullException.ThrowIfNull(members);

            try
            {
                using var rsa = OpenOrCreateRsa(out var publicKey);
                if (!MatchesOrStoresPublicKey(publicKey, out reason))
                {
                    return false;
                }

                baseline.ItemCount = members.Count;
                var manifestHash = ComputeManifestHash(baseline, members);
                baseline.IntegrityAlgorithm = Algorithm;
                baseline.IntegrityManifestHash = manifestHash;
                baseline.IntegrityPublicKey = publicKey;
                baseline.IntegritySignature = rsa.SignHash(manifestHash, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                reason = string.Empty;
                return true;
            }
            catch (Exception exception) when (IsTpmOrCngFailure(exception))
            {
                reason = $"TPM-backed integrity could not sign the completed baseline: {exception.GetType().Name}.";
                return false;
            }
        }

        public bool TryVerify(BaselineMetadata baseline, IReadOnlyCollection<BaselineMember> members, out string reason)
        {
            ArgumentNullException.ThrowIfNull(baseline);
            ArgumentNullException.ThrowIfNull(members);

            var claimsTpmIntegrity = baseline.IntegrityAlgorithm == Algorithm || baseline.Algorithm == Algorithm;
            if (baseline.IntegritySignature is not { Length: > 0 })
            {
                if (claimsTpmIntegrity)
                {
                    reason = "The TPM-backed baseline is missing its required integrity signature.";
                    return false;
                }

                reason = string.Empty;
                return true;
            }

            if (baseline.IntegrityAlgorithm != Algorithm || baseline.IntegrityPublicKey is not { Length: > 0 } ||
                baseline.IntegrityManifestHash is not { Length: > 0 })
            {
                reason = "The baseline contains incomplete or unsupported TPM integrity metadata.";
                return false;
            }

            try
            {
                var configured = settings.TpmIntegrityPublicKey;
                if (configured.Length == 0 || !CryptographicOperations.FixedTimeEquals(configured, baseline.IntegrityPublicKey))
                {
                    reason = "The configured TPM integrity public key does not match the baseline public key.";
                    return false;
                }

                var manifestHash = ComputeManifestHash(baseline, members);
                if (!CryptographicOperations.FixedTimeEquals(manifestHash, baseline.IntegrityManifestHash))
                {
                    reason = "The stored baseline manifest hash does not match its current contents.";
                    return false;
                }

                using var rsa = RSA.Create();
                rsa.ImportSubjectPublicKeyInfo(baseline.IntegrityPublicKey, out _);
                if (!rsa.VerifyHash(manifestHash, baseline.IntegritySignature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                {
                    reason = "The TPM integrity signature is invalid.";
                    return false;
                }

                reason = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException or CryptographicException)
            {
                reason = $"The TPM integrity metadata is malformed: {exception.GetType().Name}.";
                return false;
            }
        }

        public static bool TryRetire(Settings settings, out string reason)
        {
            ArgumentNullException.ThrowIfNull(settings);
            if (!settings.Success)
            {
                reason = "TPM-backed integrity key retirement requires a valid WinFIMLog settings generation.";
                return false;
            }

            // The public-key preference is also the durable ownership marker. If WinFIMLog never
            // provisioned a key, do not require a TPM/provider merely to uninstall the product.
            if (settings.TpmIntegrityPublicKey.Length == 0)
            {
                reason = string.Empty;
                return true;
            }

            try
            {
                if (CngKey.Exists(KeyName, CngProvider.MicrosoftPlatformCryptoProvider, CngKeyOpenOptions.MachineKey))
                {
                    using var key = CngKey.Open(KeyName, CngProvider.MicrosoftPlatformCryptoProvider, CngKeyOpenOptions.MachineKey);
                    key.Delete();
                }

                settings.StoreTpmIntegrityPublicKey([]);
                reason = string.Empty;
                return true;
            }
            catch (Exception exception) when (IsTpmOrCngFailure(exception))
            {
                reason = $"TPM-backed integrity key retirement failed: {exception.GetType().Name}.";
                return false;
            }
        }

        internal static byte[] ComputeManifestHash(BaselineMetadata baseline, IReadOnlyCollection<BaselineMember> members)
        {
            using var canonical = new MemoryStream();
            using var writer = new BinaryWriter(canonical, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write("WinFIMLog-TpmBaseline-v2");
            writer.Write(baseline.Id);
            writer.Write((int)baseline.Source);
            writer.Write(baseline.ScopeHash);
            writer.Write(baseline.SourceIdentity);
            writer.Write(baseline.SchemaVersion);
            writer.Write((int)baseline.Algorithm);
            writer.Write(baseline.ConsistencyMethod);
            writer.Write(baseline.ObservationPasses);
            writer.Write(baseline.ItemCount);
            foreach (var member in members.OrderBy(x => x.Identity, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.Identity, StringComparer.Ordinal))
            {
                writer.Write(member.Identity);
                writer.Write(member.Path);
                writer.Write((int)member.NodeType);
                WriteNullable(writer, member.ContentHash);
                writer.Write((int)member.HashState);
                writer.Write((int)member.AclState);
                writer.Write(member.AclEvidence);
                writer.Write(member.StreamNames.Length);
                foreach (var streamName in member.StreamNames)
                {
                    writer.Write(streamName);
                }
                WriteNullable(writer, member.LinkCount);
                writer.Write(member.IsSystem);
                writer.Write(member.IsSparse);
                writer.Write(member.IsTemporary);
                writer.Write(member.IsOffline);
                WriteNullable(writer, member.RegistryValueKind);
                WriteNullable(writer, member.RegistryValueData);
            }

            writer.Flush();
            return SHA256.HashData(canonical.GetBuffer().AsSpan(0, checked((int)canonical.Length)));
        }

        private bool MatchesOrStoresPublicKey(byte[] publicKey, out string reason)
        {
            var configured = settings.TpmIntegrityPublicKey;
            if (configured.Length == 0)
            {
                settings.StoreTpmIntegrityPublicKey(publicKey);
                reason = string.Empty;
                return true;
            }

            if (!CryptographicOperations.FixedTimeEquals(configured, publicKey))
            {
                reason = "The configured TPM integrity public key does not match the named TPM key.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static void WriteNullable(BinaryWriter writer, string? value)
        {
            writer.Write(value is not null);
            if (value is not null)
            {
                writer.Write(value);
            }
        }

        private static void WriteNullable(BinaryWriter writer, int? value)
        {
            writer.Write(value.HasValue);
            if (value.HasValue)
            {
                writer.Write(value.Value);
            }
        }

        private static void WriteNullable(BinaryWriter writer, byte[]? value)
        {
            writer.Write(value is not null);
            if (value is not null)
            {
                writer.Write(value.Length);
                writer.Write(value);
            }
        }

        private static RSACng OpenOrCreateRsa(out byte[] publicKey)
        {
            CngKey key;
            if (CngKey.Exists(KeyName, CngProvider.MicrosoftPlatformCryptoProvider, CngKeyOpenOptions.MachineKey))
            {
                key = CngKey.Open(KeyName, CngProvider.MicrosoftPlatformCryptoProvider, CngKeyOpenOptions.MachineKey);
            }
            else
            {
                var parameters = new CngKeyCreationParameters
                {
                    Provider = CngProvider.MicrosoftPlatformCryptoProvider,
                    KeyCreationOptions = CngKeyCreationOptions.MachineKey
                };
                parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(RsaKeyLength), CngPropertyOptions.None));
                key = CngKey.Create(CngAlgorithm.Rsa, KeyName, parameters);
            }

            var rsa = new RSACng(key);
            publicKey = rsa.ExportSubjectPublicKeyInfo();
            return rsa;
        }

        private static bool IsTpmOrCngFailure(Exception exception) => exception is CryptographicException or
            PlatformNotSupportedException or UnauthorizedAccessException or InvalidOperationException or IOException;
    }

    public interface ITpmBaselineIntegrity
    {
        bool TryPrepare(out string reason);
        bool TrySeal(BaselineMetadata baseline, IReadOnlyCollection<BaselineMember> members, out string reason);
        bool TryVerify(BaselineMetadata baseline, IReadOnlyCollection<BaselineMember> members, out string reason);
    }
}
