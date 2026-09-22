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
    throw 'This compatibility proof is intentionally scoped to the reviewed macOS direct physical probe adapter.'
}

$native = (Resolve-Path -LiteralPath $NativePwshPath).Path
# /tmp and /var are symlink aliases on macOS, while this slice deliberately
# rejects every link component. Keep the fixture below the checked-out root.
$fixture = Join-Path ([IO.Path]::GetFullPath((Get-Location).Path)) ("pwsh-aot-lite-testpath-" + [guid]::NewGuid().ToString('N'))

function Invoke-Captured {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $FileName
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $null = $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    if (-not $process.Start()) { throw "Could not start '$FileName'." }
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    [pscustomobject]@{ ExitCode = $process.ExitCode; StdOut = $stdout; StdErr = $stderr }
}

function Assert-BoolMatch {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$PathType
    )

    $escaped = $Path.Replace("'", "''")
    $oracle = "`$PSStyle.OutputRendering = 'PlainText'; Test-Path -Path '$escaped' -PathType $PathType"
    $normal = Invoke-Captured -FileName 'pwsh' -Arguments @('-NoProfile', '-Command', $oracle)
    $nativeResult = Invoke-Captured -FileName $native -Arguments @('-Command', "Test-Path -Path '$escaped' -PathType $PathType")
    if ($normal.ExitCode -ne 0 -or $nativeResult.ExitCode -ne 0) {
        throw "Test-Path scalar oracle failed for '$Path' ($PathType). normal=$($normal.ExitCode), native=$($nativeResult.ExitCode)."
    }

    # Boolean scalar output has no table or styling contract to normalize.
    if ($normal.StdOut -cne $nativeResult.StdOut) {
        Write-Host "--- normal pwsh for $Path ($PathType) ---"
        Write-Host $normal.StdOut
        Write-Host "--- Native AOT for $Path ($PathType) ---"
        Write-Host $nativeResult.StdOut
        throw "Test-Path scalar Boolean mismatch for '$Path' ($PathType). No normalization is permitted."
    }
}

try {
    $null = New-Item -ItemType Directory -Path $fixture
    $file = Join-Path $fixture 'one-file.txt'
    $directory = Join-Path $fixture 'one-directory'
    $missing = Join-Path $fixture 'missing.txt'
    $linkTarget = Join-Path $fixture 'link-target'
    $linkParent = Join-Path $fixture 'link-parent'
    [IO.File]::WriteAllText($file, 'alpha')
    $null = New-Item -ItemType Directory -Path $directory
    $null = New-Item -ItemType Directory -Path $linkTarget
    [IO.File]::WriteAllText((Join-Path $linkTarget 'ordinary-child.txt'), 'must not traverse')
    $null = [IO.Directory]::CreateSymbolicLink($linkParent, $linkTarget)

    foreach ($pathType in 'Any', 'Container', 'Leaf') {
        foreach ($path in $file, $directory, $missing, '  ') {
            Assert-BoolMatch -Path $path -PathType $pathType
        }
    }

    $linkedChild = (Join-Path $linkParent 'ordinary-child.txt').Replace("'", "''")
    $rejected = Invoke-Captured -FileName $native -Arguments @('-Command', "Test-Path -Path '$linkedChild'")
    if ($rejected.StdOut.Length -ne 0 -or $rejected.StdErr -notmatch 'AOT6205') {
        throw 'Test-Path ancestor-symlink no-follow regression: rejected probe emitted a Boolean or omitted AOT6205.'
    }

    Write-Host 'Test-Path macOS direct file/directory/missing scalar Boolean and no-follow compatibility proof passed.'
}
finally {
    if (-not $KeepFixture -and (Test-Path -LiteralPath $fixture)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
