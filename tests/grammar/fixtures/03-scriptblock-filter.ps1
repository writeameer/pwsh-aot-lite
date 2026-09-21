Get-Process | Where-Object { $_.CPU -gt 10 -and $_.ProcessName -like 'pwsh*' } | Select-Object -Property Name, Id
