# pwsh-aot-lite porting playbook

This repository is a Native-AOT PowerShell migration spike. It is deliberately
separate from the original PowerShell source tree. Do not edit
`.upstream/PowerShell`; it is the pinned reference checkout restored by
`eng/Restore-Upstream.ps1` and is ignored by Git.

## Goal and boundary

Port one cmdlet at a time into this runner. A port is not a textual copy of a
PowerShell cmdlet: retain its command behavior while replacing its dependency
on `System.Management.Automation` with the AOT runtime abstractions in this
repository.

The runner must remain Native-AOT safe:

- no PowerShell SDK reference;
- no reflection-based command discovery;
- no dynamic assembly loading;
- no expression compilation or runtime code generation.

## Required lifecycle entry point

Before any cmdlet research, converter work, or runtime implementation, follow
the canonical [cmdlet-port lifecycle](docs/campaign/cmdlet-port-lifecycle.md).
A command counts as migrated only after its verified Native AOT implementation
is non-fast-forward merged and pushed in lifecycle step 9. The detailed port
checklist below expands lifecycle steps 6–9; it does not replace the lifecycle.

## Read before changing code

1. `docs/campaign/cmdlet-port-lifecycle.md` — required campaign entry point,
   exact migration count rule, evidence locations, and release discipline.
2. `ARCHITECTURE.md` — architecture, guardrails, and non-negotiable port rules.
3. `PORTING.md` — source-to-target mapping.
4. `port-variances.json` — durable evidence of every intentional difference.
5. `tools/PwshAotPortGenerator/PortManifestGenerator.cs` — compile-time source
   contract extractor.
6. `PARSER-REUSE-GUARD.md` — mandatory language-fidelity and AOT-boundary
   gates.  PowerShell grammar is upstream source, not a new grammar to grow.
7. `docs/architecture/reviewer-personas.md` — mandatory independent-review
   personas, dispatch matrix, verdict authority, and review-ledger process.
8. `docs/architecture/dual-lens-architecture-review.md` — mandatory DLAR
   gate for profile families, shared substrates, value/pipeline boundaries,
   formatting/diagnostics, and compatibility-affecting behavior.
9. `docs/architecture/language-tooling-contract.md` — the shared parser
   contract for execution, CLI tooling, and eventual editor/LSP consumers.
10. `docs/architecture/diagnostic-contract.md` — mandatory Phase-1 structured
   diagnostics, renderer, and negative-test requirements.
11. `docs/architecture/language-compatibility-core.md` — the current reviewed
   block-plan, lexical-scope, expression, and delayed-binder boundary.
12. `docs/architecture/terminal-presentation.md` — ANSI policy, sanitization,
   and the shared-parser boundary for future interactive highlighting.
13. `docs/architecture/provider-host-substrate.md` — capability authority map
   for physical files, process inspection, host/platform state, and explicit
   unavailable credential/network boundaries.
14. `docs/campaign/phase10-built-in-cmdlets.md` — checked 290-declaration
   campaign classification; do not port outside its prerequisites or silently
   reclassify a row.
15. `docs/architecture/upstream-reuse-governance.md` — mandatory anti-NIH
    evidence matrix, output/format-contract provenance, permitted AOT
    replacement exceptions, and reviewer BLOCK criteria.

## Mandatory independent review process

Do not treat architecture review as optional. The implementing agent must
dispatch the applicable **read-only independent reviewers** before declaring a
change integrated or describing behavior as supported. A single `BLOCK`
prevents integration into the `PwshAotLite` executable path or a support claim.
Resolve it by changing the implementation, narrowing the claim, or recording
an explicit variance; never silently bypass it.

