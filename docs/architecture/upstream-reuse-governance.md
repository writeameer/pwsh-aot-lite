# Upstream reuse and format-contract governance

This is the anti-NIH gate for every cmdlet port. A Native AOT boundary does
not authorize a clean-room implementation merely because upstream code uses an
engine service. The port owner must first prove what can be retained, then
record the smallest justified replacement for what cannot.

This policy applies to command behavior, parameter metadata, base-class/lifecycle
behavior, emitted record fields, default table columns, and formatter behavior.
It is an evidence gate, not an aspiration.

## Required upstream-reuse evidence matrix

Before implementation, add an **Upstream reuse evidence matrix** to the
per-cmdlet note. Complete one row for each meaningful behavior or output
surface; a blank row is not evidence. Rows must include at least:

1. command declaration, parameters, aliases, parameter sets, and validation;
2. base-class and lifecycle behavior used by the command;
3. each source method/helper containing business logic that is in scope;
4. each engine/service call that prevents direct reuse;
5. every field emitted by the AOT record; and
6. every default display column and renderer rule exposed by the command.

Use this exact table shape:

| Surface | Exact upstream evidence | Reuse decision | AOT target / contract | Why direct copy is or is not possible | Variance ID | Verification |
| --- | --- | --- | --- | --- | --- | --- |
| `<behavior or field>` | `<pinned path:line(s), type/member, and format/type-data source where applicable>` | `<copied / adapted / static-data extracted / shared-substrate replacement / deferred>` | `<target type/member or explicit unsupported diagnostic>` | `<specific engine or AOT reason; “simpler” is invalid>` | `<port-variances.json id>` | `<test/native evidence>` |

`Exact upstream evidence` is mandatory. For output fields and default columns it
must cite both the producer (cmdlet/object/member) and the formatter/type-data
source if one exists. If upstream relies on a dynamic formatting subsystem,
record the exact view/type-data rule, then classify the target as either a
static-data extraction or a deliberate deferred display contract. Do not infer
columns from what happened to be convenient to print.

## Permitted decisions

The only valid reuse decisions are:

| Decision | When it is permitted | Required target evidence |
| --- | --- | --- |
| `copied` | Pure upstream code has no excluded dynamic dependency. | Attribution, upstream pinned path, targeted tests. |
| `adapted` | Small mechanical changes isolate an AOT-safe equivalent without changing the behavior claim. | Before/after delta and unchanged contract tests. |
| `static-data extracted` | Metadata, formatting views, or fixed tables can be compiled as data. | Extraction source, generated/static artifact, snapshot. |
| `shared-substrate replacement` | The source dependency crosses the AOT boundary and a reusable finite capability is needed by more than this port. | Narrow interface, capability authority, fixtures, and reason the upstream dependency cannot cross. |
| `deferred` | Exact compatibility needs a dynamic engine feature or has no reviewed static design. | Stable rejection/availability behavior and variance. |

No other label is allowed. In particular, `new implementation`, `minimal
projection`, `temporary`, and `close enough` are not decisions. A command-local
helper replacing a reusable upstream abstraction is prohibited unless the
matrix proves it is intrinsically command-specific.

## Approved AOT replacement exception

A replacement is approved only when all of the following are recorded in the
matrix and the review ledger:

1. the exact upstream dependency and why it cannot enter the AOT binary;
2. the narrowest reusable interface or static-data artifact that preserves the
   in-scope contract;
3. the capability authority/ownership boundary, when host or OS access is
   involved;
4. why an existing target abstraction cannot be reused (with searched paths);
5. a stable explicit deferred diagnostic for unimplemented source behavior;
6. a structured `port-variances.json` entry; and
7. tests proving the retained contract and the rejected boundary.

An exception is not a permanent license to diverge. If a second port needs the
same exception, create or extend a shared substrate or static descriptor before
integrating the second command. The variance entry must mark it as an
automation candidate and link the first use.

## Output and formatting gate

Output is part of command compatibility. A closed AOT record is acceptable,
but its fields and default table must be traceable:

- Every exposed field maps to an upstream property/member, a documented
  computed value, or a deliberately omitted source field.
- Every default column/order/header/format rule maps to upstream format/type
  data or is labelled an explicit temporary display variance.
- A display variance must never silently become the claimed default PowerShell
  presentation. It needs a snapshot/native smoke and a named convergence path
  (for example, static compilation of the upstream view).
- Adding a field because it is convenient is only allowed after documenting it
  as an AOT-specific diagnostic field, not as compatibility output.

### Runtime oracle comparison is mandatory

Source evidence establishes intent; it does not prove that the target presents
the same data. For every admitted output field or default display column, the
port note must contain a repeatable **runtime oracle comparison** between a
normal `pwsh` invocation and the published Native AOT artifact over the same
controlled fixture path/input. The comparison must record:

1. the exact stock `pwsh` command and version;
2. the exact Native AOT command, artifact identity, and target RID;
3. the fixture/input path and its deterministic contents or setup command;
4. the source producer plus format/type-data view that explains the oracle;
5. the normalization used before comparison; and
6. the resulting match, intentional variance ID, or deferred field.

Permitted normalization is limited to facts which are nondeterministic or
environment-rendered: absolute fixture-root substitution, local-time/UTC
conversion when the source view explicitly changes the time zone, host-specific
owner/group identity, terminal width, and current timestamp precision. The
note must show the normalization rule and retain representative raw output or
a checked fixture/snapshot. Normalization may not rename headers, reorder
columns, discard a field, coerce a type to text, or manufacture a missing value
to make a mismatch disappear.

If the target deliberately exposes a closed record rather than the source
object, compare the fields/projection individually and compare its declared
default table separately. A mismatch becomes a named variance with a
convergence/defer decision; it is never silently normalized away. A formatter
that is not yet statically extracted from upstream must be described as an
explicit display variance, even if the values themselves compare equal.

## Review and integration gate

The port cannot be integrated until the review ledger contains an **Upstream
reuse and format-contract verdict**. The designated reviewer may be the
Static Data-Plane & Binder Guardian for behavior-only ports; add an independent
format-contract reviewer whenever emitted fields, default display, or rendering
changes.

That reviewer must issue `BLOCK` when any of these is true:

1. metadata, aliases, parameter sets, or validation were manually recreated
   rather than generated from upstream;
2. a new helper duplicates an existing target service or a reusable upstream
   abstraction without a matrix-backed reason;
3. an engine-dependent source path was rewritten before considering static-data
   extraction or a shared substrate;
4. an emitted field/default column lacks producer and formatting evidence;
5. the target claims a PowerShell-like table while using invented/default
   headers, ordering, or values;
6. a variance lacks a concrete why-not-copy rationale, exception evidence, or
   a fail-closed behavior; or
7. a second command repeats a prior exception instead of reusing or extending
   its reviewed substrate.
8. an admitted output field/default column lacks a repeatable normal-pwsh vs
   Native-AOT oracle comparison, or its normalization hides a semantic/display
   difference.

The smallest correction is always: copy/adapt the identified upstream material,
extract fixed data, reuse an existing service, narrow the supported claim, or
defer the behavior. “AOT needs a rewrite” is not a sufficient resolution.
