Get-ChildItem .
Get-ChildItem -Path ./docs | Select-Object Name, Type, Length
Get-ChildItem -LiteralPath ./docs
Get-ChildItem -Recurse .
