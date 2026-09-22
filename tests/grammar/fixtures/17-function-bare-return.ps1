function Get-FirstVerb($enabled, $verbs) {
    if ($enabled) {
        foreach ($verb in $verbs) {
            Get-Verb -Verb $verb
            return
        }
    }

    Get-Verb -Verb Get
}

Get-FirstVerb $true ('Add', 'Get')
