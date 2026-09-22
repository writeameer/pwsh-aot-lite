function Get-Zones($group = 'Common') {
    Get-TimeZone -ListAvailable
}

Get-Zones -GROUP:Common |
    Where-Object BaseUtcOffsetMinutes -ge -1000 |
    Select-Object Id, BaseUtcOffsetMinutes
