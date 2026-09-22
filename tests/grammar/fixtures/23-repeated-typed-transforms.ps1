Get-TimeZone -ListAvailable |
    Where-Object BaseUtcOffsetMinutes -ge -1000 |
    Select-Object Id, BaseUtcOffsetMinutes |
    Where-Object BaseUtcOffsetMinutes -ge -1000 |
    Select-Object Id
