$scalar = 1
$list = 1, 2
$none = $null
$literal = '$notInterpolation'
Get-Process -Id $scalar
Get-Process -Name $list
Get-Verb -Group $missing
Get-Verb -Group '$notInterpolation'
