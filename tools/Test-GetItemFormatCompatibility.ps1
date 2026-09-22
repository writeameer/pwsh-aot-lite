[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$NativePwshPath,

    [switch]$KeepFixture
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsMacOS) {
    throw 'This compatibility proof is intentionally scoped to the reviewed macOS UnixStat adapter.'
}

$native = (Resolve-Path -LiteralPath $NativePwshPath).Path
# /tmp and /var are symlink aliases on macOS, while this slice deliberately
# rejects every link component. Keep the fixture below the checked-out root.
$fixture = Join-Path ([IO.Path]::GetFullPath((Get-Location).Path)) ("pwsh-aot-lite-getitem-format-" + [guid]::NewGuid().ToString('N'))

function Normalize-DefaultView {
    param([Parameter(Mandatory)][string]$Text)

    $ansi = [regex]::new("`e\[[0-?]*[ -/]*[@-~]")
    $ansi.Replace($Text.Replace("`r`n", "`n").Replace("`r", "`n"), '').TrimEnd("`n")
}

function Assert-DefaultViewMatch {
    param([Parameter(Mandatory)][string]$Path)

    $escaped = $Path.Replace("'", "''")
    $oracle = "`$PSStyle.OutputRendering = 'PlainText'; Get-Item -LiteralPath '$escaped' | Out-String -Width 500"
    $normalRaw = ((& pwsh -NoProfile -Command $oracle) -join [Environment]::NewLine)
    if ($LASTEXITCODE -ne 0) { throw "Installed pwsh oracle failed for '$Path'." }

    $nativeRaw = ((& $native -Command "Get-Item -Path '$escaped'") -join [Environment]::NewLine)
    if ($LASTEXITCODE -ne 0) { throw "Native AOT runner failed for '$Path'." }

    $normal = Normalize-DefaultView $normalRaw
    $nativeView = Normalize-DefaultView $nativeRaw
    if ($normal -cne $nativeView) {
        Write-Host "--- normal pwsh raw for $Path ---"
        Write-Host $normalRaw
        Write-Host "--- Native AOT raw for $Path ---"
        Write-Host $nativeRaw
        throw "Get-Item default-view compatibility mismatch for '$Path'. Only ANSI/newline transport normalization is permitted."
    }

    return $native
}

try {
    $null = New-Item -ItemType Directory -Path $fixture
    $file = Join-Path $fixture 'one-file.txt'
    $directory = Join-Path $fixture 'one-directory'
    $nested = Join-Path $directory 'must-not-enumerate.txt'
    $linkTarget = Join-Path $fixture 'link-target'
    $linkParent = Join-Path $fixture 'link-parent'
    [IO.File]::WriteAllText($file, 'alpha')
    $null = New-Item -ItemType Directory -Path $directory
    [IO.File]::WriteAllText($nested, 'not an output of Get-Item')
    $null = New-Item -ItemType Directory -Path $linkTarget
    [IO.File]::WriteAllText((Join-Path $linkTarget 'ordinary-child.txt'), 'must not traverse')
    $null = [IO.Directory]::CreateSymbolicLink($linkParent, $linkTarget)

    $fileMode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::OtherRead
    $directoryMode = $fileMode -bor [IO.UnixFileMode]::UserExecute -bor [IO.UnixFileMode]::GroupExecute -bor [IO.UnixFileMode]::OtherExecute
    [IO.File]::SetUnixFileMode($file, $fileMode)
    [IO.File]::SetUnixFileMode($directory, $directoryMode)
    $lastWrite = [datetime]::SpecifyKind([datetime]'2024-02-03T04:05:00', [DateTimeKind]::Local)
    [IO.File]::SetLastWriteTime($file, $lastWrite)
    [IO.Directory]::SetLastWriteTime($directory, $lastWrite)

    $null = Assert-DefaultViewMatch -Path $file
    $directoryView = Assert-DefaultViewMatch -Path $directory
    if ($directoryView -match '(?m)must-not-enumerate\.txt') {
        throw 'Get-Item directory output enumerated a nested child instead of returning the directory itself.'
    }

    $escapedLinkChild = (Join-Path $linkParent 'ordinary-child.txt').Replace("'", "''")
    $probeInfo = [Diagnostics.ProcessStartInfo]::new()
    $probeInfo.FileName = $native
    $probeInfo.UseShellExecute = $false
    $probeInfo.RedirectStandardOutput = $true
    $probeInfo.RedirectStandardError = $true
    $null = $probeInfo.ArgumentList.Add('-Command')
    $null = $probeInfo.ArgumentList.Add("Get-Item -Path '$escapedLinkChild'")
    $probe = [Diagnostics.Process]::new()
    $probe.StartInfo = $probeInfo
    if (-not $probe.Start()) { throw 'Could not start the native ancestor-link probe.' }
    $probeOutput = $probe.StandardOutput.ReadToEnd()
    $probeError = $probe.StandardError.ReadToEnd()
    $probe.WaitForExit()
    $ancestorRaw = $probeOutput + [Environment]::NewLine + $probeError
    if ($ancestorRaw -notmatch 'AOT6205' -or $ancestorRaw -match '(?m)^UnixMode\s+') {
        throw 'Get-Item ancestor-symlink no-follow regression: native output did not fail closed.'
    }

    Write-Host 'Get-Item macOS direct file/directory default-view and no-enumeration compatibility proof passed.'
}
finally {
    if (-not $KeepFixture -and (Test-Path -LiteralPath $fixture)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
