# Structured value plane

The AOT runtime needs a shared data shape for pipelines such as:

```powershell
Get-Process | Where-Object CPU -gt 10 | Select-Object Name, Id, CPU
```

It must preserve PowerShell syntax and the useful structured-data behavior of
PowerShell/Nushell without making the AOT host depend on `PSObject`, CLR
reflection, or arbitrary runtime object adaptation.

## Boundary

[`AotValue.cs`](../../AotValue.cs) introduces a deliberately closed value
model:

| Value | Representation |
| --- | --- |
| absence | `AotValue.Null` |
| logical | `Boolean` |
| number | `Integer`, `Decimal`, or finite `FloatingPoint` |
| scalar text/time | `String` or `DateTime` |
| binary | copied, read-only `Bytes` |
| sequence | copied, read-only `List` |
| structured row | immutable `AotRecord` |

`AotRecord` retains field order for presentation and uses a case-insensitive
field lookup. Duplicate fields that differ only by case are rejected. It is the
only generic property bag; `AotValue.TryGetProperty` resolves a named field
only when the value is a record. There is no fallback to CLR properties.

The model also supplies only the shared helpers required by the next phase:

- `AotRecord.Project` and `WithField` for projection/shape changes;
- `AotValue.TryGetProperty` for explicit property access; and
- `AotValueComparison.TryCompare` for finite numeric, Boolean, string, date,
  and null ordering.

These helpers are intentionally explicit. They do **not** claim PowerShell's
full extended type system or coercion semantics. The AST lowerer/predicate
implementation must define and test any additional compatibility behavior
before adding it here.

## Current migration slice (reviewed, limited)

The first executable slice keeps each cmdlet's business logic and its typed
`IPipelineRecord` output unchanged. `PipelineValueAdapter` is the sole,
explicit type-switch adapter from those records to `AotRecord`; an unknown
record type fails closed instead of falling back to CLR members, `object`, or
reflection.

For the narrow upstream-AST-backed pipeline subset, a direct finite
`Where-Object <property> <comparison> <number>` or direct
`Select-Object <property>[, <property>...]` causes the rows to cross that
adapter boundary. `Where-Object` resolves the named property and compares
`AotValue`s; `Select-Object` produces an immutable `AotRecord.Project` result.
`AotPipelineRecord` is only a compatibility shell for the existing table
writer, and renders fields from the resulting `AotRecord`; it does not expose
the source CLR record to later pipeline work.

This is a reviewed, deliberately limited supported slice—not a claim of
general PowerShell object-pipeline compatibility. The Native AOT Boundary
Sentinel, Static Data-Plane & Binder Guardian, and Compatibility Proof
Adversary verdicts are recorded in the
[value-plane migration review ledger](../reviews/2026-09-21-value-plane-first-migration.md).
The focused evidence and remaining limits are recorded in
[the migration note](value-plane-first-migration.md).

## Non-goals

This initial migration does not:

- replace `IPipelineRecord` as cmdlet-internal output or migrate cmdlet
  business logic into generic maps;
- load arbitrary .NET objects, invoke getters, use reflection, or use
  `PSObject`;
- implement PowerShell conversion, member enumeration, formatting, providers,
  serialization, or dynamic script blocks; or
- make `Sort-Object`, script-block predicates, or PowerShell's full
  `Where-Object`/`Select-Object` conversion and member-enumeration semantics
  executable.

The upstream language frontend and narrow AST lowerer remain the sole syntax
authority. The executable generic slice is intentionally limited to direct
finite-numeric predicates and direct projections of fields explicitly supplied
by the actual `AotRecord`. Field lookup is case-insensitive; a missing field
raises a stable `ScriptException`. A projection cannot request the same field
twice with different casing (`id, ID`): that would violate the immutable
case-insensitive record shape, so the runner rejects it with a stable
`ScriptException` rather than silently choosing or renaming a field. Existing
typed cmdlet logic adapts at a single boundary rather than being rewritten into
untyped maps.

## Focused test plan

`SelfTest` in `Pipeline.cs` covers the first adapter migration with these
assertions:

1. `AotRecord` lookup resolves `Name`, `name`, and `NAME` to the same value and
   rejects duplicate case-insensitive field names.
2. Mutation of the source byte array or source list after construction cannot
   alter an `AotValue`.
3. `Project` retains caller-selected field order; missing fields fail clearly;
   `WithField` replaces without changing the original record.
4. Numeric comparison works across integer/decimal/floating-point finite
   values; unsupported comparisons (record/list/bytes) return `false` rather
   than performing dynamic conversion.
5. Property lookup works for records and returns `false` for every other value
   kind; it must never reflect over a wrapped CLR object.
6. Execute a fixture-backed `Get-Process` pipeline through the same plan used
   by the runner, asserting that the predicate and projection leave an
   `AotPipelineRecord` containing only the projected fields.

Native AOT publish/run remains required before this slice may be described as
supported; managed release build and `--self-test` only establish local
regression evidence.
