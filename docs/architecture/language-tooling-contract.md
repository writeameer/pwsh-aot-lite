# Shared language-tooling contract

## Decision

The pinned upstream PowerShell parser is one shared language service, not an
implementation detail of command execution. Its stable parse result supports
the AOT execution kernel, interactive CLI tooling, and eventual editor/LSP
features while keeping all three faithful to PowerShell syntax.

```text
source text
  └─ upstream parser facade
       ├─ structural AST + parse diagnostics ─► feature policy / lowerer
       ├─ tokens + source extents ────────────► syntax highlighting
       └─ AST, tokens, extents, diagnostics ─► completion, hover, navigation
```

The execution lowerer decides whether a valid AST node is supported by the
AOT runtime. It must return a stable unsupported-execution diagnostic when it
is not. It may never make parser visibility conditional on executability.

## Facade contract

The production parser facade must expose, without executing source:

- structural AST access;
- tokens with upstream kind, text, and source extent;
- parse diagnostics with stable upstream identity and source extent; and
- source-document identity/version supplied by the caller, so an editor may
  associate an asynchronous result with the document it parsed.

The facade must accept incomplete and malformed documents. Those results are
for diagnostics and highlighting only; a consumer must not attempt lowering or
execution unless parsing and the execution feature policy both allow it.

The contract is intentionally language-only. It does **not** load modules,
resolve runtime types, enumerate providers, invoke argument completers,
evaluate expressions, execute script blocks, or inspect extension assemblies.
Those actions belong to separately designed runtime/trust boundaries.

## Consumer boundaries

| Consumer | May use | Must not do |
| --- | --- | --- |
| AOT execution kernel | AST, diagnostics, extents | Own parsing, reinterpret syntax as words, execute unsupported nodes |
| CLI editor/highlighter | Tokens, diagnostics, extents; later catalog-backed completion | Execute input merely to colorize or complete it |
| Editor/LSP | AST, tokens, diagnostics, source version; later static symbols/catalog metadata | Depend on a live runspace or execute source for basic language features |
| Metadata completion | Existing data-only catalog plus a deliberately narrow incomplete-line scanner | Claim parser authority or grow into a second executable grammar |

`CompletionInput` is currently the last row's narrow scanner. It remains a
temporary metadata client. It is not an alternative to the upstream parser
facade and must not acquire executable syntax semantics.

## Roadmap placement

1. **AOT Execution Kernel:** define and test the parser facade's AST/token/
   extent/diagnostic contract. The lowerer consumes it directly.
2. **Language Compatibility Core:** expand supported lowering and add static
   symbol analysis for variables, functions, and parameters.
3. **Engine Runtime Core:** make the contract useful in the REPL through
   highlighting and catalog-backed completion; add command/module discovery to
   static language analysis.
4. **Built-in and provider coverage:** improve completion and help as runtime
   contracts mature, without changing the language boundary.
5. **Ecosystem compatibility:** deliver editor/LSP integration and
   extension-aware completion under the extension trust policy.

## Required evidence

For every parser-facade or tooling change:

1. Run the existing parser differential harness and preserve token text/kind,
   AST structure/extents, and diagnostic IDs for adopted syntax.
2. Test a valid document and a malformed or incomplete document; prove no
   source execution, import, or evaluation occurs in either tooling path.
3. Test source extents against the submitted document and, once document
   versions are implemented, prevent stale results from being applied.
4. Keep execution support claims separate from parser support claims. A parsed
   feature can be highlighted and diagnosed before the AOT lowerer supports it.
5. Obtain the Upstream Grammar Steward and Language Tooling Contract Guardian
   verdicts; involve the Native AOT Boundary Sentinel if executable references
   or dependencies change.
