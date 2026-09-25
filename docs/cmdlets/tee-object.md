# Tee-Object boundary note

`TeeObjectCommand` accepts pipeline `PSObject` then delegates its two target
modes to `Out-File` (`FilePath`/`LiteralPath`/`Encoding`) and `Set-Variable`
(`Variable`). The current AOT host has only read-only physical-file resolution
and deliberately has no session-variable or general write authority.

No adapter is registered. A direct file write would be an unsafe replacement:
it would bypass provider/path, encoding, append/overwrite, error, and host
authorization contracts; a variable route would recreate session state. Revisit
only after a reviewed explicit write-capability and session-state policy.
