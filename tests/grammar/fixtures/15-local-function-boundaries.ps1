function With-Default($group = 'Common') {
    Get-Verb -Group $group
}

function With-BodyParam {
    param($group)
    Get-Verb -Group $group
}

filter Stream-Verb {
    Get-Verb -Verb Add
}
