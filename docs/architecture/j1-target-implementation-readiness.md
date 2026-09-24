# J1 target implementation readiness: lexical path adapters

> **Historical outcome:** this pre-implementation readiness packet led to the
> bounded J1 runtime implementation merged at `9a279d3`. Its design claim is
> preserved as evidence; the current support/verification claim is the
> [J1 implementation review](../reviews/j1-lexical-path-implementation.md).

**Historical status:** design-ready pending independent DLAR. This packet
authorized no code, registration, or runtime-support claim at the time it was
written.

**Work record:** started `2026-09-24T19:01:39Z`; ended
`2026-09-24T19:17:43Z`; elapsed `00:16:04`. This covers the readiness-design
slice only.

## Decision

After DLAR passes, implement exactly two lexical-text adapters:

| Command | Adapter | Closed operation | Output |
| --- | --- | --- | --- |
| `Join-Path` | `JoinPathCmdlet` | `PathText.Compose` | one `TextRecord` per valid base |
| `Split-Path` | `SplitPathCmdlet` | `PathText.Decompose` / `LastDotV1` | text, or closed Boolean for `-IsAbsolute` |

They transform direct physical path *text*. They must not resolve, inspect,
canonicalize, or require an existing item. `IPhysicalChildItemCatalog`,
`IPhysicalFileResolver`, `System.IO.Path`, `Directory`, `File`, providers,
captured roots, current location, and host configuration are forbidden. The
existing physical catalog has filesystem authority and is not reusable here.

## Exact target seams

| File | Future implementation change | Required reuse | Forbidden |
| --- | --- | --- | --- |
| `LexicalPathText.cs` (new) | immutable `PathText`, child fragment, fixed dialect, pure compose/decompose/last-dot | closed `AotValue.String` ingress | filesystem/resolver/provider/platform path APIs or ambient state |
| `AotLanguageCore.cs` and `UpstreamAstPipelineLowerer.cs` | one AST-derived command-value-group carrier; no `ArrayLiteralAst` or evaluated `AotValue.List` flattening for a J1 profile | upstream AST extents and existing expression lowering | text reparse, new lexer/parser, list/string splitting |
| `Pipeline.cs` | one descriptor-owned sequential static-binding branch inside the sole registry binder; two adapters, registry entries, focused self-tests | lifecycle, `CommandInvocation`, `TextRecord`, `BooleanRecord`, static registry | parser, second binder, reflection, generic object/property conversion |
| `HelpCatalog.cs` | native-name availability entries in the same change as registry | generated source catalog and equality check | alternate hand-authored metadata |
| `AotDiagnostics.cs` | no renderer change; typed source-aware errors only | `AotDiagnostic` and `ScriptException` | raw exceptions or command-specific console output |
| grammar fixtures/baselines | admitted syntax and rejected static parameter forms | upstream parser differential harness | lexical parser or text re-tokenizer |
| cmdlet notes/variance/timing | two notes, reuse matrices, timing, variances, proof records | existing templates | completion claim before integration |

`LexicalPathText.cs` is a zero-authority value-plane utility, not an
`AotHostSubstrate` capability. Its dialect is publish-time immutable data;
fixtures may inject explicit POSIX-v1/Windows-v1 values. It cannot query the
active OS at invocation time.

### Exact limited group and binder extension

The current target cannot represent J1 groups: `AddCommandValueArguments`
flattens `ArrayLiteralAst`, `AotCommandArgumentConverter.Append` flattens
`AotValue.List`, and `CommandSyntaxAtom` carries no group identity. The J1
implementation must add one closed carrier, `AotCommandValueGroupPlan`, with
already-lowered expressions, one upstream group extent, and no text form.
`UpstreamAstPipelineLowerer` creates it for an `ArrayLiteralAst` used either
as a command element or as `CommandParameterAst.Argument`; ordinary
comma-separated command elements remain separate. Evaluation creates one
`CommandSyntaxValueGroup` with evaluated scalar atoms and the original group
span. A list never expands while producing a J1 group; a non-scalar member
follows the existing closed-value conversion failure.

