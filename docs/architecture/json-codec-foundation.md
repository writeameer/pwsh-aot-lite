# J0: closed JSON codec foundation

## Status

**Implemented and verified codec foundation — no JSON cmdlet is executable.**
This is the pulled-forward J0 prerequisite for `ConvertFrom-Json`,
`ConvertTo-Json`, and eventually `Test-Json`. The executable contains the
closed codec and its self-tests only; it does not alter the PowerShell parser,
binder, executable registry, host substrate, or terminal presentation. Both
JSON command names remain deliberately unregistered (`AOT2001`).

The source declarations remain catalogued-only. This document is not a
compatibility claim and must not be used to register a cmdlet before the review
ledger records PASS verdicts and the focused managed/native evidence exists.

## Upstream evidence and non-transferable dependencies

- `ConvertFromJsonCommand` buffers `InputObject` in `ProcessRecord`, decides
  between separate documents and newline-joined pipeline content in
  `EndProcessing`, and calls `JsonObject.ConvertFromJson` before `WriteObject`:
  `../../.upstream/PowerShell/src/Microsoft.PowerShell.Commands.Utility/commands/utility/WebCmdlet/ConvertFromJsonCommand.cs:20-136`.
- `ConvertToJsonCommand` buffers arbitrary `object` values, forms an array for
  multiple input values or `-AsArray`, and calls the Newtonsoft/PowerShell
  preprocessing path in `JsonObject.ConvertToJson`:
  `../../.upstream/PowerShell/src/Microsoft.PowerShell.Commands.Utility/commands/utility/WebCmdlet/ConvertToJsonCommand.cs:20-144` and `JsonObject.cs:25-510`.
- `JsonObject` relies on Newtonsoft and PowerShell object/ETS conversion. The
  full non-copyable source closure is `JsonObject.cs:169-466` for decode and
  `JsonObject.cs:435-846` for encode: `ProcessValue`/`ProcessCustomObject`
  unwrap `PSObject`, inspect arbitrary CLR types and public members, invoke
  getters, traverse dictionaries/enumerables, and finally call
  `JsonConvert.SerializeObject`. Those dependencies cannot cross into the
  Native AOT executable. `PSObject`, arbitrary CLR object traversal,
  reflection, and runtime serialization are expressly out of scope.

## Proposed reusable seam

`AotJsonCodec` is a pure, static, `System.Text.Json`-based data-plane service.
It receives only closed `AotValue` values and a fixed, explicit limits record;
it has no host capability, provider, session-state, credential, network, or
module dependency. It uses a hand-written recursive `Utf8JsonReader` /
`Utf8JsonWriter` traversal, not `JsonSerializer`, `JsonNode`, or a serializer
adapter.

```text
UTF-8 JSON bytes --parse with fixed limits--> AotValue
AotValue           --write with fixed policy--> UTF-8 JSON bytes
```

The exact J0 API is:

```csharp
AotValue Decode(ReadOnlySpan<byte> utf8, AotJsonReadLimits limits,
                AotSourceSpan? inputSpan, CancellationToken token);

byte[] Encode(AotValue value, AotJsonWriteLimits limits,
              AotSourceSpan? inputSpan, CancellationToken token);
```

The initial immutable limits are deliberately small and do not reuse source
`-Depth` parameters: read input at most **1 MiB**, write output at most **1
MiB**, reader nesting at most **16**, at most **32,768** total values, at most
**4,096** properties per object or values per array, and at most **65,536 UTF-8
bytes** per decoded/encoded string or property name. These align with the
existing bounded data-only repository-index intake (1 MiB/16 levels) while
placing additional collection limits before a general-purpose codec is exposed.
Every limit is checked during traversal before an unbounded materialization or
write can occur. The writer may retain a bounded 256-byte implementation
scratch segment even when a deliberately smaller logical output limit is
selected for a test, but the logical limit is enforced on every committed byte
advance; thus an exact four-byte `null` is valid under a four-byte output
budget and no emitted JSON can exceed that budget.

The codec may use only these existing cases:

| JSON token | Closed result |
| --- | --- |
| `null`, Boolean | `AotValue.Null`, `Boolean` |
| integer | `Integer` when it fits signed Int64 |
| decimal number | `Decimal` when it is representable exactly |
| floating number | finite `FloatingPoint` only when neither `Int64` nor `Decimal` represents its JSON token |
| string | `String` |
| array | immutable `List` |
| object | ordered immutable `AotRecord` |

Object property order is retained. Duplicate property names, including names
that differ only by case, are rejected because `AotRecord` has a
case-insensitive field contract. Date coercion, byte/string conventions,
arbitrary object member discovery, dictionaries, enums, `PSObject`, hashtable
identity, and CLR serialization are not codec behavior.

