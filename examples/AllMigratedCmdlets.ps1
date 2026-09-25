Join-String -InputObject "=== Get-ChildItem ===" -Separator ""
Get-ChildItem -Path fixtures

Join-String -InputObject "=== Get-FileHash ===" -Separator ""
Get-FileHash -LiteralPath fixtures/all-migrated-cmdlets.txt -Algorithm SHA256

Join-String -InputObject "=== New-Guid ===" -Separator ""
New-Guid -Empty

Join-String -InputObject "=== New-TimeSpan ===" -Separator ""
New-TimeSpan -Days 1 -Hours 2 -Minutes 3 -Seconds 4 -Milliseconds 5

Join-String -InputObject "=== Start-Sleep ===" -Separator ""
Start-Sleep -Milliseconds 1

Join-String -InputObject "=== Get-Item ===" -Separator ""
Get-Item -Path fixtures/all-migrated-cmdlets.txt

Join-String -InputObject "=== Test-Path ===" -Separator ""
Test-Path -Path fixtures/all-migrated-cmdlets.txt -PathType Leaf

Join-String -InputObject "=== Resolve-Path ===" -Separator ""
Resolve-Path -Path fixtures/all-migrated-cmdlets.txt

Join-String -InputObject "=== Convert-Path ===" -Separator ""
Convert-Path -Path fixtures/all-migrated-cmdlets.txt

Join-String -InputObject "=== Join-Path ===" -Separator ""
Join-Path -Path alpha,beta -ChildPath child

Join-String -InputObject "=== Split-Path ===" -Separator ""
Split-Path -Path alpha/beta -Leaf

Join-String -InputObject "=== Where-Object / Select-Object ===" -Separator ""
Get-TimeZone -Id UTC | Where-Object BaseUtcOffsetMinutes -GE -1000 | Select-Object Id, BaseUtcOffsetMinutes

Join-String -InputObject "=== Get-Random ===" -Separator ""
Get-Random -SetSeed 7 -Minimum 0 -Maximum 10

Join-String -InputObject "=== Get-SecureRandom ===" -Separator ""
Get-SecureRandom -Minimum 0 -Maximum 10

Join-String -InputObject "=== Join-String ===" -Separator ""
Join-String -InputObject "alpha","beta" -Separator ","

Join-String -InputObject "=== Compare-Object ===" -Separator ""
Compare-Object -ReferenceObject "alpha","beta" -DifferenceObject "beta","gamma" -SyncWindow 0

Join-String -InputObject "=== Select-String ===" -Separator ""
Select-String -Path fixtures/all-migrated-cmdlets.txt -Pattern "native AOT sample" -SimpleMatch -Raw

Join-String -InputObject "=== Measure-Object ===" -Separator ""
Measure-Object -InputObject "hello world" -Line -Word -Character

Join-String -InputObject "=== Get-Unique ===" -Separator ""
Join-Path -Path alpha,alpha,beta -ChildPath item | Get-Unique -AsString

Join-String -InputObject "=== Group-Object ===" -Separator ""
Join-Path -Path alpha,alpha,beta -ChildPath item | Group-Object -NoElement

Join-String -InputObject "=== Sort-Object ===" -Separator ""
Join-Path -Path beta,alpha,gamma -ChildPath item | Sort-Object

Join-String -InputObject "=== ForEach-Object ===" -Separator ""
Join-Path -Path alpha,beta -ChildPath item | ForEach-Object -MemberName Length

Join-String -InputObject "=== Get-Member ===" -Separator ""
Join-Path -Path alpha,beta -ChildPath item | Get-Member -Name Length
