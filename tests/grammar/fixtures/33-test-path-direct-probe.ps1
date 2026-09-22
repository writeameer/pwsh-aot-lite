Test-Path ./README.md
Test-Path -Path ./docs -Type Container
Test-Path -Path ./README.md -PathType Leaf
Test-Path -LiteralPath ./README.md
Test-Path -IsValid ./missing
