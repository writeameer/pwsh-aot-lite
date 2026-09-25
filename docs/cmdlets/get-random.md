# Get-Random port notes

## Status and admitted surface

Release-pending Native AOT slice. The adapter admits exactly one direct,
named, seeded Int32 form:

```powershell
Get-Random -SetSeed <Int32> -Minimum <Int32> -Maximum <Int32>
```

All three parameters are required, `Minimum` must be less than `Maximum`, and
the command emits one invariant-culture integer line. It is not a general
random-object or pipeline adapter.

- Upstream command: `src/Microsoft.PowerShell.Commands.Utility/commands/utility/GetRandomCommand.cs`
- Shared upstream base: `GetRandomCommandBase.cs`
- Generated metadata: `GeneratedCmdletPorts.GetRandom`
- Target adapter: `Pipeline.cs`: `GetRandomCmdlet`

## Reuse and boundary

The target retains the exact seeded algorithm from upstream's
`PolymorphicRandomNumberGenerator`: `Random(seed).NextBytes`, `InternalSample`,
`NextDouble`, and bounded `Next(minimum, maximum)`. It does not substitute
`Random.Next(minimum, maximum)`, whose sequence differs from PowerShell.

Unseeded cryptographic mode, `Get-SecureRandom`, omitted bounds, positional
arguments, `InputObject`, `Count`, `Shuffle`, pipeline input, and non-Int32
numeric modes are outside the descriptor and fail closed. No runspace,
`PSObject`, reflection, provider, dynamic binder, or runtime compilation was
added.

## Verification

```powershell
dotnet build -c Release -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src
dotnet run -c Release --no-build -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src -- -Command 'Get-Random -SetSeed 7 -Minimum 0 -Maximum 10'
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src -o ./artifacts/osx-arm64-random
./artifacts/osx-arm64-random/PwshAotLite --self-test
./artifacts/osx-arm64-random/PwshAotLite -Command 'Get-Random -SetSeed 7 -Minimum 0 -Maximum 10'
./artifacts/osx-arm64-random/PwshAotLite -Command 'Get-Random -SetSeed 42 -Minimum -10 -Maximum 10'
```

Managed and native outputs matched stock PowerShell: `9` and `-7` for those
two forms. Invalid reversed bounds produce source-spanned `AOT6702`; `-Count`
is rejected by generated binding as `AOT2002`.
