# Cmdlet porting recipe

Before selecting another built-in cmdlet, consult the checked
[Phase 10 campaign manifest](docs/campaign/phase10-built-in-cmdlets.md). It
classifies every source declaration, records its prerequisite wave, and is
verified against the generated source contract. Classification is planning
evidence, not authorization to claim execution compatibility.

This is a mechanical path for moving a cmdlet into the AOT runner without
bringing `System.Management.Automation` along.

Before using this recipe, complete the mandatory
[upstream reuse and format-contract evidence matrix](docs/architecture/upstream-reuse-governance.md).
The matrix is the gate against clean-room duplication: source behavior, base
classes, output fields, and default display must be copied, adapted, extracted
as static data, replaced by a reviewed shared substrate, or explicitly
deferred. A convenient new projection is not an acceptable implicit choice.

`PwshAotPortGenerator` now provides the first mechanical step: it reads the
original cmdlet attributes at build time and emits `GeneratedCmdletPorts.g.cs`.
The `Get-Process` port uses that generated metadata directly.

| Existing PowerShell concept | AOT replacement |
| --- | --- |
| `Cmdlet` subclass | `IAotCmdlet` implementation |
| `[Cmdlet]` / `[Parameter]` attributes | static `CmdletDescriptor` / `ParameterSpec` |
| program source syntax entry | `UpstreamAstPipelineLowerer.Parse` → pinned upstream AST |
| focused direct-command fixture helper | `AotCmdletRegistry.ParseSource` → upstream AST lowerer |
| parameter binder | `AotCmdletRegistry.BindCommand` over generated metadata |
| `WriteObject` | `IEnumerable<IPipelineRecord>` |
| `WriteError` | `AotExecutionContext.WriteNonTerminatingError` |
| runtime discovery of command types | static `Cmdlets` registry |

For each cmdlet:

1. Complete the per-cmdlet upstream-reuse matrix with pinned source/type-data
   locations before implementing. Record why each non-copied item cannot cross
   the AOT boundary and search existing target services before proposing one.
2. Classify its code into **business logic** versus calls to the existing engine.
3. Create one `IAotCmdlet` adapter with generated static metadata for the supported parameters.
4. Move its business logic behind explicit interfaces for operating-system or host
   services (as `IProcessCatalog` does here).
5. Translate engine output/errors to `IPipelineRecord` and `AotExecutionContext`.
   For every output field and default column, cite its upstream producer and
   format/type-data contract; static compilation of the source view is preferred
   to an invented table. Compare normal `pwsh` and the Native AOT binary on the
   same controlled fixture, retaining raw/snapshot output and documenting only
   permitted normalization; any mismatch is a named variance or a deferral.
6. Add fixture tests for command behavior, then verify an AOT Docker publish.
7. Obtain an explicit upstream-reuse/format-contract PASS in the review ledger;
   resolve every BLOCK before merging.

## Proven example: `Get-Process`

Source material:

- `src/Microsoft.PowerShell.Commands.Management/commands/management/Process.cs`
- `ProcessBaseCommand.MatchingProcesses` supplies the selection algorithm.
- `GetProcessCommand` supplies the `Name` and `Id` parameter shape.

Ported behavior in `Pipeline.cs`:

- `GetProcessCmdlet`: static parameter declaration and parameter-set check.
- `ProcessSelector`: `All`, `ByName`, and `ById` selection; duplicate removal;
  name/ID ordering; and non-terminating missing-process errors.
- `SystemProcessCatalog`: the platform boundary.

Current deliberate scope includes `*` and `?` wildcard matching, normal process
output, `Name` / `Id`, and the reviewed `-Module`, `-FileVersionInfo`, and
`-IncludeUserName` adapters. Rich PowerShell wildcard syntax, providers,
remoting, and source-level command-to-command pipelines remain deferred.
