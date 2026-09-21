$verbs = 'Add', 'Get'
foreach ($verb in $verbs) {
    if ($verb -eq 'Add') {
        Get-Verb -Verb $verb | Select-Object Verb
    }
}

foreach ($outer in 'Add', 'Get') {
    foreach ($inner in 'Add', 'Get') {
        Get-Verb -Verb $inner
    }
}
