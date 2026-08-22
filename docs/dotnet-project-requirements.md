# .NET Project Requirements

## Purpose

This document records reusable engineering requirements established for .NET projects that use configuration, the Windows Registry, cryptographic evidence, and durable local state. It is intended to guide implementation and review work in this repository.

## Type-First Design

Application algorithms, operating modes, status values, and other finite protocol details **must be represented by C# enums with explicit integer values**. Code must use those enum members rather than free-form string labels. This requirement applies to baseline algorithms, optional integrity modes, fallback selections, and externally emitted identifiers.

| Requirement | Required practice |
|---|---|
| Finite application value | Declare an enum with an explicit `int` underlying value. |
| Runtime comparison | Compare enum values, not labels or concatenated version strings. |
| Persisted identifier | Store the enum’s stable integer value. |
| Event payload | Emit the stable integer when an algorithm or mode is sent as structured data. |
| Diagnostic presentation | Convert to text only at a genuine human-facing boundary, such as a message template or documentation. |

Free-form strings remain appropriate only when they are inherently textual data, such as paths, identities, error explanations, CNG container names, registry value names, and compatibility migrations for data written by earlier versions.

## Registry and Group Policy

Configuration values that model an enum or Boolean-like mode **must use `REG_DWORD` values**. The code must validate that the read value maps to a defined enum member before publishing it as effective configuration. Invalid values must fail configuration loading clearly rather than silently being coerced.

| Concern | Requirement |
|---|---|
| Policy value | Use the same DWORD-backed setting and stable numeric values as the local preference. |
| Precedence | Resolve Group Policy, local preference, and legacy values through the project’s central `Settings` and registry-precedence model. |
| Component access | Runtime components must obtain effective configuration from `Settings`; they must not read or write the registry directly. |
| Change publication | Settings changes must be published atomically as a complete effective-settings generation. |
| Cryptographic bytes | Store public keys, signatures, hashes, and similar byte material as binary registry values or `byte[]`, never as Base64 or hexadecimal unless crossing a textual external protocol boundary. |

## Cryptographic Integrity

SHA-256 file hashing remains a digest operation. Optional authenticated integrity mechanisms must use a cryptographically compatible construction.

> An HMAC requires the verifier to possess the same secret key. A public key cannot verify an HMAC. Where verification uses a public key, use a digital signature and keep the private key protected.

For TPM-backed signing, use a non-exportable TPM key, retain only binary public-key material in configuration, sign canonical baseline evidence, and verify a prior signed baseline before using it for reconciliation. The signed evidence must bind all persisted fields that affect integrity conclusions, including the member count.

## Secure Fallback and Lifecycle Behavior

Optional hardening must not break the default SHA-256 path. When a policy-enabled TPM operation is unavailable or sealing fails, the application must emit a structured error event, use the source-native fallback enum, and preserve the latest comparable fallback baseline before reconciliation. This preserves change detection during an operational degradation.

Uninstall or retirement workflows must stop dependent services before deleting cryptographic material. If key retirement fails, the workflow must fail safely rather than remove the application while leaving unmanaged key material behind.

## Compatibility and Migration

When replacing persisted string or textual encodings with enums and binary values, add a **versioned, one-time migration**. It must run transactionally, materialize data before modifying a collection, convert only recognized legacy values, and fail clearly on an unsupported legacy value. Tests must cover migration of both algorithm identifiers and binary cryptographic evidence.

Legacy versions with different evidence semantics must not be relabeled as a current algorithm. Preserve such rows as invalid audit history or represent the legacy algorithm separately so that reconciliation cannot compare incompatible evidence contracts.

## Test and Validation Requirements

Tests must focus on correctness, not merely compilation. At a minimum, cover enum-value mappings, invalid DWORD rejection, effective-settings publication changes, registry-policy precedence, compatibility migration, cryptographic manifest determinism, missing-signature rejection, fallback event payloads, and preservation of reconciliation lineage after a hardening failure.

Before delivering a patch, build and run the full test suite in a clean, isolated worktree with declared project dependencies initialized. Validate that the patch applies cleanly, contains no terminal escape or carriage-return control characters, and passes whitespace checks.

## Review Checklist

| Review question | Expected answer |
|---|---|
| Are finite values modeled as explicit integer enums? | Yes. |
| Are enum-like registry settings stored and validated as DWORDs? | Yes. |
| Are components using `Settings` rather than direct registry access? | Yes. |
| Is cryptographic material stored and handled as bytes? | Yes. |
| Are strings limited to inherently textual or compatibility boundaries? | Yes. |
| Does fallback preserve both observability and reconciliation continuity? | Yes. |
| Is migration transactional and tested? | Yes. |
| Is the patch independently buildable and testable? | Yes. |
