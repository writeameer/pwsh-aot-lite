New-TimeSpan
New-TimeSpan -Days 1 -Hours 2 -Minutes 3 -Seconds 4 -Milliseconds 5
New-TimeSpan -Seconds -2
New-TimeSpan -Seconds 1.2
New-TimeSpan -Milliseconds 0 | Select-Object Value, TotalMilliseconds
New-TimeSpan -Start 2020-01-01
New-TimeSpan 2020-01-01
