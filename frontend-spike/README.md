# Upstream-derived PowerShell front-end proof

This is an isolated, Native-AOT-safe lexer/parser proof. It parses the shape of
a basic PowerShell command pipeline into data; it does not execute it.

Run:

    dotnet run --project frontend-spike -- --self-test
    dotnet run --project frontend-spike -- "Get-Process -Name 'pwsh*' | Where-Object CPU -gt 10 | Select-Object Name, Id"
    dotnet publish frontend-spike -c Release -r osx-arm64 --self-contained true

The exact upstream origin, adaptation decisions, and blockers to importing the
full AST are in [UPSTREAM-ORIGIN.md](UPSTREAM-ORIGIN.md).
