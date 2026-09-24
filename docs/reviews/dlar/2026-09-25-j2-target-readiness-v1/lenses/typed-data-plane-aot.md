# Typed data-plane and Native AOT lens

**Perspective disclaimer:** an evidence-based design perspective, not real
person participation or endorsement.

**Verdict:** **BLOCK**

## Evidence reviewed

- The proposed stage takes and returns only immutable `AotRecordBatch` values;
  its rows are closed `AotRecord`/`AotValue` data. It does not receive an
  arbitrary CLR object, `PSObject`, or a reverse pipeline adapter.
- `AotValue` is a finite tagged union, and the existing J0 `AotJsonCodec`
  already provides a bounded, source-generated JSON representation for those
  values. This is a viable future *payload* basis, not a transport selection.
- The target plan reuses the upstream AST plus generated descriptor binder,
  fixes stages to a source-derived command contract, and forbids `ScriptBlock`,
  reflection, providers, runspaces, and ambient authority.
- The architecture already requires any future extension/sidecar endpoint to
  be selected explicitly by execution endpoint and trust policy. No endpoint
  is implemented or authorized by this J2 packet.

## Finding T1 — transport-neutral seam is implicit, not contractual

The proposed operation is structurally transport-neutral: its command semantics
are a pure record-batch transform. But the readiness packet does not state the
required wire-boundary rule. Without that rule, a future stdio or gRPC adapter
could leak endpoint objects, framing, retries, or transport errors into a
cmdlet adapter and accidentally make the command semantics transport-dependent.

### Smallest correction

Add a **Transport neutrality (preserved seam; not implementation)** subsection
to the J2 target-readiness design with all of these fixed rules:

1. The portable payload is an ordered batch of the existing closed values,
   represented at a future boundary as `AotValue.List` of `AotValue.Record`;
   no `PSObject`, arbitrary CLR value, type name, delegate, or runtime plugin
   crosses it.
2. `Where-Object` and `Select-Object` operate only on `AotRecordBatch`; their
   binding, matching, projection, ordering, atomicity, and diagnostics are
   identical regardless of local, future stdio, or future gRPC origin.
3. A future endpoint is selected at compile time from a closed host
   configuration. It may frame/unframe the payload and map cancellation or
   transport failure at the outer host boundary, but it may not register
   cmdlets, invoke arbitrary code, alter record shapes, or reinterpret command
   parameters.
4. No stdio adapter, gRPC adapter, wire protocol, network capability, process
   launch, retry policy, authentication, or plugin mechanism is implemented or
   implied by J2. Those need their own authority/trust design and review.
5. The J2 implementation proof must include a local boundary fixture showing
   that a batch reconstructed from the closed payload produces the same output
   and diagnostic result as the original batch. This tests the preserved data
   seam only; it is not a transport test.

This correction is documentation and fixture-plan scope only. It neither
broadens J2 nor authorizes a transport implementation.

## Other checks

- **Closed values / object boundary:** passes subject to T1. `AotRecordBatch`
  remains one-way from typed source rows and does not re-enter generic object
  execution.
- **AOT boundary:** passes subject to T1. The plan admits no reflection,
  dynamic engine, runtime code generation, sidecar invocation, or provider
  authority.
- **Diagnostics:** the proposed source-spanned, atomic diagnostic plan is
  compatible with a transport-neutral stage. Endpoint diagnostics must remain
  outside the J2 command contract when a transport is later designed.

T1 is the only blocker. After the stated amendment, rerun this lens; do not
reinterpret this BLOCK as a requirement to implement stdio or gRPC in J2.
