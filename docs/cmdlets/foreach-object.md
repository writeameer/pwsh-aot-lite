# ForEach-Object port notes

## Admitted surface

```powershell
<TextRecord-producing AOT command> | ForEach-Object -MemberName Length
```

The adapter emits invariant text lengths for closed `TextRecord` inputs.

- Upstream: `src/System.Management.Automation/engine/InternalCommands.cs` (`ForEachObjectCommand`)
- Generated contract: `GeneratedCmdletPorts.ForEachObject`
- Target: `ForEachObjectCmdlet`

## Boundary and verification

The source property-and-method set resolves members dynamically; its default
script-block, parallel, argument-list, PSObject, and method routes are not
admitted. This adapter hard-codes only the static `String.Length` property and
adds no member discovery, reflection, ETS, or runtime compilation.

Managed and fresh osx-arm64 Native AOT self-tests passed. The `abc/x,de/x`
corpus matched stock (`5`, `4`); dynamic method spelling fails with `AOT6733`.
Integrated to `main` at `2911712`.
