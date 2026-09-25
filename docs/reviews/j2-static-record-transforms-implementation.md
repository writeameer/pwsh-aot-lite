# J2 static record-transform implementation review ledger

**Status:** Native AOT / typed-data boundary review passed. The diagnostic and
compatibility correction is ready for a fresh review; this remains a Step-7
code-evidence ledger, not a migration or release claim.

## Scope

The branch contains exactly two descriptor-bound, post-source record stages:

1. `Where-Object`: literal record field + one generated numeric operator +
   finite numeric value.
2. `Select-Object`: one-or-more literal explicit record fields.

All other J2 commands, all source-position uses, `PSObject`/ETS, ScriptBlock
execution, providers, dynamic engine services, runtime discovery, and actual
transport endpoints remain outside the implementation.

## Required independent verdicts

| Lens | Reviewer | Status | Evidence to check |
| --- | --- | --- | --- |
| Architecture / AOT | Native AOT Boundary Sentinel | **PASS** (2026-09-25) | `StaticRecordStages.cs`, closed `IAotRecordBatchStage`, generated metadata ownership, no transport/runtime registration |
| Diagnostics / compatibility | Compatibility Proof Adversary | **re-review requested** (2026-09-25) | corrected `AOT6404` binding boundary, managed/native snapshots, stock oracle and 52-ID corpus |

## Evidence prepared

- Generated source metadata: `GeneratedCmdletPorts.WhereObject` and
  `GeneratedCmdletPorts.SelectObject`; no aliases are copied into the J2
  descriptor.
- Redirect: `UpstreamAstPipelineLowerer` removes direct structural
  `Where-Object`/`Select-Object` dispatch and binds the one stage registry
  route.
- Test-only transport contract proof: ordered
  `AotValue.List(AotValue.Record(...))` reconstruction equality, without an
  endpoint, listener, package, transport registration, or configuration path.
- Parser fixture/baseline: `38-j2-static-record-stage-redirects`.
- Per-command evidence: [Where-Object](../cmdlets/where-object.md) and
  [Select-Object](../cmdlets/select-object.md).
- Verification run: managed Release build/self-test; parser-reuse guard;
  38-fixture parser differential baseline; fresh self-contained `osx-arm64`
  publish at `/private/tmp/pwsh-aot-lite-j2-static-aot`; native self-test;
  positive `Get-TimeZone` filter/projection smoke; and native ScriptBlock
  rejection with `AOT6406`. The documented Homebrew OpenSSL/Brotli
  `LIBRARY_PATH` was required for the macOS linker.

## Native AOT / typed-data boundary review — PASS (2026-09-25)

Reviewed commit: `4dbd7e11b9f5c784d81f7b56d3b658ae7bd7a1c5`.

- `AotStaticRecordStageRegistry` is a fixed, compile-time dictionary of only
  the two source-generated command identities; both descriptors select their
  admitted parameter surfaces through `GeneratedCmdletPorts.*.CreateAotDescriptor`.
  It neither scans assemblies nor discovers plugins.
- The only new execution seam is
  `AotRecordBatch -> IAotRecordBatchStage -> AotRecordBatch`. Its types are
  internal and closed over `AotRecord`, `AotValue`, `AotRecordShape`, and
  `CmdletDescriptor`; no CLR `object`, `PSObject`, provider, transport,
  endpoint, or arbitrary `IPipelineRecord` can cross it.
- The lowerer recognizes the two names from the reused upstream `CommandAst`
  before the ordinary command route, rejects source-position and ScriptBlock
  forms, and passes original AST-derived arguments to the descriptor route.
  The pre-J2 `AOT400*` structural parsing helpers have no J2 execution call
  site.
- Numeric normalization is limited to a single upstream `BareWord` atom and
  produces an `AotValue`; it does not tokenize source, invoke PowerShell
  conversion, reflect over a type, or execute a ScriptBlock.
- Diff and dependency scans found no added PowerShell SDK/SMA reference,
  `PSObject`, reflection/emit, dynamic dispatch, expression compilation,
  assembly loading, runspace, provider/plugin registration, or transport
  endpoint/configuration path.
- Independently reproduced evidence in an isolated worktree: parser reuse
  guard passed; all 38 parser baselines (including fixture 38) verified;
  managed Release self-test passed; fresh self-contained `osx-arm64` Native
  AOT publish and self-test passed; the `Get-TimeZone | Where-Object |
  Select-Object` smoke emitted `UTC`; and ScriptBlock input failed closed with
  source-spanned `AOT6406`. The upstream-derived language extraction retains
  its pre-existing warning set; this J2 diff added no new dynamic-runtime
  dependency.

