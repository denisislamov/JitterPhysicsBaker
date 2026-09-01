# Requirements and compatibility

[Documentation home](index.md) · [Installation](installation.md) ·
[Quick Start](quick-start.md) · [Troubleshooting](troubleshooting.md)

This page separates declared requirements from configurations that have actually been exercised.
An assembly definition without platform exclusions is not, by itself, proof that every Unity
player target works.

## Support vocabulary

| Term | Meaning |
| --- | --- |
| Declared | The package manifest or source contract requires this configuration. |
| Verified | The repository has exercised this configuration with a recorded build or test run. |
| Unverified | The design allows it, but the current release has no completed acceptance gate for it. |
| Unsupported | The package detects or rejects this configuration deliberately. |

## Compatibility matrix

| Area | Status | Details |
| --- | --- | --- |
| Unity minimum | Declared | Unity `6000.3`, from the package manifest. |
| Unity Editor | Blocked for this release | Unity `6000.3.19f1` is the exact editor revision used by the development project. The `0.7.0` rerun was blocked before fresh XML by Licensing Client protocol mismatch. |
| Later Unity versions | Unverified | `6000.3` is a minimum, not evidence that every later editor release has been tested. Run the package and consumer suites before adopting a later editor. |
| Clean import without Jitter2 | Verified design contract | The always-compiled package assemblies do not reference `Jitter2.Core`. Import and authoring UI remain available when Jitter2 is absent. Baking and world construction do not. |
| Unity Editor scripting backend | Compile verified, engine run blocked | Generated Unity csproj compilation passes; fresh Edit Mode and Play Mode XML is not available for `0.7.0`. |
| Mono player | Unverified for this release | Editor tests do not replace a built-player smoke test. |
| IL2CPP player | Unverified for this release | IL2CPP is an intended integration target, but a completed IL2CPP player gate is not recorded for this documentation release. Do not present it as certified. |
| Render pipelines | Unverified matrix | The package has no URP or HDRP package dependency, but Built-in/URP/HDRP player acceptance has not been recorded as a release matrix. |
| Desktop and mobile players | Unverified matrix | The runtime assemblies have no platform include/exclude list. That makes them eligible to compile; it does not prove Windows, macOS, Linux, Android, or iOS acceptance. |
| WebGL and consoles | Unverified | No current release evidence certifies these targets. Check threading, managed-plugin, AOT, and platform restrictions in a consumer build. |
| Portable server code | Verified under the repository harness | `Contracts`, `ArtifactCodec`, and the shared Jitter integration are compiled and tested by the repository's .NET 10 harness. |
| Consumer dedicated server | Integration-dependent | The package projects sources into the consumer server. The consumer still owns hosting, deployment, Jitter2, the tick loop, and connection approval. See [Dedicated server integration](dedicated-server.md). |
| Networking framework | Framework-agnostic | The package has no Netick, NGO, Mirror, transport, EFT, or NPI runtime dependency. The consumer carries the compatibility token in its own handshake. |

> [!IMPORTANT]
> A successful package test run is not a player-platform or two-client acceptance result. Report
> package checks, Unity tests, built-player smoke tests, server tests, and end-to-end networking as
> separate gates.

> [!NOTE]
> The `0.7.0` Unity rerun did not reach test discovery: the existing Licensing Client answered
> with unsupported protocol `1.18.1` (`ResponseCode 505`), and the editor-specific client did not
> establish its channel. Older XML is not a current-release pass. Rerun both Unity suites before
> publishing the release tag.

## Unity Package dependencies

The package manifest declares only Unity modules:

| Package | Why it is used |
| --- | --- |
| `com.unity.modules.jsonserialize` | Project settings, receipts, manifests, and diagnostic data. |
| `com.unity.modules.physics` | Unity collider authoring and conversion. |
| `com.unity.modules.imgui` | Editor windows and inspectors. |
| `com.unity.modules.uielements` | Native editor integration, including the Scene View overlay. |
| `com.unity.modules.unitywebrequest` | Explicit artifact upload from the Editor. |

Jitter2 is deliberately not a manifest dependency. A consumer may use the package-owned fallback
or one compatible project-owned source copy. The integration adapter is installed only after that
choice has been made.

The package does not require the Input System. The imported demos support the active Unity input
backend without adding `com.unity.inputsystem` to the base package.

## Jitter2 compatibility

Baking requires one, and only one, provably compatible `Jitter2.Core`.