`AotCmdletRegistry.BindCommand` remains the only binder. It consumes a closed
`CommandSyntaxArgument` union (parameter atom, scalar value atom, value group),
while its current flat-atom path remains unchanged for every legacy descriptor.
Add one descriptor-owned mode, `SequentialStatic`, declaring ordered positional
slots, group acceptance, and one static-set selector table. The registry chooses
it only for J1. Compose mapping is fixed: a scalar or group in `Path` maps to
an ordered base vector; each base receives the same ordered child vector. A
scalar or group in `ChildPath` maps to that child vector; `AdditionalChildPath`
maps one post-child group or each subsequent scalar in order to the same vector.
Thus `Join-Path a,b c` and `Join-Path -Path a,b -ChildPath c` emit `a/c`, then
`b/c`; `Join-Path a b,c` and `Join-Path -Path a -ChildPath b,c` emit `a/b/c`.
No cartesian product, pairwise zip, or hidden list expansion is admitted.
Decompose validates one selector route before mapping its sole position-0 path.
This branch cannot infer a route or become a general binder. Prove all pre-J1
binder fixtures and non-J1 command results byte-for-byte unchanged, plus J1
group-survival and named/positional vector fixtures.

## Static contracts and binding

### `Join-Path`: compose

- `Path` / `PSPath`: scalar or array at position 0; direct string pipeline
  input only.
- `ChildPath`: one-or-more `PathTextChildFragment` values at position 1.
- `AdditionalChildPath`: zero-or-more fragments at position 2+; an upstream
  remaining-arguments `ValueGroup` remains one atomic group in source order.
- Property-name binding, child pipeline binding, `Resolve`, `Extension`,
  `Credential`, dynamic/transaction parameters, and unsupported common
  parameters fail closed.
- The binder preserves AST groups; it never flattens array literals or
  re-splits evaluated text.

### `Split-Path`: decompose

- `Path` is position 0 only for `Parent` (default), `Leaf`, `LeafBase`,
  `Extension`, and `IsAbsolute` static modes.
- `LiteralPath` / `PSPath` / `LP` is default-parent only; no pipeline or
  selector route.
- Exactly one static mode is chosen before values. A conflict is `AOT6213`;
  no dynamic parameter-set selection occurs.
- `Qualifier`, `NoQualifier`, `Resolve`, `Credential`, dynamic/transaction
  parameters, unsupported common parameters, and property binding fail closed.

J1 does not widen the finite shared `-ErrorAction` / `-Verbose` / `-Debug`
mechanism. It uses its shared diagnostic path; no command-local common binder
is permitted.

## Values, errors, and atomicity

`PathText` accepts only full-string forms in a selected immutable dialect:
POSIX-v1 accepts `/`, Windows-v1 accepts `\\`. Relative text stays relative.
Foreign separators, provider/drive-looking forms, wildcards, malformed roots,
qualification, and NUL fail closed. `PathTextChildFragment` is distinct:
nonempty, unrooted, no empty interior segment, trailing separators preserved.

| ID | Failure | Primary span | Result |
| --- | --- | --- | --- |
| `AOT6210` | dialect/pattern/qualification/foreign/rooted-child | offending value | one error; zero output; zero authority calls |
| `AOT6211` | missing/null/empty path or child | missing parameter or offending value | one error; zero output; zero authority calls |
| `AOT6212` | rejected parameter/alias/property/pipeline route | parameter/property argument | one error; zero output; zero authority calls |
| `AOT6213` | conflicting `Split-Path` set | second conflicting selector/path | one error; zero output; zero authority calls |
| `AOT6214` | NUL, unrepresentable text, parent-of-root | offending value | one error; zero output; zero authority calls |

Both adapters materialize all values in binding order before emitting anything.
The first invalid value ends the command with the exact diagnostic. Use
`CommandInvocation.GetValueSpan`; command-span fallback is only for truly
missing values.

