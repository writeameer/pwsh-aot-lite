foreach ($item in Get-Verb) {
    Get-Verb -Verb $item
}

foreach ($item in 1..3) {
    Get-Verb -Verb $item
}

foreach -parallel ($item in $items) {
    Get-Verb -Verb $item
}

foreach -throttlelimit 2 ($item in $items) {
    Get-Verb -Verb $item
}

:named foreach ($item in $items) {
    break named
}
