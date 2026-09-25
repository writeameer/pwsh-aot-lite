# J2 remaining-command Layer A results

**Run start:** `2026-09-25T07:50:03Z`  
**Run end:** `2026-09-25T07:53:52.2577400Z`

This is the bounded command-viability screen for the 14 J2 commands that are
not already released adapters. Each row uses stock PowerShell and an existing
self-test-passing Native AOT executable only. No row created code, a manifest,
a build, a publish, a profile, or a migration claim.

| Family / elapsed | Commands | Stock bounded form | Native result | Final fast-cycle outcome |
| --- | --- | --- | --- | --- |
| shared base / 1.561 s | `Get-Random`, `Get-SecureRandom` | seeded numeric random and bounded secure random accepted; secure `SetSeed` rejected | all forms: `AOT2001 Unsupported source command` | **Layer A boundary:** no native route; B/C ineligible |
| dynamic script / 1.206 s | `ForEach-Object`, `Measure-Command` | bounded scriptblock forms accepted | `AOT1001`: pipeline/scriptblock expression is parsed but not executable | **Layer A boundary:** no static scriptblock route; B/C ineligible |
| typed property/order / 2.264 s | `Compare-Object`, `Get-Unique`, `Group-Object`, `Join-String`, `Measure-Object`, `Sort-Object` | literal-pipeline forms accepted | `AOT1001`: literal pipeline or existing structural-projection limit | **Layer A boundary:** no concrete closed native route; B/C ineligible |
| ETS member schema / 0.858 s | `Add-Member`, `Get-Member` | bounded object/member forms accepted | `AOT1001`: non-executable conversion expression / `Get-Member` stage | **Layer A boundary:** no closed object-adaptation or member route; B/C ineligible |
| external authority / 0.881 s | `Select-String`, `Tee-Object` | non-writing pipeline forms accepted | `AOT1001`: literal pipeline is not executable | **Layer A boundary:** no pipeline/authority route; B/C ineligible |

The total command-observation time was 6.770 seconds across five separate
under-five-second family probes. A family result is shared only where its
retained source facts identify the same family; it is not an inferred command
implementation.

These outcomes agree with the independently retained J2 target-readiness
matrix in
[j2-static-record-transform-target-readiness.md](../architecture/j2-static-record-transform-target-readiness.md):
the commands need named static contracts for script execution, typed-property
semantics, object/member schema, random authority, or text/file authority.
They are explicit current-boundary outcomes, not environmental build failures
and not permanent impossibility claims.

The earlier disposable proof linker failure is recorded separately as invalid
environment preflight; it is excluded from this command evidence and these
timings.