## Metadata, discovery, help, and output

`GeneratedCmdletPorts.JoinPath` / `.SplitPath` remain metadata authority. The
implementation must add the exact narrow `SequentialStatic` descriptor factory
described above because generic `CreateAotDescriptor` cannot represent grouped
remaining arguments or a closed selector table. It may encode only these two
profiles and cannot become a general second binder.

The two adapters join the sole `AotCmdletRegistry` and native availability set
together. Existing registry equality, help, `Get-Command`, completion, and
module/catalog views then share the same availability. `Join-Path` uses prose
`TextRecord`; `Split-Path` uses `TextRecord` for Parent/Leaf/LeafBase/Extension
and `BooleanRecord` for `-IsAbsolute`. `PipelineValueAdapter` is the sole
projection to `AotValue`: those records become `{ Value: String }` and
`{ Value: Boolean }`. No adapter returns `AotValue` directly, and no `PathInfo`,
filesystem metadata, type-data lookup, or display system is added.

## Verified coding order

1. Create in-progress notes/timing rows and complete source-pinned reuse
   matrices from `CombinePathCommand.cs` / `ParsePathCommand.cs`.
2. Add parser fixtures/baselines for grouped positional values, aliases,
   selector sets, rejected parameters, and exact spans.
3. Implement the pure lexical seam with dialect injection and authority
   tripwires; prove truth tables without authority calls.
4. Add `AotCommandValueGroupPlan` / `CommandSyntaxValueGroup` and the one
   `SequentialStatic` registry-binder branch; prove all pre-J1 binder fixtures
   and non-J1 command results byte-for-byte unchanged.
5. Add only two adapters, shared catalog/registry declarations, and discovery.
6. Add managed tests: 20 child fragments, two mixed-invalid atomic batches,
   five property rejections, selector conflicts, aliases, output type,
   `Join-Path | Split-Path -Leaf`, `Join-Path a,b c`,
   `Join-Path -Path a,b -ChildPath c`, grouped `ChildPath`, and zero-authority
   assertions. Mechanically check the fixture count.
7. Run a controlled stock `pwsh` oracle over the admitted non-existent-path
   subset, recording only documented normalization/variance.
8. Run parser-reuse/differential gates, managed build/self-test, fresh Native
   AOT publish/self-test, and focused published-native smoke tests.
9. Dispatch mandatory reviewer-matrix and implementation DLAR gates; a BLOCK
   narrows/fixes the slice and prevents merge.
10. Record timing/variances/evidence, non-fast-forward merge, and push.

## Immutable evidence

| Artifact | Immutable source | SHA-256 |
| --- | --- | --- |
| accepted J1 v11 plan | [plan](../../../pwsh-aot-conversion-survey/analysis/j1-conversion-plan-v11/plan.json) | `cdc099ae571f8995a16bbf8ec5f141d0429dca22618cc8b4b787e168629b2af1` |
| `Join-Path` handoff | [handoff](../../../pwsh-aot-conversion-survey/runs/j1-conversion-run-v1/items/08-join-path/compiler-artifacts/JoinPath.handoff.json) | `321d098e24ba56b1f41ffa295e9cc6702460742968714787da33e8a1518970fe` |
| `Split-Path` handoff | [handoff](../../../pwsh-aot-conversion-survey/runs/j1-conversion-run-v1/items/12-split-path/compiler-artifacts/SplitPath.handoff.json) | `3a358e19844856b1f41ffa295e9cc6702460742968714787da33e8a1518970fe` |
| J1 remainder requirements | [requirements](../../../pwsh-aot-conversion-survey/runs/j1-conversion-run-v1/shared/requirements/J1Requirements.json) | `d01eb92cdf613be418ccfd35a35495abd606c2f3a732437ce633e38e51fa8b09` |

The local compiler plan is commit `00e530f`; it is generation evidence, not
an implementation claim. The [accepted profile design](j1-direct-lexical-path-profiles.md)
remains in force; this packet resolves target-readiness blockers only.
