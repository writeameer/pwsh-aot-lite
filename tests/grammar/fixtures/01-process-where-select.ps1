Get-Process | Where-Object CPU -gt 10 | Select-Object Name, Id, CPU
