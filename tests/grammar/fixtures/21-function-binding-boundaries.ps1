function Pick-Verb($group = 'Common', $verb = 'Add') {
    Get-Verb -Group $group -Verb $verb
}

Pick-Verb -GROUP:Common -VERB Add
Pick-Verb Common -Verb Get
Pick-Verb -missing value
