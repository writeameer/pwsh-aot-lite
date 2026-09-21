if ($name -ceq 'Common') {
    Get-Verb -Group Common
}

if ($enabled -and $true) {
    Get-Verb -Group Common
}

if (Get-Date) {
    Get-Verb -Group Common
}
