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
# macOS commonly spells the system temporary area through /tmp or /var,
# which are themselves symlink aliases. The command's strict direct-physical
# contract deliberately rejects those aliases, so use the checked-out working
# directory as the fixture's non-link parent.
$fixture = Join-Path ([IO.Path]::GetFullPath((Get-Location).Path)) ("pwsh-aot-lite-gci-format-" + [guid]::NewGuid().ToString('N'))

function Normalize-DefaultView {
    param([Parameter(Mandatory)][string]$Text)

    # Both hosts use the same fixture, process culture, and PlainText oracle.
    # Normalize transport only: ANSI escapes, newline representation, and the
    # host-specific terminal trailing newline. Table
    # geometry (including every inter-column space, header/data start, padding,
    # underline, grouping path, values, and row order) is a source-attributed
    # compatibility assertion and must remain byte-for-byte identical.
    $ansi = [regex]::new("`e\[[0-?]*[ -/]*[@-~]")
    $ansi.Replace($Text.Replace("`r`n", "`n").Replace("`r", "`n"), '').TrimEnd("`n")
}

try {
    $null = New-Item -ItemType Directory -Path $fixture
    $alpha = Join-Path $fixture '01-alpha.txt'
    $beta = Join-Path $fixture '02-beta.txt'
    $directory = Join-Path $fixture '03-directory'
    $ancestorTarget = Join-Path $fixture '04-ancestor-target'
    $ancestorCase = Join-Path $fixture '05-ancestor-case'
    $ancestorLink = Join-Path $ancestorCase 'linked-parent'
    $ancestorChild = Join-Path $ancestorTarget 'must-not-traverse.txt'
    [IO.File]::WriteAllText($alpha, 'alpha')
    [IO.File]::WriteAllText($beta, 'bravo-charlie')
    $null = New-Item -ItemType Directory -Path $directory
    $null = New-Item -ItemType Directory -Path $ancestorTarget
    $null = New-Item -ItemType Directory -Path $ancestorCase
    [IO.File]::WriteAllText($ancestorChild, 'must-not-traverse')
    $null = [IO.Directory]::CreateSymbolicLink($ancestorLink, $ancestorTarget)

    $fileMode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::OtherRead
    $directoryMode = $fileMode -bor [IO.UnixFileMode]::UserExecute -bor [IO.UnixFileMode]::GroupExecute -bor [IO.UnixFileMode]::OtherExecute
    [IO.File]::SetUnixFileMode($alpha, $fileMode)
    [IO.File]::SetUnixFileMode($beta, $fileMode)
    [IO.File]::SetUnixFileMode($directory, $directoryMode)

    $lastWrite = [datetime]::SpecifyKind([datetime]'2024-02-03T04:05:00', [DateTimeKind]::Local)
    [IO.File]::SetLastWriteTime($alpha, $lastWrite)
    [IO.File]::SetLastWriteTime($beta, $lastWrite)
    [IO.Directory]::SetLastWriteTime($directory, $lastWrite)

    $escapedFixture = $fixture.Replace("'", "''")
    $oracleCommand = "`$PSStyle.OutputRendering = 'PlainText'; Get-ChildItem -LiteralPath '$escapedFixture' | Out-String -Width 500"
    $normalRaw = ((& pwsh -NoProfile -Command $oracleCommand) -join [Environment]::NewLine)
    if ($LASTEXITCODE -ne 0) {
        throw "Installed pwsh oracle failed with exit code $LASTEXITCODE."
    }

    $nativeRaw = ((& $native -Command "Get-ChildItem -Path '$escapedFixture'") -join [Environment]::NewLine)
    if ($LASTEXITCODE -ne 0) {
        throw "Native AOT runner failed with exit code $LASTEXITCODE."
    }

    $normal = Normalize-DefaultView $normalRaw
    $nativeView = Normalize-DefaultView $nativeRaw
    if ($normal -cne $nativeView) {
        Write-Host "--- normal pwsh raw ---"
        Write-Host $normalRaw
        Write-Host "--- Native AOT raw ---"
        Write-Host $nativeRaw
        Write-Host "--- normalized normal ---"
        $normal | Write-Host
        Write-Host "--- normalized Native AOT ---"
        $nativeView | Write-Host
        throw 'Get-ChildItem default-view compatibility mismatch. Only ANSI/newline transport normalization is permitted.'
    }

    # This is intentionally not a normal-pwsh equivalence assertion: the AOT
    # direct-physical contract rejects *every* symlink component. Prove the
    # dangerous case separately, where the final name is ordinary but an
    # ancestor directory is a link. The native command must not project the
    # target child and must emit its stable fail-closed diagnostic.
    $escapedAncestorChild = (Join-Path $ancestorLink 'must-not-traverse.txt').Replace("'", "''")
    # Diagnostics are deliberately written to the native process's error
    # stream. Capture both streams directly so PowerShell does not render the
    # expected failing native diagnostic as an incidental test-host error.
    $probeInfo = [Diagnostics.ProcessStartInfo]::new()
    $probeInfo.FileName = $native
    $probeInfo.UseShellExecute = $false
    $probeInfo.RedirectStandardOutput = $true
    $probeInfo.RedirectStandardError = $true
    $null = $probeInfo.ArgumentList.Add('-Command')
    $null = $probeInfo.ArgumentList.Add("Get-ChildItem -Path '$escapedAncestorChild'")
    $probe = [Diagnostics.Process]::new()
    $probe.StartInfo = $probeInfo
    if (-not $probe.Start()) {
        throw 'Could not start the native ancestor-link probe.'
    }
    $probeOutput = $probe.StandardOutput.ReadToEnd()
    $probeError = $probe.StandardError.ReadToEnd()
    $probe.WaitForExit()
    $ancestorRaw = $probeOutput + [Environment]::NewLine + $probeError
    if ($ancestorRaw -notmatch 'AOT6205' -or $ancestorRaw -match '(?m)^UnixMode\s+') {
        Write-Host '--- Native AOT ancestor-link probe ---'
        Write-Host $ancestorRaw
        throw 'Get-ChildItem ancestor-symlink no-follow regression: native output did not fail closed.'
    }

    Write-Host 'Get-ChildItem macOS default-view compatibility and ancestor-link no-follow proof passed.'
    $normal | ForEach-Object { Write-Host $_ }
}
finally {
    if (-not $KeepFixture -and (Test-Path -LiteralPath $fixture)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
