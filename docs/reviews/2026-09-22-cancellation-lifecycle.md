# Review: Cooperative cancellation and lifecycle

Date: `2026-09-22`
Claim reviewed: `A host-supplied cancellation token cooperatively stops started AOT cmdlet lifecycles exactly once and ScriptRunner returns 130 without creating a runtime diagnostic.`
Upstream commit: `1e53f6bbab4b8791eae782474d21889f9e5d6038`

## Evidence

- Source/provenance: upstream `engine/CommandBase.cs` exposes a pipeline stop
  token and stop check; `engine/pipeline.cs` cancels its pipeline token, calls
  command stop hooks, and suppresses stop-hook failures. The AOT equivalent is
  `AotExecutionContext → AotCmdletBase → ScriptRunner`.
- Build and tests: `dotnet build -c Release --no-restore -v:q`, `dotnet run
  -c Release --no-build -- --self-test`, and `git diff --check` passed. The deterministic fixture
  suite covers a pre-cancelled context, normal lifecycle, cancellation during
  process materialization, exactly-once stop, suppressed stop-hook failure,
  typed input-stage cancellation, transcript retention, and exit code `130`.
- Native AOT evidence: fresh self-contained `osx-arm64` publish of
  `PwshAotLite.csproj`, followed by native `--self-test`, passed.
- Differential parser evidence: N/A; parser and accepted syntax are unchanged.

## Independent verdicts

| Persona | PASS / BLOCK | Evidence-backed finding | Resolution / accepted variance |
| --- | --- | --- | --- |
| Native AOT Boundary Sentinel | **PASS** | The token is host-owned and the runtime uses normal closed BCL cancellation types only; no reflection, dynamic code, or scheduler was introduced. | Cancellation is cooperative rather than preemptive. |
| Static Data-Plane & Binder Guardian | **PASS** | Token checks preserve the existing typed records and invocation/event contracts; a typed input stage is covered by deterministic cancellation tests. | Shape copying is closed/small today; poll it if future adapters make it large. |
| Diagnostic Experience Guardian | **PASS** | Cancellation produces no synthetic error event or diagnostic, preserves prior transcript events, and maps at the host boundary to 130. | Existing terminating-error exit behavior remains unchanged. |
| Compatibility Proof Adversary | **PASS** | Tests prove pre-start, mid-process, stop-hook failure, exactly-once stop, skipped End, transcript retention, and host exit behavior. | Ctrl-C signal wiring, jobs, external-process cancellation, and concurrency are intentionally not claimed. |
| Architecture Guard | **PASS** | Lifecycle materialization and stop policy remain centralized in `AotCmdletBase`; no port-specific cancellation workaround or alternate pipeline path was added. | New long-running shared boundaries must opt into the same cooperative token policy. |

## Outcome

`integrated — cooperative cancellation/lifecycle` — the contract is cooperative
only: it excludes preemptive interruption, terminal signal policy,
native/external process cancellation, jobs, concurrency, parser cancellation,
and ordinary terminating-error cleanup.
