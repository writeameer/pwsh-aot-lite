Get-TimeZone -ListAvailable |
    Where-Object BaseUtcOffsetMinutes -GE -1000 |
    Select-Object Id, BaseUtcOffsetMinutes

Get-TimeZone -ListAvailable |
    Where-Object -Property BaseUtcOffsetMinutes -GE -Value -1000 |
    Select-Object -Property Id, BaseUtcOffsetMinutes

Get-Process | Where-Object { $_.CPU -GT 10 }
Get-Process | Select-Object -ExcludeProperty Name
Get-TimeZone -Id UTC | Select-Object Id -Property BaseUtcOffsetMinutes
Get-TimeZone -Id UTC | Select-Object -Property Id BaseUtcOffsetMinutes
Get-TimeZone -Id UTC | Select-Object I*
