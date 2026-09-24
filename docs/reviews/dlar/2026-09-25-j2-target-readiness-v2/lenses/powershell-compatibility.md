# PowerShell semantic and compatibility lens — v2

**Perspective disclaimer:** an evidence-based design perspective, not real person participation or endorsement.

**Verdict:** **PASS**

## Evidence reviewed

- [J2 readiness v2](../../../../architecture/j2-static-record-transform-target-readiness-v2.md), amendment A, gives each future `Where-Object` and `Select-Object` spelling exactly one descriptor-bound route from the upstream AST and original source atoms/extents to a generated descriptor and record stage.
- The same amendment requires removal of the direct legacy
  `AotFilterTailStagePlan`/`AotProjectionTailStagePlan` dispatch for those two
  names when descriptors are registered. It prohibits legacy-first binding,
  descriptor fallback, and fallback after an error.
- The redirect table assigns source position, admitted structural forms,
  unadmitted source-valid forms, and unknown names to mutually exclusive
  owners with original-source diagnostic spans.
- The 12 redirect fixtures assert one route and one diagnostic route, preserve
  pre-release catalog/count state, and make release the sole count-changing
  point. The 14 other J2 commands remain explicitly deferred.

## Finding P1 — resolved

The v1 blocker was duplicate structural and descriptor ownership. Amendment A
settles precedence before binding: the normal executable registry owns source
position; the generated descriptor owns the two stage spellings; explicit
descriptor policy owns unadmitted forms; the ordinary unknown-command path
owns unknown names. The legacy structural forms lose all post-redirect
ownership. This removes both dual-binding and dual-diagnostic routes without
changing the stated static subset.

## Compatibility checks

- **One command route:** PASS. The replacement rule is mandatory in the same
  implementation change as descriptor registration, so the two old tail plans
  cannot remain executable alternatives for these names.
- **Diagnostic ownership:** PASS. The `AOT6401`–`AOT6406` table assigns
  unsupported and structural cases to descriptor-associated policy; historical
  `AOT4001`–`AOT4008` have no ownership after redirect. Original spans remain
  required.
- **Static, fail-closed surface:** PASS. ScriptBlocks are inspected only to
  form an unsupported descriptor-associated plan; they are neither evaluated
  nor redirected to a dynamic/legacy path. Unadmitted parameters, wildcard,
  calculated, and collection forms remain unavailable.
- **Command accounting:** PASS. Catalog/help/completion and campaign counts do
  not change before lifecycle step 9. The redirect fixtures make the release
  gate the only count-changing point.

## Scope of this verdict

This PASS approves the corrected v2 design only. It does not authorize runtime
code, descriptor registration, a compatibility/support claim, or a migrated
command count. Parser, diagnostics, compatibility, Native AOT, and release
verification remain required for any implementation.
