# Terminal diagnostic presentation

The host owns terminal presentation; `AotDiagnosticRenderer` owns no terminal
probing. This keeps one structured diagnostic usable by CLI, tests, JSON, and
future editor projections.

`PwshAotLite --color auto|always|never -Command <script>` controls ANSI only
for diagnostics written to `stderr`. Table output and completion output remain
plain data. `auto` is the default: it enables ANSI only for an unredirected
non-dumb terminal, respects `NO_COLOR`, and is conservative on Windows unless
an ANSI-capable host marker is present. `always` is the deterministic test/user
override; `never` preserves plain output.

Capability probing is advisory and catches host-console failures, falling back
to plain output and no width constraint. No P/Invoke, package, or terminal
library is used, keeping the policy Native-AOT portable.

The renderer sanitizes control characters in source-derived fields before
writing. Consequently, the only ANSI control sequences it can emit are its own
balanced styling sequences. Interactive syntax highlighting is intentionally
not part of this layer: upstream token kinds/extents already provide its data
plane, but `Console.ReadLine` cannot safely color an editable buffer. A future
raw-key editor must project the shared parser result and must not create a
second lexer or execute input to color it.

The [conditional execution and terminal-presentation review ledger](../reviews/2026-09-22-if-control-flow-terminal-presentation.md)
records the terminal-policy, sanitizer, parser, and Native AOT evidence for
this admission.
