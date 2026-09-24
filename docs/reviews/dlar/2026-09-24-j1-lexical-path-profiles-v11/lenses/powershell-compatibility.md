# PowerShell semantic and compatibility lens

**Perspective:** PowerShell design-principles-inspired perspective—not authored
by or attributed to any real person.  This is an evidence-backed design lens,
not an impersonation or endorsement.

**Verdict:** **PASS.**

**Reviewed packet:** the immutable J1 v11 plan and PASS report pinned in
[evidence.md](../evidence.md).  This in-repository ledger summarizes that
external review with provenance; it does not replace or modify it.

## Evidence-backed conclusion

The packet closes the prior compatibility blockers: it declares P1
`Join-Path` aliases, positions, mandatory/cardinality rules, string pipeline
admission, atomic remaining arguments, explicitly rejected source property
binding, exclusions, output cardinality, and diagnostics.  It declares P2
`Split-Path` input routes, aliases, default/selector parameter sets, conflict
behavior, output kind—including `AotValue.Boolean` for `-IsAbsolute`—and
exclusions.

The plan deliberately narrows the claim to non-resolving lexical transforms.
It does not imply provider, current-location, wildcard, qualifier, credential,
or dynamic/common-parameter compatibility.  All retained source behavior is
either explicit in the static contract or fail-closed with a stable diagnostic.

## Remaining integration condition

This PASS accepts only the design packet.  Implementation must preserve the
declared metadata/binding tables, run the admitted stock-oracle cases and
negative fixtures, and obtain the ordinary implementation reviews before a
runtime support claim.