## Diagnostic / compatibility review — correction submitted (2026-09-25)

Reviewed commit: `4dbd7e11b9f5c784d81f7b56d3b658ae7bd7a1c5`.

### Original blocking evidence

`AotSelectStaticFieldsDescriptor.Bind` does not retain the generated
`Property` parameter's positional-binding boundary. It collects every direct
argument both before and after a named `-Property` group, so it accepts syntax
that stock PowerShell rejects:

```powershell
# Stock pwsh: "A positional parameter cannot be found ..."
1..2 | Select-Object Foo -Property Bar
1..2 | Select-Object -Property Foo Bar

# Current Native AOT J2: both successfully project Foo, Bar
Get-TimeZone -Id UTC | Select-Object Id -Property BaseUtcOffsetMinutes
Get-TimeZone -Id UTC | Select-Object -Property Id BaseUtcOffsetMinutes
```

The second pair was reproduced against a freshly published `osx-arm64` J2
artifact. This is an acceptance expansion, not merely a different diagnostic:
the stage emits a successful table. It conflicts with the J2 readiness
contract that `Property` is one source-derived positional group **or** the
generated `Property` parameter group, and with the per-command assertion that
metadata, position, and parameter sets remain source owned. The current
descriptor's generated-name/alias lookups are useful, but they do not by
themselves preserve generated positional binding.

The smallest correction is to reject a positional property group combined
with `-Property`, and reject bare property values after a `-Property` group,
through one stable source-spanned `AOT6403`/`AOT6404` binding diagnostic.
Add managed and native negative snapshots for both forms above, and include
them in the stock-oracle matrix. Do not merge or describe the J2 command
surface as supported until the corrected binder accepts only the declared
forms.

### Corrective evidence

- `Select-Object` now retains each upstream AST expression as a source group.
  The descriptor admits exactly one positional group **or** exactly one named
  generated `-Property` group. It rejects both mixed forms and a second bare
  group with one source-spanned `AOT6404` route; no legacy or secondary binder
  can accept them.
- `AssertJ2DiagnosticSnapshot` fixes managed renderer snapshots for both mixed
  forms and wildcard property syntax. The wildcard is now `AOT6404`, matching
  the readiness diagnostic table.
- `tests/j2-static-record-transform-fixtures.json` is embedded in the native
  artifact and mechanically asserts 52 unique evidence IDs: 12 grammar, 14
  binder/diagnostic, 12 batch, eight stock-oracle, and six Native AOT smoke.
  Its count and category distribution are executed by the managed and native
  self-tests.
- The per-cmdlet notes retain the eight controlled stock-oracle cases: UTC
  positional/named projection, positional/named numeric composition, a
  controlled culture projection, both invalid mixed forms, and the explicit
  wildcard variance. The two mixed cases are rejection-equivalent to stock;
  all positive rows preserve their stated table headers/values after the
  documented closed-record normalization. The recorded stock oracle was
  `pwsh` 7.6.6 on the same macOS host.
- `tools/Test-J2StaticRecordTransformCompatibility.ps1` executes those eight
  stock rows and six focused native checks from an explicitly supplied fresh
  artifact. Its two mixed native cases assert the exact `<command>:1:41` and
  `<command>:1:51` source spans, so the native evidence is a repeatable
  snapshot rather than a transcript-only claim.
- Fresh verification after the correction passed: managed Release build and
  self-test; parser-reuse guard; all 38 parser differential baselines; fresh
  self-contained `osx-arm64` Native AOT publish and self-test; a positive UTC
  filter/projection smoke; both mixed-binding `AOT6404` source snapshots; and
  wildcard `AOT6404`. The native executable SHA-256 is
  `77ddd76474bb848ec8e773a81321cd1f13411656dc02576f9cc02bc174682881`.

### Re-review scope

The original compatibility concerns are addressed only in the accepted J2
slice. Re-review must retain the existing source-position `AOT6401`,
ScriptBlock `AOT6406`, quoted-numeric rejection, AST-provenance bare/signed
numeric acceptance, field/operator/value spans, absence of any J2 `AOT400*`
route, parser baseline 38, managed/parser gates, and fresh native evidence.
Timing and variance records remain deliberately in-progress/not-migrated:
neither a review pass nor this correction changes catalog availability,
migration count, or timing end.

## Gate

No merge, catalog availability change, migration count, timing end, or release
is permitted until both independent verdicts PASS and all required managed,
parser, fresh Native AOT, and focused smoke checks pass.
