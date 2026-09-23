# Review: Convert-Path Wave 3 direct canonical-text slice

Claim: generated Path-only `Convert-Path` reuses the released closed physical
resolution seam and emits existing prose text only.

- Target source base: `51697c7`; compiler provenance: local compiler `main`
  `a56e3b9`.
- The whole collection is prevalidated before any resolution: first exact empty
  value produces sole AOT6213, zero rows/resolver calls/other diagnostics.
- Evidence: managed self-test; parser guard/baseline 35; campaign/queue;
  strict `pwsh` 7.6.6 native oracle; fresh artifact provenance below.
- Fresh native release proof: `artifacts/osx-arm64-convert-path-final/PwshAotLite`,
  built from the `codex/convert-path` source delta over target base `51697c7`
  using local compiler `main` `a56e3b9`; SHA-256
  `e199a2d9b31940956e0e66c4ab299c347620584090c1bda3cada51a207cab023`.

| Persona | Verdict |
| --- | --- |
| Grammar steward | **PASS** — The adapter adds no lexer, grammar, parser, AST, or lowerer. `tools/Test-ParserReuseGuard.ps1` passed and the upstream-parser differential baseline verification passed all 35 fixtures, including `35-convert-path-direct-physical.ps1`. Its deferred `-LiteralPath` and `-Force` forms remain upstream-parsed syntax that the shared static binder rejects; no alternate grammar was introduced. |
| AOT boundary | **PASS** — `ConvertPathCmdlet` is thin: it preflights the full bound collection before its only resolver call, calls only the existing `IPhysicalChildItemCatalog.ResolveExistingDirectPhysicalPath`, projects an already-resolved path through existing `TextRecord`, and adds no resolver, record, table, provider, `File.Exists`/`Directory.Exists`, `FileInfo`/`DirectoryInfo`, reflection, or dynamic fallback. The released resolver retains the single all-components no-follow descriptor acquisition and closed Resolved/Missing/Rejected result. The focused runner self-test now uses a counting closed-outcome catalog: empty values at index zero, middle, and end each throw exactly one source-spanned `AOT6213` with zero catalog calls, rows, context errors, or transcript events; a no-empty resolved/missing/rejected/resolved sequence proves ordered text rows and each emitted catalog diagnostic retains its own value span. Fresh native self-test and the strict oracle passed against the immutable release-proof artifact recorded above. |
| Data-plane/binder | **PASS** — The descriptor is exactly `GeneratedCmdletPorts.ConvertPath.CreateAotDescriptor("Path")`; no `LiteralPath`, `Force`, pipeline, or property-name binding is admitted. The first exact empty string is checked across the whole collection before the resolver loop and throws source-spanned `AOT6213`, so it cannot produce a partial typed batch. Successful entries alone become the existing scalar `TextRecord`, with `AotTerminalPresentation.Prose`; there is no `PathInfo`, `PSObject`, table, or dynamic property bridge. The focused native check confirms `-LP` and `-Force` fail through `AOT2002`. |
| Compatibility adversary | **PASS** — The strict stock `pwsh` 7.6.6/native oracle ran against the immutable artifact recorded above. It compares exact direct file, directory, and multi-value prose output; validates no-empty missing/rejected continuation, whole-collection empty zero-output `AOT6213`, whitespace-as-literal `AOT6206`, and provider/wildcard/link/LP/Force fail-closed boundaries. It now creates a controlled fixture root, runs both stock and native with relative `file.txt` from that root and compares exact output, then starts the native image from a different controlled CWD and proves the same relative input is missing (`AOT6206`). This is executable captured-root coverage, not a manual assertion. |
| Reuse reviewer | **PASS** — The upstream `SessionState.Path.GetResolvedProviderPathFromPSPath` / `WriteObject(Collection<string>)` lifecycle is explicitly narrowed to the released direct-existing-resolution seam plus the pre-existing string-shaped `TextRecord`. This is a direct physical-file subset, not claimed provider compatibility. The port does not duplicate the Resolve-Path acquisition flow or invent a formatter; provider, drive, literal, wildcard, Force, transaction, and pipeline behavior remain explicit deferrals. |
