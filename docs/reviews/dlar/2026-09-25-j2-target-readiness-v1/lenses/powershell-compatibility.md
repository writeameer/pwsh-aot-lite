# PowerShell semantic and compatibility lens — target readiness v1

**Perspective disclaimer:** an evidence-based design perspective, not real
person participation or endorsement.

**Verdict:** **BLOCK**

## Evidence reviewed

- [Target readiness design](../../../../architecture/j2-static-record-transform-target-readiness.md), especially its decision, static contracts, metadata/stage contract, diagnostics, and no-silent-promotion table.
- Pinned upstream `WhereObjectCommand` declaration and behavior in
  `src/System.Management.Automation/engine/InternalCommands.cs:1279-1655`,
  including pipeline `InputObject`, positional `Property`/`Value`, and the
  named operator parameter sets/aliases.
- Pinned upstream `SelectObjectCommand` declaration in
  `src/Microsoft.PowerShell.Commands.Utility/commands/utility/Select-Object.cs:26-77`,
  including pipeline `InputObject`, positional `Property`, and the excluded
  parameter routes.
- Current target at `bfbe5e8`: `UpstreamAstPipelineLowerer.LowerPipeline`
  already recognizes command names `Where-Object` and `Select-Object` as
  structural tail stages; `AotRecordFilterTransform` and
  `AotRecordProjectionTransform` execute them. This is also exposed in the
  target help text and self-tests.

## Finding R1 — unresolved duplicate command path

The proposed adapters say they will use generated command metadata and the
registry binder, while the existing lowerer has a separate command-name branch
which directly parses the same two command names into structural transforms.
The design calls the latter “non-migration evidence,” but does not say whether
it is removed, redirected, or made unreachable before the new adapters are
registered.

That leaves two potential acceptance/binding/diagnostic paths for the same
spelling. They already differ: the legacy lowerer accepts only its positional
structural form and owns its own unsupported diagnostics; the proposed route
admits generated `Property` metadata, exact aliases, and AOT6401–AOT6406.
Without one named dispatch rule, the claimed pipeline-only subset cannot prove
which path handles `Where-Object`/`Select-Object`, nor that existing structural
behavior is not mislabeled as a newly migrated cmdlet.

This prevents a semantic PASS. The source-derived narrowing itself is sound:
the plan explicitly rejects script blocks, dynamic member access, wildcards,
calculated properties, and the other upstream parameter sets rather than
silently emulating them. The blocker is the target's unresolved ownership of
the admitted spellings.

## Smallest corrective action

Amend the readiness design with one mechanical replacement rule:

1. name the legacy `LowerPipeline` branches and state that the implementation
   removes or routes both through the descriptor-owned stage binder before any
   help/catalog registration;
2. specify the single post-source dispatch order: upstream AST atoms →
   generated descriptor/binder for admitted parameters → static stage, with
   `InputObject` supplied only by the typed batch;
3. state the replacement diagnostics for every old structural rejection,
   including omitted `Select-Object -Property`, named `-Property`, source
   aliases, conflicting operators, and legacy direct-source invocation; and
4. add fixtures that prove there is exactly one route for each spelling and no
   J2 migration count/support claim before release.

After that amendment, re-review this lens. No runtime or converter change is
authorized by this verdict.
