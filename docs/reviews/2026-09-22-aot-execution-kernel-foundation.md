# Review: AOT Execution Kernel foundation

Claim reviewed: `The host may use the shared upstream parser facade to compile the existing reviewed structural pipeline subset into an explicit AOT execution plan, and may expose typed source-aware diagnostics for parse, unsupported-syntax, binder, and active-command runtime failures.`

Scope: `AotScriptParser`, `AotExecutionKernel`, `AotExecutionPlan`, typed
diagnostics/renderer, lowerer/binder span propagation, host error boundary,
and foundation documentation. This is not a claim that variables, blocks,
control flow, functions, or all legacy port errors are implemented.

## Evidence

- Provenance: pinned upstream language extraction at
  `1e53f6bbab4b8791eae782474d21889f9e5d6038`; no grammar source was added.
- Managed build and self-test: `dotnet build -c Release --no-restore` and
  `dotnet run -c Release --no-build -- --self-test` passed with zero host
  warnings/errors.
- Parser evidence: `tools/Test-ParserReuseGuard.ps1` passed;
  `tools/Export-PwshParserBaseline.ps1 -Verify` matched all four checked-in
  stock-`pwsh` token/AST/diagnostic fixtures.
- Native evidence: `dotnet publish -c Release -r osx-arm64 --self-contained
  true` (with the documented Homebrew `LIBRARY_PATH`) passed; the published
  binary passed `--self-test` and rendered an exact-span `AOT2002` binder
  diagnostic.
- Diagnostic evidence: `SelfTest` snapshots plain output for unsupported
  syntax, malformed parse, unsupported parameter, and non-terminating runtime
  error; it also verifies an empty diagnostic stream on success plus ANSI and
  width-constrained renderer behavior.

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Upstream Grammar Steward | **PASS** | `AotScriptParser` delegates directly to pinned `Parser.ParseInput`, preserves AST/tokens/parser IDs/extents/document identity, and lowering consumes only that result. No second lexer/parser emerged. | `ScriptParser.Parse` remains a forwarding compatibility name only. Existing parser differential fixtures remain the grammar gate. |
| Language Tooling Contract Guardian | **PASS** | The facade exposes AST/tokens/extents/diagnostics before lowering without binding, import, or execution. Completion remains a non-authoritative metadata scanner. | The obsolete parser-runner architecture note was corrected to name the facade, execution plan, and current diagnostic contract. |
| Native AOT Boundary Sentinel | **PASS** | New parser facade, plan, diagnostics, and renderer use direct static types only; no dynamic loading, reflection dispatch, runtime code generation, SDK/runtime-engine dependency, or new package reference was introduced. Native osx-arm64 publish/self-test passed. | Existing fail-closed upstream compatibility stubs were unchanged and no newly reachable dynamic path was found. |
| Static Data-Plane & Binder Guardian | **PASS** | The generated-metadata `AotCmdletRegistry.BindCommand` remains the only binder. Atom-level spans flow from upstream AST into binder diagnostics, and the closed `AotValue`/`AotRecord` boundary is unchanged. | The currently unreachable legacy input-command branch must enter invocation scope if surfaced later. |
| Diagnostic Experience Guardian | **PASS** | Parser, unsupported syntax, binder, and active-command runtime failures have typed IDs/categories/spans. Binder underlines the exact parameter atom; non-terminating errors inherit active invocation span. Host maps unexpected failures to `AOT9000` without leaking detail. | `AOT3000` remains a documented temporary wrapper for untouched legacy ports. Tab/Unicode-perfect column rendering is deferred; ASCII foundation snapshots are authoritative. |
| Compatibility Proof Adversary | **PASS** | Managed/native positive structural-pipeline tests plus malformed-pipe, script-block, exact binder-span, non-finite predicate (`AOT4002`), invalid operator with operator-token attribution (`AOT4003`), empty projection (`AOT4004`), invalid generic filter/property (`AOT4005`), missing generic projection field (`AOT4008`), conflicting Get-Process parameter-set (`AOT3001`), and non-terminating runtime snapshots passed. | Legacy ports may still originate `AOT3000`, but the shared cmdlet boundary attaches the active command span while each port migrates to an origin-specific ID. |

## Follow-up constraints

1. New kernel/binder work must not use the `AOT3000` legacy wrapper.
2. Variables, blocks, control flow, and functions must extend
   `AotExecutionPlan`; none may bypass the parser facade or generated binder.
3. Each migrated port replaces its legacy error strings with origin-typed
   diagnostics and adds the required negative snapshot.
4. Improve tab/Unicode source-column rendering before claiming general
   source-layout fidelity.
