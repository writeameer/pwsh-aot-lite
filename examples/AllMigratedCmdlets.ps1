[CmdletBinding()]
param(
    [Parameter()]
    [string] $NativePwshPath = (Join-Path $PSScriptRoot '../artifacts/osx-arm64/PwshAotLite')
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$nativePath = [System.IO.Path]::GetFullPath($NativePwshPath, $repoRoot)
if (-not (Test-Path -LiteralPath $nativePath -PathType Leaf)) {
    throw "Native AOT executable was not found: $nativePath. Publish it first, or pass -NativePwshPath."
}

$demoRoot = Join-Path $repoRoot ('pwsh-aot-lite-example-' + [guid]::NewGuid().ToString('N'))
$dataFile = Join-Path $demoRoot 'sample.txt'

function Invoke-NativeExample {
    param(
        [Parameter(Mandatory)]
        [string] $Title,

        [Parameter(Mandatory)]
        [string] $Command
    )

    Write-Host "`n=== $Title ==="
    Write-Host "> $Command"
    & $nativePath -Command $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Native command failed with exit code ${LASTEXITCODE}: $Command"
    }
}

New-Item -ItemType Directory -Path $demoRoot | Out-Null
@(
    'native AOT sample',
    'PowerShell-compatible literal search',
    'native AOT sample'
) | Set-Content -LiteralPath $dataFile -NoNewline:$false

try {
    Push-Location $repoRoot

    # Pre-cohort direct physical, scalar, and duration ports.
    Invoke-NativeExample 'Get-ChildItem' "Get-ChildItem -Path '$demoRoot'"
    Invoke-NativeExample 'Get-FileHash' "Get-FileHash -LiteralPath '$dataFile' -Algorithm SHA256"
    Invoke-NativeExample 'New-Guid' 'New-Guid -Empty'
    Invoke-NativeExample 'New-TimeSpan' 'New-TimeSpan -Days 1 -Hours 2 -Minutes 3 -Seconds 4 -Milliseconds 5'
    Invoke-NativeExample 'Start-Sleep' 'Start-Sleep -Milliseconds 1'
    Invoke-NativeExample 'Get-Item' "Get-Item -Path '$dataFile'"
    Invoke-NativeExample 'Test-Path' "Test-Path -Path '$dataFile' -PathType Leaf"
    Invoke-NativeExample 'Resolve-Path' "Resolve-Path -Path '$dataFile'"
    Invoke-NativeExample 'Convert-Path' "Convert-Path -Path '$dataFile'"

    # J1 lexical text ports: no provider or filesystem authority is needed.
    Invoke-NativeExample 'Join-Path' 'Join-Path -Path alpha,beta -ChildPath child'
    Invoke-NativeExample 'Split-Path' 'Split-Path -Path alpha/beta -Leaf'

    # J2 static record stages use the existing typed time-zone source.  The
    # predicate and projection are both deliberately closed numeric/field forms.
    Invoke-NativeExample 'Where-Object and Select-Object' 'Get-TimeZone -Id UTC | Where-Object BaseUtcOffsetMinutes -GE -1000 | Select-Object Id, BaseUtcOffsetMinutes'

    # J2 direct scalar and direct-string ports.
    Invoke-NativeExample 'Get-Random' 'Get-Random -SetSeed 7 -Minimum 0 -Maximum 10'
    Invoke-NativeExample 'Get-SecureRandom' 'Get-SecureRandom -Minimum 0 -Maximum 10'
    Invoke-NativeExample 'Join-String' 'Join-String -InputObject "alpha","beta" -Separator ","'
    Invoke-NativeExample 'Compare-Object' 'Compare-Object -ReferenceObject "alpha","beta" -DifferenceObject "beta","gamma" -SyncWindow 0'
    Invoke-NativeExample 'Select-String' "Select-String -Path '$dataFile' -Pattern 'native AOT sample' -SimpleMatch -Raw"
    Invoke-NativeExample 'Measure-Object' 'Measure-Object -InputObject "hello world" -Line -Word -Character'

    # The remaining J2 ports intentionally consume only TextRecord-producing
    # pipelines.  Join-Path supplies that closed text input without filesystem I/O.
    Invoke-NativeExample 'Get-Unique' 'Join-Path -Path alpha,alpha,beta -ChildPath item | Get-Unique -AsString'
    Invoke-NativeExample 'Group-Object' 'Join-Path -Path alpha,alpha,beta -ChildPath item | Group-Object -NoElement'
    Invoke-NativeExample 'Sort-Object' 'Join-Path -Path beta,alpha,gamma -ChildPath item | Sort-Object'
    Invoke-NativeExample 'ForEach-Object' 'Join-Path -Path alpha,beta -ChildPath item | ForEach-Object -MemberName Length'
    Invoke-NativeExample 'Get-Member' 'Join-Path -Path alpha,beta -ChildPath item | Get-Member -Name Length'
}
finally {
    Pop-Location -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $demoRoot -Recurse -Force -ErrorAction SilentlyContinue
}
