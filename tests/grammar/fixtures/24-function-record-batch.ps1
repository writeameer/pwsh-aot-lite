function Get-TwoVerbs {
    $group = 'Common'
    Get-Verb -Verb Add | Select-Object Verb
    Get-Verb -Verb Get | Select-Object Verb
}

Get-TwoVerbs | Select-Object Verb
