# Control-plane implementation notes

The control plane answers discovery, installation, and completion questions
without becoming command execution. It is intentionally separate from the
Native-AOT adapter registry.

| Capability | State | Contract |
| --- | --- | --- |
| `Get-Help` | complete catalog target | generated built-ins plus declarative extensions |
| `Get-Command` | complete catalog target | known commands with visible availability—not a runnable-session claim |
| `Get-Module` | complete catalog inventory | built-in plus installed extension-package records |
| metadata completion | complete static target | [`completion.md`](completion.md) |
| `Find-Module` | local repository-index target | read-only, no transport or install |
| `Install-Module` | local file-package proof | [installation notes](../cmdlets/install-module.md) |

The durable rule is one-way:

```text
repository catalog --read--> Find-Module
                       |
                       +--explicit write--> Install-Module staging/activation
                                                   |
extension package catalog --read--------------------+--> Get-Module/Get-Command/Get-Help/completion
```

Only invocation may execute code. Discovery and installation validation read
declarative data only; installing a legacy extension does not silently import
or activate its code.