| Setup status | Meaning | Required action |
| --- | --- | --- |
| `Missing` | No `Jitter2.Core` is available. | Install the bundled fallback or add a compatible source copy. |
| `Compatible` | The project-owned source hash, or the receipt-owned fallback, matches `jitter2.lock.json`. | No Jitter2 change is required. |
| `Incompatible` | The source set or installed fallback differs from the package lock. | Align client and server on one supported source set before baking. |
| `Duplicate` | More than one assembly definition declares `Jitter2.Core`. | Remove the duplicate. The package will not guess which physics implementation to use. |
| `UnsupportedPlugin` | A precompiled `Jitter2.Core` exists without the package receipt, so its source identity cannot be proven. | Supply a compatible source copy, or remove it and use the package-owned fallback. |

An external Jitter2 copy remains consumer-owned. The installer locates it by assembly name and
does not copy, move, edit, or uninstall it.

The bundled fallback is different from an arbitrary precompiled plugin: its exact source hash,
compile profile, installed files, and file hashes are recorded by the package receipt. That
receipt is what allows the compatibility report to prove its identity.

## Assembly and runtime boundaries

| Layer | Unity dependency | Jitter2 dependency | Intended use |
| --- | --- | --- | --- |
| `DataSakura.JitterPhysics.Contracts` | None | None | Portable DTOs, identifiers, provider/preview contracts, and typed error results. |
| `DataSakura.JitterPhysics.ArtifactCodec` | None | None | Canonical binary/manifest codecs, hashes, providers, validation, runtime identity, and compatibility token. |
| `DataSakura.JitterPhysics.UnityArtifact` | Yes | None | Unity asset references and project paths. |
| `DataSakura.JitterPhysics.Authoring` | Yes | None | Scene components and world-profile assets. |
| `DataSakura.JitterPhysics.Editor` | Editor only | None | Validation, baking, installation, export, settings, diagnostics, and preview. |
| Installed `DataSakura.JitterPhysics.JitterIntegration` | Consumer Unity project or server projection | Yes | Rebuilds the Jitter2 world from validated records. |

The package never owns `World.Step`, dynamic-body creation, networking, prediction,
reconciliation, or connection approval. Build the static artifact into a new world before adding
dynamic bodies and before the first step. See [Runtime API](runtime-api.md) and
[Integration](integration.md).

## AOT, stripping, and threading

- No current `link.xml` contract or completed stripping matrix is published for the package.
- The portable result APIs avoid using exceptions for invalid artifact, file, manifest, or network
  input; callers receive typed errors instead.
- The documented sample steps Jitter2 single-threaded. A different threading policy is a consumer
  decision and requires its own platform verification.
- Do not infer IL2CPP or console support from successful Editor compilation.

If a build fails only under IL2CPP, AOT, or stripping, capture the first player-build error and
follow [Troubleshooting](troubleshooting.md). Do not add speculative preservation rules without a
reproduced missing-code path.

## Known runtime limitations

These limitations are properties of the current implementation and must be considered when
integrating it:

- `SubstepCount` is serialized into the world profile and artifact, but the shared world builder
  does not currently assign it to the Jitter2 world. Values greater than `1` therefore do not
  change the rebuilt runtime world.
- A failed world apply removes bodies and restores world settings. If the result reports
  `RequiresWorldDiscard`, discard the failed world and create a new one.
- `TopologyFingerprint` is a diagnostic summary, not a compatibility credential. For mesh shapes
  it includes vertex and index counts, not the complete mesh content. Use
  `artifactHash + runtimeCompatibilityId` for client/server compatibility.
- Bit-identical `World.Step` results across Unity and .NET are not claimed. The package guarantees
  byte-identical artifact input and checked runtime semantics; the server remains authoritative.

## What to verify in a consumer

Before treating a target as supported, record all applicable results independently:

1. The package imports and compiles with no Jitter2.
2. Exactly one compatible Jitter2 is selected.
3. The integration adapter compiles in the consumer assembly graph.
4. Edit Mode and Play Mode tests pass in the consumer Unity version.
5. The actual player target builds and starts under its chosen scripting backend.
6. The same artifact is accepted by the client and server using both compatibility values.
7. The server builds its static world before enabling connection approval.
8. The project's own two-client, reconnect, prediction, and reconciliation flows pass when they
   are in release scope.

Continue with [Installation](installation.md), then follow the reproducible
[Quick Start](quick-start.md).
