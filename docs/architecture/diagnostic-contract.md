# Diagnostic-experience contract

## Decision

Diagnostic quality is a Phase-1 responsibility of the **AOT Execution Kernel**.
A capability is not considered supported until it either completes successfully
or fails through the shared diagnostic path with clear, source-precise,
actionable output. This prevents a growing collection of cmdlet-specific error
strings and leaked implementation exceptions.

```text
parse / feature policy / lower / bind / pipeline / cmdlet runtime
                              │
                              ▼
                       AotDiagnostic
     id · severity · category · primary message · source extent
                    labels · notes · help · inner detail
                              │
                  ┌───────────┼────────────┐
                  ▼           ▼            ▼
            terminal renderer JSON      editor/LSP
            (plain or ANSI) projection  projection
```

`AotDiagnostic` is the only public failure contract. Exceptions are an
implementation mechanism: the host boundary translates unexpected exceptions
to a stable internal diagnostic and exposes technical detail only through an
explicit diagnostic/debug mode.

## Required behavior

Every public diagnostic has:

- a stable, documented identifier (for example `AOT1001`), never a message
  string used as an identifier;
- severity and category that distinguish parsing, unsupported execution,
  binding, runtime, and internal-host failures;
- a concise primary message that says what happened;
- a source extent and labelled underline whenever the failure arose from source
  text;
- an actionable `help` or `note` when a safe alternative exists; and
- structured data from which terminal, JSON, and editor clients can render
  without reparsing human text.

The terminal renderer targets the useful properties of Rust-style errors:
stable IDs, a source location, a focused caret/underline label, and an
actionable help line. It must support both ANSI and plain-text output and
respect a caller-selected width without losing the diagnostic ID or primary
message.

```text
error[AOT1001]: script-block predicates are not supported yet
  ┌─ input.ps1:1:15
  │
1 │ Get-Process | Where-Object { $_.CPU -gt 10 }
  │               ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ unsupported execution feature
  │
  = help: use the supported direct-property form: Where-Object CPU -gt 10
```

The example is an output shape, not a claim that the cited command is already
supported.

## Layer ownership

| Layer | Owns | Must not do |
| --- | --- | --- |
| Upstream parser adapter | Preserve upstream diagnostic identity/text/extent | Reword a parse failure into an execution failure |
| Feature policy and lowerer | Stable AOT unsupported-feature diagnostics | Treat valid-but-unsupported syntax as command words or skip it |
| Binder | Parameter, argument-shape, and parameter-set diagnostics | Accept an unsupported parameter and ignore it |
| Pipeline/cmdlet runtime | Typed terminating and non-terminating runtime diagnostics | Write user-visible error strings directly |
| Host renderer | Terminal/JSON/LSP projections and unexpected-exception containment | Make output text the source of diagnostic truth |

## Phase placement

1. **AOT Execution Kernel:** implement `AotDiagnostic`, source extents/labels,
   upstream parser adaptation, lowerer/binder errors, caller-selected
   plain/ANSI/width-constrained terminal rendering, and snapshot fixtures for
   successful, malformed, unsupported, binding, and runtime cases.
2. **Language Compatibility Core:** add expression/conversion/scope context,
   nested labels and causal notes.
3. **Engine Runtime Core:** model PowerShell-compatible terminating versus
   non-terminating pipeline errors, invocation context, streams, and
   REPL-friendly presentation.
4. **Built-in/provider coverage:** migrate each port to shared diagnostics and
   reject old command-specific formatting during review.
5. **Ecosystem compatibility:** project the same typed records to JSON and
   editor/LSP diagnostics, then consider safe code actions.

## Required tests and review gate

For every newly supported or explicitly unsupported behavior:

1. Add a stable diagnostic ID to the documented registry.
2. Test the structured fields and a terminal snapshot (plain text; ANSI where
   styling is material).
3. Assert the source extent/label for source-derived failures.
4. Exercise malformed, unsupported, binding, and runtime-negative paths as
   applicable.
5. Verify the host does not expose raw exceptions in ordinary output.
6. Obtain the **Diagnostic Experience Guardian** verdict, plus the applicable
   grammar, data-plane/binder, and AOT-boundary reviewers.

An unsupported feature with a precise `AOT` diagnostic is an intentional,
useful result. A vague exception, silent fallback, or unstructured error string
is not.
