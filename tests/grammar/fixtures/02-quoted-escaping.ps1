Get-Process -Name 'pwsh''aot' | Select-Object "Name", @{ Name = 'Path'; Expression = { $_.Path } }
