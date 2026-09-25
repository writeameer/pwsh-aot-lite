# Cmdlet port timing ledger

This is a lightweight wall-clock history for port slices. It lets later
campaign planning compare scope against elapsed calendar time without claiming
that the duration is staffed effort or a performance benchmark.

Each cmdlet note owns the detailed record. Start the clock when investigation,
design, or implementation of that cmdlet's slice begins. End it only once the
required verification has passed and the verified slice is integrated to
`main`. Times are UTC ISO 8601 with a `Z` suffix; elapsed wall clock is end
minus start and includes review, builds, and waiting. Never infer a missing
historical value. Use `unavailable (not recorded)` instead.

The timing start gate and the anti-NIH gate happen together: before an agent
starts implementation, the linked cmdlet note must contain an initialized
**Upstream reuse evidence matrix** with the command metadata/base-class rows
and any already-known output/format surfaces. It can be expanded during
research, but the timing record cannot be marked integrated until every
implemented field/default column and AOT replacement has a completed row and a
review-ledger PASS. This makes elapsed time comparable to the actual analysis,
reuse, review, and verification work—not just target coding.

| Cmdlet | UTC work started | UTC work ended | Elapsed wall clock | Scope note | State |
| --- | --- | --- | --- | --- | --- |
| `New-Guid` | unavailable (not recorded) | unavailable (not recorded) | unavailable (not recorded) | Wave 2 calibration: default UUID v7 and `-Empty` subset. | integrated |
| `New-TimeSpan` | unavailable (not recorded) | unavailable (not recorded) | unavailable (not recorded) | Wave 2 calibration: direct component-construction subset. | integrated |
| `Start-Sleep` | unavailable (not recorded) | unavailable (not recorded) | unavailable (not recorded) | Wave 2 calibration: `-Milliseconds` / `-ms` subset. | integrated |
| `Get-ChildItem` | 2026-09-22T09:59:17.2280730Z | 2026-09-22T11:09:30.2144590Z | 01:10:12 | Wave 3 macOS-arm64 captured-root direct physical immediate-child/default-`Path` slice. | integrated |
| `Get-Item` | 2026-09-22T22:25:55.0487520Z | 2026-09-22T22:45:15.0000000Z | 00:19:19.9512480 | Wave 3 captured-root direct physical file-or-directory lookup slice; no provider, wildcard, or dynamic-parameter support. | integrated |
| `Test-Path` | 2026-09-22T23:07:18.1558270Z | 2026-09-22T23:14:42.0000000Z | 00:07:23.8441730 | Wave 3 direct physical existence/kind probe; closed Any/Container/Leaf Boolean projection. | integrated |
| `Resolve-Path` | 2026-09-23T07:49:52.4190000Z | 2026-09-23T08:31:39.0000000Z | 00:41:46.5810000 | Wave 3 direct existing physical resolution; closed canonical Path record/static one-column view. | integrated |
| `Convert-Path` | 2026-09-23T08:40:00.0000000Z | 2026-09-23T09:29:55.0000000Z | 00:49:55 | Wave 3 Path-only canonical text projection over the released resolution seam. | integrated |
| `ConvertFrom-Json` | 2026-09-23T13:05:01.6890880Z | — | — | J0 closed JSON-to-`AotValue` foundation is integrated; adapter work has not begun. | queued after J0 |
| `ConvertTo-Json` | 2026-09-23T13:05:01.6890880Z | — | — | J0 closed `AotValue`-to-JSON foundation is integrated; adapter work has not begun. | queued after J0 |
| `Join-Path` | 2026-09-24T19:50:10Z | 2026-09-24T20:06:38Z | 00:16:28 | J1 POSIX-v1 zero-authority lexical composition; verified and merged at `9a279d3`. | integrated |
| `Split-Path` | 2026-09-24T19:50:10Z | 2026-09-24T20:06:38Z | 00:16:28 | J1 POSIX-v1 lexical decomposition/selectors; verified and merged at `9a279d3`. | integrated |
| `Where-Object` | 2026-09-24T19:52:33Z | 2026-09-25T01:02:31Z | 05:09:58 | J2 descriptor-bound numeric record predicate; verified, reviewed, and released at `0927d11`. | integrated |
| `Select-Object` | 2026-09-24T19:52:33Z | 2026-09-25T01:02:31Z | 05:09:58 | J2 descriptor-bound explicit record-field projection; verified, reviewed, and released at `0927d11`. | integrated |

When a port completes, replace its in-progress end and elapsed cells in this
ledger and in its individual note in the same integration change. If a port is
deferred, preserve its end time and say why in the scope note; it remains useful
planning evidence.
