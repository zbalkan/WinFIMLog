# Optional TPM-Backed Baseline Integrity

## Purpose

WinFIMLog continues to calculate **SHA-256** content hashes for every eligible file. This is the default and remains active when TPM hardening is disabled or unavailable. The optional hardening feature additionally signs the canonical manifest of each completed filesystem or registry baseline with **RSA-PSS/SHA-256** using a non-exportable machine key created through the Microsoft Platform Crypto Provider. Windows documents that this provider uses the TPM to protect cryptographic keys and operations.[1]

> This feature intentionally uses a digital signature rather than HMAC. HMAC verification requires a shared secret; a stored public key cannot verify an HMAC. The signature design permits verification with the public key while the private key remains hardware-protected.

| Component | Location or behavior |
|---|---|
| Default file evidence | SHA-256 content hash stored in `BaselineMember.ContentHash` |
| Baseline algorithms | `BaselineAlgorithm.Sha256 = 1`, `RegistryV2 = 2`, `TpmRsaPssSha256 = 3` |
| TPM integrity mode | `TpmIntegrityMode.Disabled = 0`, `PlatformRsaPssSha256 = 1` |
| Optional baseline evidence | Binary RSA-PSS/SHA-256 signature over a versioned, binary canonical baseline manifest |
| TPM key provider | Microsoft Platform Crypto Provider |
| TPM key name | `WinFIMLog.BaselineIntegrity.v1` machine key |
| Local public key record | Binary `Settings.TpmIntegrityPublicKey`, resolved from policy, preference, then legacy registry values |
| Policy switch | `HKLM\SOFTWARE\Policies\WinFIMLog\TpmIntegrityMode` (`REG_DWORD`) |
| Fallback signal | Event ID **7798**, record type `TpmIntegrityUnavailable`, WinFIMLog Operational channel |

## Group Policy deployment

Copy `policy/WinFIMLog/WinFIMLog.admx` to the Central Store `PolicyDefinitions` directory and copy `policy/WinFIMLog/adml/en-US/WinFIMLog.adml` to its `en-US` subdirectory. In Group Policy Management, configure the following computer policy:

> **Computer Configuration → Policies → Administrative Templates → WinFIMLog → Enable TPM-backed baseline integrity hardening**

The enabled policy writes `TpmIntegrityMode=1` (`PlatformRsaPssSha256`) to the policy registry path. Disabled writes `0` (`Disabled`). Policy values take precedence over local WinFIMLog preferences.

| Policy result | Runtime behavior |
|---|---|
| `PlatformRsaPssSha256` and TPM key available | A completed baseline receives `TpmRsaPssSha256` signed-manifest metadata. Subsequent reconciliation verifies the prior signed baseline before trusting it. |
| `PlatformRsaPssSha256` but TPM/CNG unavailable | WinFIMLog writes Event ID 7798 at error severity and completes the baseline using its source-native fallback: `Sha256` (1) for filesystem and `RegistryV2` (2) for registry. The event includes the integer in `fallbackAlgorithm`. |
| TPM sealing fails after baseline creation | WinFIMLog restores the latest complete baseline for the source-native fallback algorithm before reconciliation, preserving prior-baseline change detection. |
| `Disabled` or unconfigured | WinFIMLog uses `Sha256` baselines without TPM key operations. |

## TPM or vTPM deployment qualification

Run this qualification on a dedicated Windows device or virtual machine with a compatible TPM or vTPM before enabling the policy across production endpoints. First, enable the policy and request both filesystem and registry snapshots. Confirm that each completed baseline records `BaselineAlgorithm.TpmRsaPssSha256` integrity metadata, binary public-key data is visible through `Settings.TpmIntegrityPublicKey`, and Event ID 7798 is absent. Next, request a subsequent snapshot and confirm that prior baseline verification succeeds. Finally, run the uninstall workflow in the qualification environment and confirm that it removes the named key; a retirement failure must block service removal. Do not use a production endpoint for this retirement test.

## Lifecycle and retirement

The service creates the named machine key only when TPM hardening is enabled and a baseline is prepared. It stores the public SubjectPublicKeyInfo bytes through `Settings.StoreTpmIntegrityPublicKey`, which writes binary local preference data and atomically republishes the effective settings generation. Reads use `Settings.TpmIntegrityPublicKey`, so the normal policy, preference, and legacy-registry precedence applies consistently before signing and before verifying a signed baseline.

Retention is evaluated per comparable baseline lineage, including algorithm, so successful TPM baselines cannot evict the latest filesystem or registry fallback lineage. The binary manifest independently encodes nullable values, byte arrays, member fields, and metadata that affect reconciliation; this avoids delimiter collisions and distinguishes missing evidence from present-but-empty evidence.

The `WinFIMLog.exe uninstall` action stops the service and then deletes the named TPM key before deleting the service. If key retirement fails, uninstall stops with an error rather than deleting the service and leaving the TPM key in place. Microsoft documents that persisted CNG keys are deleted by passing the key handle to `NCryptDeleteKey`; .NET invokes this operation through `CngKey.Delete`.[2]

## Operational limitation

This is a **local tamper-evidence** hardening control, not a remote root of trust. The TPM keeps the private key non-exportable, but a privileged local administrator can still alter the installed application, its process, its local database, or its configuration. Without a remote verifier, attestation service, protected code-execution boundary, or independent evidence store, WinFIMLog cannot provide a conclusive guarantee against a malicious administrator controlling the host. Microsoft’s key-attestation model uses a certification authority to verify TPM protection and trust the TPM identity; that external validation boundary is deliberately outside this deployment.[3]

SIEM users should collect Event ID 7798 as an integrity-hardening coverage-gap signal and alert whenever it occurs on a device where the policy is intended to be enabled.

## References

[1]: https://learn.microsoft.com/en-us/windows/win32/seccertenroll/cng-key-storage-providers "CNG Key Storage Providers — Microsoft Learn"

[2]: https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptdeletekey "NCryptDeleteKey function — Microsoft Learn"

[3]: https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/component-updates/tpm-key-attestation "TPM Key Attestation — Microsoft Learn"
