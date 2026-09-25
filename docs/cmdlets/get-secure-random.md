# Get-SecureRandom port notes

## Admitted surface

Release-pending Native AOT slice:

```powershell
Get-SecureRandom -Minimum <Int32> -Maximum <Int32>
```

Both parameters are named and required; `Minimum` is strictly less than
`Maximum`; the adapter writes one integer in `[Minimum, Maximum)`.

- Upstream: `GetSecureRandomCommand.cs` and `GetRandomCommandBase.cs`
- Generated contract: `GeneratedCmdletPorts.GetSecureRandom`
- Target: `Pipeline.cs`: `GetSecureRandomCmdlet`

## Reuse and boundary

The adapter reuses the upstream cryptographic bounded-Int32 helper shape:
`RandomNumberGenerator.Create`, `NextBytes`, `InternalSample`, `NextDouble`,
and bounded `Next`. It intentionally excludes the source base's runspace
generator map/session lifetime, plus default/unbounded mode, `SetSeed`,
object/list input, `Count`, `Shuffle`, pipeline, positional binding, Int64,
and floating-point paths. Those forms fail closed.

## Verification

```powershell
dotnet build -c Release -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PowerShellSourceRoot=/Users/ameerdeen/progs/PowerShell/src -o ./artifacts/osx-arm64-secure-random
./artifacts/osx-arm64-secure-random/PwshAotLite --self-test
./artifacts/osx-arm64-secure-random/PwshAotLite -Command 'Get-SecureRandom -Minimum 0 -Maximum 10'
```

Native self-test passed; the native result was range-checked. Reversed bounds
fail with `AOT6712`; `-SetSeed` is rejected by generated binding (`AOT2002`).
