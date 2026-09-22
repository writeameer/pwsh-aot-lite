function Get-CommonVerb($group) {
    Get-Verb -Group $group | Select-Object Verb
}

Get-CommonVerb Common
