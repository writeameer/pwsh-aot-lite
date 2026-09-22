function First-Verb {
    Get-Verb -Verb Add | Select-Object Verb
    return
    Get-Verb -Verb Get | Select-Object Verb
}

First-Verb | Select-Object Verb

function Mixed-Output {
    Get-Verb -Verb Add | Select-Object Verb
    Get-Date | Select-Object DateTime
}

Mixed-Output | Where-Object Verb -eq 0
