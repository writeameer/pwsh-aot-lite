# J2 target-readiness evidence and provenance

## Source pins and retained facts

Upstream checkout: `/Users/ameerdeen/progs/PowerShell` at
`1e53f6bbab4b8791eae782474d21889f9e5d6038`.

| Artifact | Immutable location | SHA-256 |
| --- | --- | --- |
| accepted J2 v2 plan | [plan](../../../../../pwsh-aot-conversion-survey/analysis/j2-conversion-plan-v2/plan.json) | `1f1a18d3878c76eb560309f94a8393ee4b1d11f23c7d15252d5a6860c5932a55` |
| J2 source-evidence index | [index](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/index.json) | `ab0244ff27fb3cc94d8ffcb65eefd8210172ff43369e3526e9796b868ec391a8` |
| J2 conversion report | [report](../../../../../pwsh-aot-conversion-survey/runs/j2-conversion-run-v2/report.json) | native extractor `3e7edfa5e0a731957c9a4da4b54b1ad41236cd7b7b63537b47ba2c9f7e8f99c7` |

| Command | Source SHA-256 | Retained source record |
| --- | --- | --- |
| `Add-Member` | `7ebe9a7a9f3c90698743ece1e38964cd3ca4680f7d9f796938171306d64e6195` | [record](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/add-member.json) |
| `Compare-Object` | `ef470f0cb7b5a568a067b6a000a18290822c5aff0f93c955d27445d8729fd846` | [record](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/compare-object.json) |
| `ForEach-Object` / `Where-Object` | `244a2d8805df96484ee03a0ac1fcfc6c1b5a2904ea708982a388929b552e9def` | [ForEach](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/foreach-object.json), [Where](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/where-object.json) |
| `Get-Member` | `1d013a87d79e451729f55f0482b037f40444469a62c780e22a367e75c2ba5f0e` | [record](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/get-member.json) |
| `Get-Random` | `596827bc4375123ccb51c58db4a8d7f6c15058b1f10432b6fec18279035c71aa` | [record](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/get-random.json) |
| `Get-SecureRandom` | `f41ded71516e27818180d5efad5b474c67d5caeff230c465d152a509d3767903` | [record](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/get-securerandom.json) |
| `Get-Unique` | `e439330d0e60d9bf30bc99ecdf687c7b0fc5611070cd83fcaf17606c802c9e9d` | [record](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/get-unique.json) |
| `Group-Object` | `39097c459aa3b7efe03405223254b7c078f1c89cc8bea4cac483ea10a90b5fca` | [record](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/group-object.json) |
| `Join-String` | `f865bfca2420a11f4aab676d06ca2c202933562c5fcc62f3886fa8c345023637` | [record](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/join-string.json) |
| `Measure-Command` | `7014733fbffa4d59780d858a8b082bdd03604abcf7a94ff2120a512cc2479d7c` | [record](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/measure-command.json) |
| `Measure-Object` | `084250173af268a9dcdbababc0c2a401ba85aeb7c7078a6392e579acdfa8e0a0` | [record](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/measure-object.json) |
| `Select-Object` | `d3417a1b761ad82a4f910640f80a2a7b444072e3a4b2aae747ac1bece83fbf0f` | [record](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/select-object.json) |
| `Select-String` | `ec5eaaa5daacae8c48558bf94e13830e1a22aaffd92bf70c269e5f2b48a0cc20` | [record](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/select-string.json) |
| `Sort-Object` | `f76a650139200402a4ef472d1ffd5d1b7457a2f24f47fed54f892c4cd1329f32` | [record](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/sort-object.json) |
| `Tee-Object` | `f5d3df7c8feb3ea816c2bcf6e9a03702693c730ff54a423cb841a6f5a850f752` | [record](../../../../../pwsh-aot-conversion-survey/evidence/materialized/j2-source-facts-v1/cmdlets/tee-object.json) |

## Target seams inspected

`AotValue.cs`, `AotRecordBatch.cs`, `PipelineValueAdapter.cs`, `Pipeline.cs`,
`AotLanguageCore.cs`, `UpstreamAstPipelineLowerer.cs`, `AotDiagnostics.cs`,
and `HelpCatalog.cs`. No host, provider, filesystem, process, credential, or
network capability is reusable or required for this zero-authority slice.
