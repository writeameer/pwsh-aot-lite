# Provider and host substrate

Phase 9 introduces the fixed, injectable `AotHostSubstrate` composition
boundary. It is the vocabulary a native cmdlet adapter uses for host and
platform dependencies. It is **not** a partial PowerShell provider engine,
`PSHost`, runspace, service locator, or a compatibility object.

## Authority map

| Capability | Current local implementation | Admitted authority | Explicit exclusion |
| --- | --- | --- | --- |
| physical files | `IPhysicalFileResolver` / `SystemPhysicalFileResolver` | direct physical read paths and terminal `*`/`?` enumeration | PS drives, provider paths, recursive provider expansion, content streams |
| direct physical item catalog | `IPhysicalChildItemCatalog` / `SystemPhysicalChildItemCatalog` | captured-root direct physical item, existence, and existing-path facts through one all-component no-follow acquisition | providers/drives, ambient current location, wildcard globber, `PathInfo`, `FileInfo`, `PSObject` |
| processes | `IProcessCatalog` / `SystemProcessCatalog` | inspection and existing `Get-Process` data | arbitrary process launch or a process provider |
| process owner | `IProcessOwnerReader` / `UnixPsProcessOwnerReader` | fixed `/bin/ps` lookup on macOS/Linux only | command lookup, shell invocation, Windows fallback, generic execution API |
| time and globalization | `IClock`, `IHostCulture`, `ICultureCatalog`, `ITimeZoneCatalog` | BCL time/culture/zone snapshot calls | `PSHost`, user-profile/session state |
| bounded elapsed wait | `IAotDelay` / `CancellationTokenDelay` | one cancellable integer-millisecond wait using an execution context's supplied host token | timers, tasks, scheduler, callbacks, a cmdlet-owned cancellation source, or general async work |
| platform | `IAotHostPlatform` / immutable `AotHostPlatformSnapshot` | OS and process architecture selection | implicit foreign P/Invoke fallthrough |
| configuration | `IAotHostConfiguration` / `ProcessAotHostConfiguration` | closed deployment and terminal key list | `Env:` provider, arbitrary variables, mutation, `PATH`/`HOME` authority |
| discovery roots | `IAotHostDiscoveryRoots` / `ProcessAotHostDiscoveryRoots` | one captured current directory and application base for declarative catalog scans | session location, provider navigation, repeated ambient root reads |
| terminal | `IAotTerminalInfoSource` / `SystemTerminalInfoSource` | advisory stderr/ANSI capability capture | terminal object model or execution control |
| credentials | `ICredentialCapability` | typed `AOT6101` unavailable state | prompts, stores, `PSCredential`, environment-secret reads |
| network | `INetworkCapability` | typed `AOT6102` unavailable state | DNS, HTTP, sockets, proxy/default credentials |

`AotHostComposition` creates the one immutable local substrate, its injected
help/module/repository catalogs, completion service, and native registry
dependencies. There is no `GetService<T>`, runtime registration, reflection
discovery, or cmdlet-selected implementation. Fixture tests may construct
capability fakes directly.

## Physical filesystem and installer boundary

The existing `Get-FileHash` physical path resolver is reusable but remains
read-only. `Install-Module` retains its separate trusted write boundary:
bounded package walks, regular-file/symlink policy, configured roots,
same-volume staging, hash verification, and atomic activation are not weakened
or generalized by this substrate. Its explicit roots now arrive via the closed
configuration capability.

No Phase 9 interface accepts `FileSystem::`, `Env:`, `Registry:`, a PS drive,
or a provider-qualified path. A future virtual-file service requires its own
design, diagnostics, trust model, fixtures, and review.

`ResolveExistingDirectPhysicalPath` is a closed catalog operation rather than
a second filesystem resolver: rooted OS paths remain rooted, unrooted paths
are lexical-normalized from the immutable captured root, empty input is a
source-spanned rejection, and whitespace remains literal. Its only result is
`Resolved(DirectPhysicalPathRecord)`, `Missing`, or `Rejected`; consumers may
not invoke `GetDirectPhysicalItem`, `TryDescribe`, `File.Exists`, provider
dispatch, or enumeration to recreate it. The catalog's single metadata-free
descriptor/no-follow acquisition is shared with the direct item and probe
operations; display metadata is acquired only by consumers that actually need
it.

## Platform and process policy

Platform selection is immutable at substrate construction. The current process
owner enrichment is intentionally the only subprocess-shaped operation: it is
isolated behind `IProcessOwnerReader`, uses the absolute `/bin/ps` path with a
fixed argument list, and returns the existing non-terminating diagnostic on
unsupported platforms or failure. It is not a reusable process runner.

Platform-specific P/Invoke used by package activation remains in its separately
reviewed verifier. Ports must add an explicit supported OS/architecture matrix
before adding another platform call; unknown platforms fail closed.

## Credential and network policy

The capability records deliberately communicate absence without a fake command
implementation. No current adapter consumes either capability. A future port
that needs credentials or transport must introduce a scoped trust/authority
design and map the result into a source-aware `AotDiagnostic`; it may not turn
the unavailable records into a silent no-op or ambient fallback.

## Bounded delay policy

`IAotDelay` is intentionally narrower than a clock or scheduler. Its only
operation accepts a non-negative integer milliseconds value and the
`AotExecutionContext` token supplied by the host. `CancellationTokenDelay`
uses that token's wait handle and checks cancellation before and after the
wait. It cannot arrange callbacks, enqueue work, create a timer, select a
thread, or manufacture/cancel a token. The first consumer is the narrow
`Start-Sleep -Milliseconds` port. A port needing deadlines, periodic work,
parallelism, or console-signal ownership must introduce a separate reviewed
host design rather than widening this interface.

## Porting rule

An adapter may consume an existing typed substrate capability. Adding a new
capability requires an AOT-boundary, diagnostic, architecture, and compatibility
review, plus a variance entry that names its authority and unsupported modes.
The upstream references remain behavior evidence only; do not transplant
`SessionStateProviderBase`, `FileSystemProvider`, `PSHost`, `Credential`, or
`NativeCommandProcessor` into this runner.
