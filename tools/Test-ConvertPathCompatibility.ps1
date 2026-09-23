[CmdletBinding()]
param([Parameter(Mandatory)][string]$NativePwshPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'Scoped to the reviewed macOS direct physical adapter.' }
$native = (Resolve-Path -LiteralPath $NativePwshPath).Path
$root = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) ('cp-' + [guid]::NewGuid().ToString('N'))
function Run([string]$exe,[string]$script,[string]$cwd) {
  $i=[Diagnostics.ProcessStartInfo]::new($exe); $i.UseShellExecute=$false; $i.RedirectStandardOutput=$true; $i.RedirectStandardError=$true; $i.WorkingDirectory=$cwd; $null=$i.ArgumentList.Add('-Command'); $null=$i.ArgumentList.Add($script)
  $p=[Diagnostics.Process]::new();$p.StartInfo=$i;$null=$p.Start();$o=$p.StandardOutput.ReadToEnd();$e=$p.StandardError.ReadToEnd();$p.WaitForExit();[pscustomobject]@{Code=$p.ExitCode;Out=$o;Err=$e}
}
try {
  $other=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) ('cp-other-' + [guid]::NewGuid().ToString('N'))
  $null=New-Item -ItemType Directory -Path $root,$other; $file=Join-Path $root 'file.txt'; $dir=Join-Path $root 'dir'; $missing=Join-Path $root 'missing'; $linkTarget=Join-Path $root 'target'; $link=Join-Path $root 'link'
  [IO.File]::WriteAllText($file,'x');$null=New-Item -ItemType Directory -Path $dir,$linkTarget;$null=[IO.Directory]::CreateSymbolicLink($link,$linkTarget)
  $qfile=$file.Replace("'","''");$qdir=$dir.Replace("'","''");$qmissing=$missing.Replace("'","''")
  foreach($s in "Convert-Path -Path '$qfile'","Convert-Path -Path '$qdir'","Convert-Path -Path '$qfile','$qdir'") {
    $stock=Run 'pwsh' "`$PSStyle.OutputRendering='PlainText';$s" $root;$aot=Run $native $s $root
    if($stock.Code -ne 0 -or $aot.Code -ne 0 -or $stock.Out -cne $aot.Out){throw "Exact Convert-Path output mismatch: $s"}
  }
  # The catalog captures the host composition root.  Start the native image
  # from the controlled fixture root, then prove that the same relative input
  # cannot be accidentally resolved from a distinct invocation CWD.
  $stockRelative=Run 'pwsh' "`$PSStyle.OutputRendering='PlainText';Convert-Path -Path 'file.txt'" $root
  $nativeRelative=Run $native "Convert-Path -Path 'file.txt'" $root
  if($stockRelative.Code -ne 0 -or $nativeRelative.Code -ne 0 -or $stockRelative.Out -cne $nativeRelative.Out -or $nativeRelative.Out -cne ($file + [Environment]::NewLine)){throw 'Captured-root relative Convert-Path rendering regression.'}
  $otherRelative=Run $native "Convert-Path -Path 'file.txt'" $other
  if($otherRelative.Out.Length -ne 0 -or $otherRelative.Err -notmatch 'AOT6206'){throw 'Controlled alternate-CWD relative Convert-Path regression.'}
  $continue=Run $native "Convert-Path -Path '$qfile','$qmissing','$qdir'" $root
  if($continue.Out -notmatch [regex]::Escape($file) -or $continue.Out -notmatch [regex]::Escape($dir) -or $continue.Err -notmatch 'AOT6206'){throw 'No-empty continuation regression.'}
  $empty=Run $native "Convert-Path -Path '$qfile','','$qdir'" $root
  if($empty.Out.Length -ne 0 -or $empty.Err -notmatch 'AOT6213' -or $empty.Err -notmatch ':1:.*'){throw 'First-empty zero-output/span regression.'}
  $white=Run $native "Convert-Path -Path '  '" $root
  if($white.Out.Length -ne 0 -or $white.Err -notmatch 'AOT6206'){throw 'Whitespace literal regression.'}
  $provider=Run $native "Convert-Path -Path 'FileSystem::/tmp'" $root;$wild=Run $native "Convert-Path -Path '$root/*.txt'" $root;$lp=Run $native "Convert-Path -LP '$qfile'" $root;$force=Run $native "Convert-Path -Force '$qfile'" $root;$linked=Run $native "Convert-Path -Path '$($link.Replace("'","''"))/x'" $root
  if($provider.Err -notmatch 'AOT6201' -or $wild.Err -notmatch 'AOT6202' -or $lp.Err -notmatch 'AOT2002' -or $force.Err -notmatch 'AOT2002' -or $linked.Err -notmatch 'AOT6205'){throw 'Fail-closed boundary regression.'}
  Write-Host 'Convert-Path strict direct physical compatibility proof passed.'
} finally { if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}; if(Test-Path -LiteralPath $other){Remove-Item -LiteralPath $other -Recurse -Force} }
