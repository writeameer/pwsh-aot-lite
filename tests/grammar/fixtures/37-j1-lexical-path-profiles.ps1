# J1 parser provenance fixture. Runtime expectations live in the checked J1
# corpus; these lines ensure the admitted/rejected parameter spellings retain
# their pinned upstream AST/token shape.
Join-Path alpha,beta gamma
Join-Path -PSPath alpha,beta -ChildPath gamma,delta -AdditionalChildPath tail
Join-Path alpha foo/
Split-Path alpha/beta -Leaf
Split-Path alpha/beta -LeafBase
Split-Path alpha/beta.txt -Extension
Split-Path /alpha/beta -IsAbsolute
Split-Path -LP alpha/beta
Split-Path alpha/beta -Leaf -Extension
Join-Path -Path alpha -ChildPath beta -Resolve
Split-Path -Path alpha/beta -Qualifier
