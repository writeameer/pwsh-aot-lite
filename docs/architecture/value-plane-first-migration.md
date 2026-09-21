# First generic value-plane migration

## Scope

This reviewed first migration connects the existing narrow `Where-Object` and
`Select-Object` execution stages to the closed `AotValue`/`AotRecord` model.
It does not alter PowerShell grammar, the upstream AST lowerer, generated
cmdlet metadata, or the generated-metadata binder.

```text
typed cmdlet IPipelineRecord output
          -> explicit PipelineValueAdapter
          -> AotValue.Record / immutable AotRecord
          -> direct finite-numeric Where-Object predicate
          -> direct Select-Object AotRecord.Project
          -> AotPipelineRecord compatibility renderer
```

`PipelineValueAdapter` uses a closed type switch for every current port output.
There is intentionally no default property reflection, `object` value, or
`PSObject` bridge. A newly ported record must add an explicit conversion case
before it can pass through a generic pipeline stage.

## Preserved design

- Cmdlets still implement and test their normal typed record logic.
- The upstream PowerShell parser and AST lowerer remain the only executable
  syntax path.
- `AotCmdletRegistry` and generated descriptors remain the only command
  binder; no data-plane binder was introduced.
- The existing table writer continues to consume `IPipelineRecord`, but for a
  generic-stage result it reads only fields from the projected `AotRecord`.

## Deliberate limits

- Only the pre-existing direct three-atom finite-numeric `Where-Object`
  predicate is executable; `NaN` and infinity are rejected with a stable
  `ScriptException`. Script blocks, conversion, member enumeration, and full
  ETS semantics remain excluded.
- Only direct `Select-Object` projection of fields explicitly supplied by the
  current `AotRecord` is executable. It uses the record's case-insensitive
  lookup rather than a global field-name whitelist; a missing field gets a
  stable `ScriptException` at projection. Repeating a selected field with
  different casing (for example `id, ID`) is also rejected with a stable
  `ScriptException`, because an immutable case-insensitive `AotRecord` cannot
  represent that ambiguous duplicate shape.
- `IPipelineRecord` remains a compatibility and cmdlet-internal type; this is
  not a repository-wide mechanical record rewrite.
- `Sort-Object`, lists/records/bytes table rendering, and an extension output
  contract remain future designed work.

## Evidence and review

The implementation has passing managed build and self-test coverage for value
immutability, case-insensitive records, comparison behavior, and a
fixture-backed process filter/projection. The parser-reuse guard and four
stock-`pwsh` parser baselines also pass. A fresh `osx-arm64` Native AOT publish
ran `--self-test` and:

```powershell
./artifacts/osx-arm64/PwshAotLite -Command 'Get-Process -Name PwshAotLite | Where-Object CPU -ge 0 | Select-Object Name, Id'
```

The native command produced `PwshAotLite` rows with `Name` and `Id` only. A
second native smoke used lower-case `baseutcoffsetminutes` and projected
lower-case `id, baseutcoffsetminutes` from `Get-TimeZone -ListAvailable`,
proving record-authoritative case-insensitive adapter fields. Native negative
smokes rejected a `NaN` predicate, a missing projection field, and a
case-insensitive duplicate projection with stable `ScriptException`s. The
publish completed with the pre-existing Homebrew OpenSSL/Brotli minimum-macOS
linker warnings. The independent review evidence is recorded in the
[value-plane migration review ledger](../reviews/2026-09-21-value-plane-first-migration.md).
