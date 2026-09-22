Get-TimeZone -ListAvailable |
    Where-Object BaseUtcOffsetMinutes -ge -1000 |
    Select-Object Id |
    Where-Object Id -ne 0 |
    Select-Object Id |
    Where-Object Id -ne 0
