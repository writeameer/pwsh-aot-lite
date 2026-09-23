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
    throw 'This compatibility proof is intentionally scoped to the reviewed macOS direct physical Resolve-Path adapter.'
}

$native = (Resolve-Path -LiteralPath $NativePwshPath).Path
# /tmp is an alias/symlink on macOS and is intentionally outside the no-follow
# slice. Keep this path short enough that stock redirected table formatting
# does not introduce unrelated width truncation into the exact view proof.
$fixture = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) ("rp-" + [guid]::NewGuid().ToString('N'))

function Invoke-Captured {
    param([Parameter(Mandatory)][string]$FileName, [Parameter(Mandatory)][string[]]$Arguments, [string]$WorkingDirectory)

    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $FileName
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    if ($WorkingDirectory) { $info.WorkingDirectory = $WorkingDirectory }
    foreach ($argument in $Arguments) { $null = $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    if (-not $process.Start()) { throw "Could not start '$FileName'." }
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    [pscustomobject]@{ ExitCode = $process.ExitCode; StdOut = $stdout; StdErr = $stderr }
}

function Assert-ExactRender {
    param([Parameter(Mandatory)][string]$Script, [Parameter(Mandatory)][string]$WorkingDirectory)

    $normal = Invoke-Captured -FileName 'pwsh' -Arguments @('-NoProfile', '-Command', "`$PSStyle.OutputRendering = 'PlainText'; $Script") -WorkingDirectory $WorkingDirectory
    $aot = Invoke-Captured -FileName $native -Arguments @('-Command', $Script) -WorkingDirectory $WorkingDirectory
    if ($normal.ExitCode -ne 0 -or $aot.ExitCode -ne 0) {
        throw "Resolve-Path exact oracle failed. normal=$($normal.ExitCode), native=$($aot.ExitCode), script=$Script"
    }
    if ($normal.StdOut -cne $aot.StdOut) {
        Write-Host '--- normal pwsh ---'; Write-Host $normal.StdOut
        Write-Host '--- Native AOT ---'; Write-Host $aot.StdOut
        throw "Resolve-Path exact table rendering mismatch for '$Script'."
    }
}

try {
    $null = New-Item -ItemType Directory -Path $fixture
    $file = Join-Path $fixture 'one-file.txt'
    $directory = Join-Path $fixture 'one-directory'
    $nested = Join-Path $directory 'nested.txt'
    $missing = Join-Path $fixture 'missing.txt'
    $otherCwd = Join-Path $fixture 'other-cwd'
    $linkTarget = Join-Path $fixture 'link-target'
    $linkParent = Join-Path $fixture 'link-parent'
    [IO.File]::WriteAllText($file, 'alpha')
    $null = New-Item -ItemType Directory -Path $directory, $otherCwd, $linkTarget
    [IO.File]::WriteAllText($nested, 'must not enumerate')
    [IO.File]::WriteAllText((Join-Path $linkTarget 'ordinary-child.txt'), 'must not traverse')
    $null = [IO.Directory]::CreateSymbolicLink($linkParent, $linkTarget)

    # The table, including its single leading blank line, is compared exactly.
    foreach ($path in $file, $directory) {
        $escaped = $path.Replace("'", "''")
        Assert-ExactRender -Script "Resolve-Path -Path '$escaped'" -WorkingDirectory $fixture
    }

    # The binary captures its startup root. A later process CWD never becomes
    # an implicit resolver root; the admitted unrooted path is relative to it.
    $relative = 'one-file.txt'
    $fromCapturedRoot = Invoke-Captured -FileName $native -Arguments @('-Command', "Resolve-Path -Path $relative") -WorkingDirectory $fixture
    $fromOtherCwd = Invoke-Captured -FileName $native -Arguments @('-Command', "Resolve-Path -Path $relative") -WorkingDirectory $otherCwd
    if ($fromCapturedRoot.StdOut -notmatch [regex]::Escape($file) -or $fromOtherCwd.StdErr -notmatch 'AOT6206') {
        throw 'Resolve-Path captured-root versus invocation-CWD contract regression.'
    }

    $escapedFile = $file.Replace("'", "''")
    $escapedMissing = $missing.Replace("'", "''")
    $continued = Invoke-Captured -FileName $native -Arguments @('-Command', "Resolve-Path -Path '$escapedFile','$escapedMissing','$escapedFile'") -WorkingDirectory $fixture
    if ([regex]::Matches($continued.StdOut, [regex]::Escape($file)).Count -ne 2 -or $continued.StdErr -notmatch 'AOT6206') {
        throw 'Resolve-Path missing-path continuation regression.'
    }

    $empty = Invoke-Captured -FileName $native -Arguments @('-Command', "Resolve-Path -Path ''") -WorkingDirectory $fixture
    $whitespace = Invoke-Captured -FileName $native -Arguments @('-Command', "Resolve-Path -Path '  '") -WorkingDirectory $fixture
    if ($empty.StdErr -notmatch 'AOT6213' -or $whitespace.StdErr -notmatch 'AOT6206') {
        throw 'Resolve-Path empty versus whitespace literal diagnostic regression.'
    }

    $escapedLinked = (Join-Path $linkParent 'ordinary-child.txt').Replace("'", "''")
    $rejected = Invoke-Captured -FileName $native -Arguments @('-Command', "Resolve-Path -Path '$escapedLinked'") -WorkingDirectory $fixture
    if ($rejected.StdOut.Length -ne 0 -or $rejected.StdErr -notmatch 'AOT6205') {
        throw 'Resolve-Path ancestor-symlink no-follow regression.'
    }

    $relativeRejected = Invoke-Captured -FileName $native -Arguments @('-Command', "Resolve-Path -Relative -Path '$escapedFile'") -WorkingDirectory $fixture
    if ($relativeRejected.StdErr -notmatch 'AOT2002') { throw 'Resolve-Path -Relative should fail closed.' }

    Write-Host 'Resolve-Path direct file/directory exact rendering, continuation, captured-root, and no-follow proof passed.'
}
finally {
    if (-not $KeepFixture -and (Test-Path -LiteralPath $fixture)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
