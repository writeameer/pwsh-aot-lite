# Contributing

This repository is a Native AOT migration spike, not a drop-in replacement for
PowerShell. Read [AGENTS.md](AGENTS.md), [ARCHITECTURE.md](ARCHITECTURE.md),
and [PARSER-REUSE-GUARD.md](PARSER-REUSE-GUARD.md) before proposing a port.

## Local verification

```powershell
pwsh -NoProfile -File eng/Restore-Upstream.ps1
dotnet build -c Release
dotnet run -c Release -- --self-test
pwsh -NoProfile -File tools/Test-ParserReuseGuard.ps1
pwsh -NoProfile -File tools/Export-PwshParserBaseline.ps1 -Verify
```

Do not commit `.upstream/`, `bin/`, `obj/`, or native publish artifacts. Keep
new command behavior narrowly scoped, document every variance, and obtain the
independent reviews required by `AGENTS.md` before widening a support claim.
