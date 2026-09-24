# Reconciliation and required plan amendments

## Common safe boundary

The two lenses agree on a bounded direction: `Join-Path` and non-resolving
`Split-Path` may eventually be pure direct-physical **path text** transforms.
They are not existing-item resolution, provider navigation, wildcard expansion,
drive/session-state behavior, or a generic filesystem capability.

## Required amendments before converter work

| Amendment | Resolves | Required content | Status |
| --- | --- | --- | --- |
| `A1` PathText policy | `J1-DLAR-01`, `J1-TD-01` | Host dialect, lexical normalization, relative/root/trailing separator rules, empty/whitespace, wildcard/provider/foreign spelling behavior, and no existence check. | open |
| `A2` P1 surface table | `J1-DLAR-02`, `J1-PS-01` | `Path`, `ChildPath`, `AdditionalChildPath`, `Extension`, `Resolve`, aliases, cardinality, and diagnostics. | open |
| `A3` P2 surface table | `J1-DLAR-02`, `J1-TD-02` | Each admitted static alternative, default `Parent`, output kind, `LiteralPath` decision, and all rejected combinations. | open |
| `A4` diagnostics matrix | `J1-DLAR-02`, `J1-TD-03` | Stable IDs, messages, source spans, and help for unsupported capability/switch/set/path text. | open |
| `A5` proof plan | `J1-DLAR-03`, `J1-PS-02` | Parser/binder fixtures, stock-oracle cases, non-existent paths, ordering, and `Join-Path` → `Split-Path` composition. | open |
| `A6` requirement-result schema | `J1-PS-03`, `J1-DLAR-04` | Source identity/hash, required substrate ID, excluded scope, and next approval point for the other twelve commands. | open |

## Re-review rule

When all amendments are in the updated J1 design packet, create a new dated
DLAR package for the revised scope. Do not change this completed `BLOCK`
record or mutate the original survey evidence. Both lenses must pass before
Step 2 may change the converter or generate profiles.