This duplicate-key policy is a deliberate bounded variance, not an inferred
PowerShell behavior. A controlled stock `pwsh` 7.6.6 probe rejects
`{"Name":1,"name":2}` by default and directs the caller to `-AsHashtable`,
but accepts exact duplicate `{"Name":1,"Name":2}` with the latter value;
J0 rejects both because an immutable record cannot retain duplicate fields.
An empty name is likewise rejected by stock's default object route. In
contrast, stock accepts a whitespace-only property name; J0 deliberately
rejects it as **AOT6304** because `AotRecord` treats whitespace-only names as
unsafe. That is a named fail-closed compatibility variance, not a claim that
stock has the same validation. `-AsHashtable` remains unsupported. Any
future admitted cmdlet surface must retain raw stock/native oracle output for
exact duplicates, case collisions, nested collisions, empty keys, and
whitespace-only keys; that oracle must demonstrate the stock success versus
native AOT6304 rejection rather than normalize it away.

The decoder accepts exactly one JSON root; comments, trailing commas, and a
second/trailing token are rejected. It checks the caller-owned cancellation
token before and after conversion and during every value/property/item loop.
Host cancellation propagates the original `OperationCanceledException` to the
existing `ScriptRunner` cancellation path (exit 130), rather than becoming a
raw library exception or a user-visible JSON error. The codec never owns a
`CancellationTokenSource`.

`DateTime` and `Bytes` are existing AOT value cases but are **not JSON-native
J0 cases**. Encoding either is rejected; no implicit string or Base64 mapping
is permitted. Explicit mappings, if ever needed, require a later design,
variance, oracle, and review.

## Planned narrow command adapters after J0 review

`ConvertFrom-Json` may initially admit only generated `InputObject` text and
emit closed values through an explicit pipeline-record boundary. `AsHashtable`,
`DateKind`, `NoEnumerate`, multi-input newline heuristics, and the source
`PSObject` materialization remain deferred until each has a reviewed static
meaning.

`ConvertTo-Json` may initially accept only an already-closed `AotValue`/record
shape through an explicit registered adapter. It must not accept its upstream
`object` parameter merely because metadata declares it. `AsArray`, `Compress`,
`Depth`, `EnumsAsStrings`, and `EscapeHandling` need their own generated
parameter opt-in and stock-PowerShell oracle before being admitted.

No generic object-to-JSON fallback, no JSON-specific command binder, and no
PowerShell grammar change is permitted. The existing upstream AST lowerer must
continue to produce command atoms and the existing generated-metadata binder
must continue to select parameters.

## Diagnostics and verification plan

The codec must convert all non-cancellation faults to typed `AotDiagnostic`
records at the adapter boundary. The command token/value span is retained;
parser offsets inside a JSON string may appear only as safe detail/notes, never
as a fabricated PowerShell source span. J0 reserves these stable IDs before
implementation:

| ID | Meaning |
| --- | --- |
| `AOT6301` | malformed JSON, including comments, trailing commas, or trailing content |
| `AOT6302` | decode input/structure limit exceeded |
| `AOT6303` | encode output/structure limit exceeded |
| `AOT6304` | blank, whitespace, duplicate, or case-colliding object property name |
| `AOT6305` | JSON number cannot be represented by the closed numeric domain |
| `AOT6306` | an AOT value kind has no admitted JSON representation |
| `AOT6307` | reserved cancellation outcome; host-owned cancellation propagates unchanged and produces no ordinary diagnostic |

`JsonException` and all codec-library exceptions must be contained behind the
first six diagnostic IDs. Plain and ANSI snapshot tests must prove the command
argument span and safe detail rendering.

J0 implementation evidence (not a cmdlet-support claim): managed Release
build and self-test, parser guard and 36-fixture differential baseline,
campaign/queue verification, and fresh `osx-arm64` Native AOT self-test all
passed on the recorded J0 artifact in the review ledger. Grammar fixture
`tests/grammar/fixtures/36-json-key-boundaries.ps1` and its checked-in stock
baseline preserve the upstream command-string boundary for the later JSON-key
oracle; they do not execute a JSON cmdlet.

Required proof before a command adapter can be supported:

1. upstream-generated descriptors prove every admitted parameter;
2. managed invariants cover nesting, ordering, case-insensitive duplicate keys,
   numeric boundaries, cancellation, malformed input, unsupported value kinds,
   and empty/whitespace-only/duplicate/case-colliding property names;
3. stock `pwsh` versus fresh native controlled-fixture oracles cover every
   admitted input/output surface and documented normalization;
4. parser reuse guard and baseline verification remain unchanged; and
5. a fresh Native AOT publish runs the focused codec/cmdlet smoke tests.

## Explicit non-goals

This J0 foundation does not implement a JSON provider, `Invoke-RestMethod`,
JSON schema, `Test-Json`, arbitrary object serialization, or PowerShell's full
JSON compatibility surface. It is deliberately a reusable closed-value codec,
not an alternate dynamic execution engine.