| Change | Required reviewers |
| --- | --- |
| Lexer, parser, AST, lowerer, or accepted syntax | **Upstream Grammar Steward** + **Native AOT Boundary Sentinel** |
| Parser facade, token/span/diagnostic API, syntax highlighting, completion analysis, REPL editing, or editor/LSP integration | **Upstream Grammar Steward** + **Language Tooling Contract Guardian**; add **Native AOT Boundary Sentinel** when executable dependencies change |
| Execution-kernel behavior, binding, runtime/cmdlet error, or an unsupported-feature diagnostic | **Diagnostic Experience Guardian**; add the applicable parser, data-plane, and AOT reviewers |
| `AotValue`, pipeline adapter, generic data cmdlet, or parameter binding | **Native AOT Boundary Sentinel** + **Static Data-Plane & Binder Guardian** |
| Cmdlet port or cross-cutting control-plane feature | **Static Data-Plane & Binder Guardian**; add **Native AOT Boundary Sentinel** when dependencies or execution change |
| Any feature/milestone described as supported | All three above + **Compatibility Proof Adversary** |

Each reviewer must receive the scoped change/claim, relevant source and
upstream locations, applicable guard documents, and exact build/test/AOT
evidence. Their report must state `PASS` or `BLOCK`, cite evidence, and name
the smallest corrective action. Create a ledger entry under `docs/reviews/`
from `docs/reviews/TEMPLATE.md`, and link it from the affected port or design
note.

## Dual-Lens Architecture Review (DLAR)

Before integration, invoke the two independent DLAR lenses whenever a change
adds or changes a cmdlet-profile family, shared substrate, pipeline/value
boundary, formatting/diagnostic contract, or compatibility-affecting behavior.
Use `docs/architecture/dual-lens-architecture-review.md` and retain the result
with `docs/reviews/DLAR-TEMPLATE.md`. DLAR is not a substitute for the
mandatory reviewer matrix above: one `BLOCK` from either DLAR lens prevents
integration or a support claim. Lenses are evidence-backed design perspectives,
never impersonations of real people.

For a new profile family or shared substrate, complete
`docs/architecture/profile-design-preflight.md` **before** dispatching DLAR.
It is the required closure checklist for source pins, authority, static
parameters/aliases/outputs/diagnostics, closed grammar and types, collection
and property binding, target fixtures/counts, and deferred outcomes. Missing
preflight evidence is a DLAR `BLOCK`, not work for the lenses to infer.

## Slice branch and integration rule

Every implementation slice uses its own `codex/<slice-name>` branch. A
branch/release steward creates the branch, and after the required independent
reviews and verification pass, merges it into `main` and pushes both the merge
and source branch history. A failed review is not merged: narrow, repair, or
defer the slice first. Keep one slice focused enough that its review ledger
names a single support claim.

## Parser and evaluator rule

- Never extend `frontend-spike/`; it is an archived proof and must not be
  referenced by the host project.
- Production syntax must be a pinned, attributed extraction/adaptation of the
  upstream handwritten tokenizer, parser, and structural AST. Record copied
  files, copyright/MIT notices, upstream commit, and intentional engine-hook
  removals in `UPSTREAM.md`.
- Grammar behavior must be differentially tested against stock `pwsh` for token
  kind/text/extents, AST shape, and diagnostic IDs. Parsing a construct is not
  permission to execute it: unsupported nodes must fail in the lowerer with a
  stable explicit diagnostic.
- Treat the upstream parser facade as a shared, non-executing language service:
  its AST, tokens, extents, and diagnostics serve the AOT execution kernel,
  CLI tooling, and future editor/LSP consumers. Do not create an
  execute-only parser API or a separate tooling grammar/lexer.
- Big Rock 1, the **AOT Execution Kernel**, is incomplete without the shared
  `AotDiagnostic` contract and renderer. Every supported, malformed,
  unsupported, binding, and runtime-error path must have a stable diagnostic
  ID, actionable message, source extent when source exists, and a negative
  snapshot test. Do not leak raw exceptions or add command-specific console
  error formatting; see `docs/architecture/diagnostic-contract.md`.
- Do not import the dynamic execution engine: `Compiler.cs`, `PSObject`,
  runspaces, `PowerShell`, expression compilation, Reflection.Emit, runtime
  assembly loading, or runtime code generation are outside the AOT path.
- Do not make a second command binder. AST lowering must use generated source
  metadata plus `CmdletDescriptor` and `AotCmdletRegistry` for aliases,
  parameter sets, validation, help, and availability.

## Port workflow (lifecycle steps 6–9 implementation detail)

The canonical lifecycle owns planning, converter, target-readiness, and release
state. This checklist begins once lifecycle step 6 has approved target work and
expands steps 6–9 without changing their gates or migrated-count rule.

