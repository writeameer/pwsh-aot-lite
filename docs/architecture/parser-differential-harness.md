# Stock PowerShell parser differential harness

`tools/Export-PwshParserBaseline.ps1` is the fixture oracle for the future
`PwshAotLite.Language` extraction. It runs under **stock `pwsh`**, calls only
the public `System.Management.Automation.Language.Parser`, and never invokes
the fixture. The harness is external tooling, not a dependency of the Native
AOT executable.

For every fixture it produces a stable JSON document containing:

- token kind, text, flags, and full source extent;
- the parser's real AST type hierarchy and each node extent; and
- parse diagnostic ID and extent.

AST structure comes from the public `Ast.Parent` relationship rather than
source-span inference, so equal-span nodes retain their real nesting.

## Fixtures

| Fixture | Purpose |
| --- | --- |
| `01-process-where-select.ps1` | canonical command pipeline and bare `Where-Object` property/operator form |
| `02-quoted-escaping.ps1` | single-quote escape, double-quoted string, hashtable, and nested script block |
| `03-scriptblock-filter.ps1` | predicate script block, member access, logical and comparison operators |
| `04-malformed-pipe.ps1` | parser recovery and stable syntax diagnostic baseline |

## Use

Regenerate deliberate baselines after changing a fixture or explicitly moving
the stock-PowerShell reference version:

```powershell
pwsh -NoProfile -File tools/Export-PwshParserBaseline.ps1 -Write
```

Verify the checked-in oracle before/after changing the extracted parser:

```powershell
pwsh -NoProfile -File tools/Export-PwshParserBaseline.ps1 -Verify
```

`language/PwshAotLite.Language.csproj` now consumes this schema using
source-generated `System.Text.Json` metadata and compares its pinned upstream
extraction to every fixture. A mismatch in token text/extent, AST shape, or
diagnostic ID blocks a grammar-support claim until explained in its
`UPSTREAM.md` and review ledger. Do not respond by weakening or extending the
archived `frontend-spike` parser.

Run the extracted parser comparison from this repository root:

```powershell
dotnet run --project language/PwshAotLite.Language.csproj -c Release
```

For a separately published native binary, provide the source fixture locations
explicitly; they are test inputs rather than files embedded in the executable:

```powershell
./PwshAotLite.Language \
  --fixture-root /absolute/path/to/pwsh-aot-lite/tests/grammar/fixtures \
  --baseline-root /absolute/path/to/pwsh-aot-lite/tests/grammar/baselines
```

The language host additionally runs an AOT-policy-only DSC exclusion/recovery
test. It must remain separate from the stock parser equality suite because its
`AotConfigurationExcluded` result is intentionally not stock PowerShell
behavior.

## Initial result

The initial stock-pwsh **7.6.6** run generated four baselines and verified
them. The malformed pipeline has an `EmptyPipeElement` diagnostic baseline by
design; successful parsing is not its expected result.
