# Parser reuse guard

## Decision

The PowerShell language front end is upstream source, not a new feature for
this spike to reinvent. Production parsing must use a pinned, attributed,
upstream-derived extraction of the PowerShell handwritten tokenizer, parser,
and structural AST. This preserves PowerShell syntax while allowing the dynamic
compiler and runtime engine to be replaced.

`frontend-spike/` is an **archived proof only**. It demonstrates Native AOT
deployment of a constrained parser but is not PowerShell-compatible enough to
ship and must not be expanded or referenced by the host. The parent project
explicitly excludes its source files.

## Required production path

```text
pinned upstream tokenizer/parser/AST
             ↓
       AOT feature-policy pass
             ↓
        AST lowerer (supported subset)
             ↓
existing generated metadata + AotCmdletRegistry
             ↓
 typed IPipelineRecord cmdlets
             ↓
 explicit AotValue/AotRecord adapter for reviewed generic pipeline stages
```

`AotValue`/`AotRecord` is the closed generic data boundary for the currently
implemented direct `Where-Object`/`Select-Object` slice. Cmdlet implementations
remain strongly typed and cross it only through explicit adapters. The slice
is deliberately limited; see
[`docs/reviews/2026-09-21-value-plane-first-migration.md`](docs/reviews/2026-09-21-value-plane-first-migration.md).

The language front end may parse constructs that the AOT evaluator does not yet
execute. In that case the lowerer must report a stable `UnsupportedSyntax` or
`UnsupportedExecutionFeature` error. It must never reinterpret syntax as words,
silently skip it, or invent a no-op.

## Gates for an upstream extraction

1. Record the upstream PowerShell commit, every copied source file, retained
   Microsoft copyright/MIT headers, and every delta in `UPSTREAM.md`.
2. Reuse the language closure as far as possible: `CharTraits`, `Position`,
   `token`, `tokenizer`, `Parser`, `ast`, `AstVisitor`, `PreOrderVisitor`, and
   narrow `VariablePath` support. Deltas must remove engine hooks; they must
   not reimplement grammar rules.
3. Exclude `Compiler.cs`, `PSType.cs`, reflection/type resolution, `PSObject`,
   runspaces, module loading, Dynamic Keyword/DSC execution, expression
   compilation, `System.Reflection.Emit`, and runtime code generation.
4. Put parse-time environment-sensitive features (`#requires`, `using
   assembly`, DSC configuration) behind declarative feature-policy diagnostics;
   parsing must not load, import, execute, or probe ambient modules.
5. Differentially test every adopted syntax slice against stock `pwsh` using
   the upstream parser corpus. Check token kind/text/extents, AST shape, and
   diagnostic IDs—not merely successful execution. Start with
   `test/powershell/Language/Parser/Parsing.Tests.ps1`, `Parser.Tests.ps1`,
   and `Ast.Tests.ps1`.
   The checked-in `tests/grammar/fixtures` and `tests/grammar/baselines` are
   the initial executable oracle; run
   `pwsh -NoProfile -File tools/Export-PwshParserBaseline.ps1 -Verify` before
   claiming any extracted syntax slice is compatible.
6. Do not create a second cmdlet binder. AST lowering must delegate aliases,
   parameter sets, validation metadata, help, and availability to the existing
   generated `SourceCmdletMetadata`, `CmdletDescriptor`, and
   `AotCmdletRegistry`.
7. Keep cmdlet implementations strongly typed. Only explicit adapters may
   convert their output to the closed `AotValue` model; no `object` fallback,
   property reflection, or `PSObject` wrapper is permitted.
8. Require managed build, Native AOT publish, and a native parse/lower smoke
   test before declaring an extracted grammar slice usable.

## Review checklist

Every parser/evaluator change must answer these questions in its design note
and code review:

- Which exact upstream file and commit supplied this behavior?
- Is the change an engine-hook removal or a new grammar implementation?
- Is there a differential token/AST/diagnostic test against stock `pwsh`?
- Does the change introduce any dynamic execution or reflection reachability?
- Does the lowerer give an explicit unsupported-feature error where behavior is
  not implemented?

Any missing answer blocks the change.