1. Before research or implementation, create `docs/cmdlets/<command-name>.md`
   from `docs/cmdlets/TEMPLATE.md` and add an `in progress` row to
   `docs/cmdlets/port-timing.md`. Record the UTC start at that moment. At
   integration, record the UTC end and end-minus-start wall-clock duration in
   both places. Duration includes elapsed review/build time, not just active
   engineering time. Historical values that were not recorded stay
   `unavailable (not recorded)`; never estimate them.
2. Run `eng/Restore-Upstream.ps1`, then find the original `[Cmdlet]`
   implementation under `.upstream/PowerShell/src`.
3. Build this project once. Inspect the generated contract beneath
   `obj/Generated/.../GeneratedCmdletPorts.g.cs`; it preserves command metadata,
   parameter-set bindings, aliases, lifecycle, base types, outputs, validation,
   and engine blockers.
4. Before writing target behavior, complete the upstream-reuse evidence matrix
   in the cmdlet note as specified by
   `docs/architecture/upstream-reuse-governance.md`. It must identify exact
   upstream source and format/type-data evidence for business logic, every
   emitted field, and default display columns, then prove each admitted output
   surface against a controlled normal-`pwsh` versus Native-AOT runtime oracle
   comparison with documented normalization. Shared behavior belongs in a
   reusable AOT service or `AotCmdletBase`, not in a new command-specific
   helper.
5. Add a `IAotCmdlet` implementation, normally derived from `AotCmdletBase`.
   Put command-specific behavior in `ProcessRecord`; keep lifecycle, stream,
   cancellation, and error policy shared.
6. Construct the active `CmdletDescriptor` from generated metadata with
   `CreateAotDescriptor(...)`. Include only parameters actually implemented.
   The parser must reject unsupported parameters rather than accept-and-ignore.
7. Replace every generated migration blocker with one of:
   - a shared AOT runtime service;
   - a narrow platform/host interface; or
   - an explicit deferred/out-of-scope decision.
8. Add fixture tests to `SelfTest` for parameter binding, aliases, parameter
   sets, output, error behavior, and pipeline input. Add a real native test for
   platform behavior.
9. Add a complete entry to `port-variances.json`. Record preserved behavior,
   replacements, subsets, deferrals, blockers, a concrete why-not-copy
   rationale, and whether each difference can become shared automation later.
   An AOT replacement needs the approved-exception evidence in the reuse
   governance document; it is not justified merely by convenience.
10. Only then mark the cmdlet ported in user-facing status documentation and
    complete the timing record after verified integration.

## Reuse patterns

- Use generated metadata as the source of truth; never hand-copy cmdlet
  attributes or aliases.
- Use typed `IPipelineRecord` values for objects flowing through the AOT
  pipeline. Add a reusable record or adapter when another command shares its
  output shape.
- Put operating-system access behind an interface, as `IProcessCatalog` does.
  This permits fixture testing and isolates platform differences.
- Consume only a direct, typed `AotHostSubstrate` capability. Do not introduce
  a provider, runspace, `IHost`, service locator, arbitrary environment access,
  process runner, credential store, or network fallback as a shortcut.
- Prefer shared validation, wildcard, conversion, output, and error services
  when the variance ledger shows a pattern repeated across ports.
- Do not turn an existing dynamic PowerShell feature into a fake no-op. Reject
  it with a clear error until a designed replacement exists.

## Current reference port: Get-Process

`GetProcessCmdlet` is the reference implementation. It demonstrates generated
parameter metadata, process selection, pipeline `InputObject`, module/file
version output, username enrichment, non-terminating errors, catalog-based
platform access, and AOT verification.

## Verification

From this directory:

```powershell
dotnet build -c Release
dotnet run -c Release -- --self-test

$env:LIBRARY_PATH = '/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib'
dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64
./artifacts/osx-arm64/PwshAotLite --self-test
```

Run at least one native command that exercises the port being added. For
`Get-Process`:

```powershell
./artifacts/osx-arm64/PwshAotLite -Command "Get-Process -Name PwshAotLite -Module -FileVersionInfo | Select-Object ModuleName, FileVersion"
```
