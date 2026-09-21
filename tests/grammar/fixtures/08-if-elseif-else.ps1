$threshold = 10

if ($threshold -gt 0) {
    $group = 'Common'
    Get-Verb -Group $group | Select-Object Verb
}
elseif ($false) {
    Get-Verb -Group Filter
}
else {
    Get-Verb -Group Common
}
