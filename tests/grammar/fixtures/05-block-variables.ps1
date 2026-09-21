$threshold = 10
$names = 'pwsh', 'dotnet'
Get-Process -Name $names | Where-Object CPU -gt $threshold | Select-Object Name, Id
