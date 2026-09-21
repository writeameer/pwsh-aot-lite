[CmdletBinding(DefaultParameterSetName = 'Register')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Register')]
    [Parameter(Mandatory, ParameterSetName = 'Inspect')]
    [string] $Name,

    [string] $ModulePath,

    [ValidateSet('Register', 'Inspect', 'DeclarationOnly')]
    [string] $Mode = 'Register',

    [string] $ExtensionsRoot = (Join-Path $PSScriptRoot '..' 'extensions'),

    [string] $InspectionOutput,

    # A declaration-only package intentionally does not import the target
    # module. This optional field preserves the result of an earlier explicit
    # full-inspection attempt without causing that attempt to be repeated.
    [string] $PreviousImportFailure
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SafePathSegment {
    param([Parameter(Mandatory)][string] $Value)

    # Module identities are allowed to contain dots.  Everything else becomes
    # a dash so the package location is stable on every supported host.
    return ($Value -replace '[^A-Za-z0-9._-]', '-')
}

function Get-TextValues {
    param($Value)

    if ($null -eq $Value) { return @() }

    return @($Value | ForEach-Object {
        if ($_ -is [string]) { $_ }
        elseif ($null -ne $_.Text) { [string] $_.Text }
        else { [string] $_ }
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-ObjectPropertyValue {
    param(
        $Object,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Resolve-ModuleCandidate {
    $candidate = if ($ModulePath) {
        Get-Item -LiteralPath $ModulePath
    }
    else {
        Get-Module -ListAvailable -Name $Name |
            Sort-Object Version -Descending |
            Select-Object -First 1
    }

    if ($null -eq $candidate) {
        throw "Module '$Name' was not found. Supply -ModulePath or install it into PSModulePath."
    }

    return $candidate
}

function Get-StaticAstValue {
    param($Ast)

    if ($Ast -is [System.Management.Automation.Language.StringConstantExpressionAst]) { return $Ast.Value }
    if ($Ast -is [System.Management.Automation.Language.ConstantExpressionAst]) { return $Ast.Value }
    return $null
}

function Get-DeclarationParameterContract {
    param([Parameter(Mandatory)] $Parameter)

    $aliases = [System.Collections.Generic.List[string]]::new()
    $parameterSets = [System.Collections.Generic.List[object]]::new()
    foreach ($attribute in $Parameter.Attributes) {
        $attributeName = $attribute.TypeName.FullName
        if ($attributeName -eq 'Alias') {
            foreach ($argument in $attribute.PositionalArguments) {
                $value = Get-StaticAstValue $argument
                if ($null -ne $value) { $aliases.Add([string] $value) }
            }
            continue
        }
        if ($attributeName -ne 'Parameter') { continue }

        $values = @{}
        foreach ($named in $attribute.NamedArguments) {
            $value = Get-StaticAstValue $named.Argument
            if ($null -ne $value) { $values[$named.ArgumentName] = $value }
        }
        $position = if ($values.ContainsKey('Position')) { [int] $values.Position } else { $null }
        $parameterSets.Add([ordered]@{
            name = if ($values.ContainsKey('ParameterSetName')) { [string] $values.ParameterSetName } else { '__AllParameterSets' }
            position = $position
            mandatory = [bool] ($values.Mandatory -eq $true)
            valueFromPipeline = [bool] ($values.ValueFromPipeline -eq $true)
            valueFromPipelineByPropertyName = [bool] ($values.ValueFromPipelineByPropertyName -eq $true)
            valueFromRemainingArguments = [bool] ($values.ValueFromRemainingArguments -eq $true)
        })
    }
    if ($parameterSets.Count -eq 0) {
        $parameterSets.Add([ordered]@{
            name = '__AllParameterSets'; position = $null; mandatory = $false
            valueFromPipeline = $false; valueFromPipelineByPropertyName = $false; valueFromRemainingArguments = $false
        })
    }

    $typeName = if ($Parameter.StaticType) { $Parameter.StaticType.FullName } else { 'System.Object' }
    [ordered]@{
        name = $Parameter.Name.VariablePath.UserPath
        type = $typeName
        aliases = @($aliases)
        isSwitch = $typeName -eq [System.Management.Automation.SwitchParameter].FullName
        isDynamic = $false
        parameterSets = @($parameterSets)
        validation = [ordered]@{ validateSet = @(); validatePattern = @() }
    }
}

function Get-DeclarationOnlyInspection {
    # This is deliberately a parser, not a partial importer: it reads the
    # constrained module manifest and PowerShell AST only. It never executes
    # the .psm1, function bodies, class declarations, or external commands.
    $candidate = Resolve-ModuleCandidate
    $manifest = Import-PowerShellDataFile -LiteralPath $candidate.Path
    $moduleBase = Split-Path -Parent $candidate.Path
    $rootModule = [string] $manifest.RootModule
    if ([string]::IsNullOrWhiteSpace($rootModule) -or -not $rootModule.EndsWith('.psm1', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Declaration-only inspection supports script modules with a .psm1 RootModule; '$($candidate.Path)' declares '$rootModule'."
    }
    $scriptPath = Join-Path $moduleBase $rootModule
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "The script RootModule '$scriptPath' was not found."
    }

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref] $tokens, [ref] $parseErrors)
    if ($parseErrors.Count -gt 0) {
        throw "The RootModule could not be parsed without execution: $($parseErrors[0].Message)"
    }
    $functionsByName = @{}
    foreach ($function in $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) {
        if (-not $functionsByName.ContainsKey($function.Name)) { $functionsByName[$function.Name] = $function }
    }
    $declaredNames = @($manifest.FunctionsToExport | Where-Object { $_ -and $_ -ne '*' })
    if ($declaredNames.Count -eq 0 -and @($manifest.FunctionsToExport) -contains '*') {
        $declaredNames = @($functionsByName.Keys | Sort-Object)
    }

    $commands = foreach ($functionName in $declaredNames | Sort-Object -Unique) {
        $function = $functionsByName[$functionName]
        if ($null -eq $function) {
            # A manifest can export a dynamically created function. Retain the
            # name but never invent a contract that the static AST cannot see.
            [ordered]@{
                name = $functionName; commandType = 'Function'; moduleName = $Name; source = $candidate.Path
                definitionKind = 'declaration-only-dynamic-or-unresolved'; supportsShouldProcess = $false
                defaultParameterSet = $null; parameterSets = @(); parameters = @(); outputTypes = @()
                help = [ordered]@{ synopsis = 'Declared by module manifest; no static function definition was available.'; description = ''; notes = 'Static declaration-only registration cannot inspect dynamically generated exports.'; examples = @(); relatedLinks = @(); helpUri = $null; sourceKind = 'manifest-declaration-only' }
            }
            continue
        }
        $parameterAsts = if ($function.Body.ParamBlock) { @($function.Body.ParamBlock.Parameters) } else { @() }
        $parameters = @($parameterAsts | ForEach-Object { Get-DeclarationParameterContract $_ })
        $helpContent = $function.GetHelpContent()
        [ordered]@{
            name = $function.Name; commandType = 'Function'; moduleName = $Name; source = $scriptPath
            definitionKind = 'declaration-only-script-function'; supportsShouldProcess = [bool] @($(if ($function.Body.ParamBlock) { $function.Body.ParamBlock.Attributes } else { @() }) | Where-Object { $_.TypeName.FullName -eq 'CmdletBinding' }).Count
            defaultParameterSet = '__AllParameterSets'; parameterSets = @([ordered]@{ name = '__AllParameterSets'; isDefault = $true; parameters = @($parameters | ForEach-Object name) })
            parameters = $parameters; outputTypes = @()
            help = [ordered]@{
                synopsis = [string] $helpContent.Synopsis; description = (@(Get-TextValues $helpContent.Description) -join [Environment]::NewLine)
                notes = (@(Get-TextValues $helpContent.Notes) -join [Environment]::NewLine); examples = @(); relatedLinks = @(); helpUri = $null; sourceKind = 'comment-based-static-ast'
            }
        }
    }

    $sourceText = Get-Content -LiteralPath $scriptPath -Raw
    $staticRisks = [System.Collections.Generic.List[object]]::new()
    if ($sourceText -match '(?im)(?:kubectl|&\s*\$\{?executable\}?)') { $staticRisks.Add([ordered]@{ kind = 'external-executable'; detail = 'Source statically references kubectl via the module executable variable.' }) }
    if ($sourceText -match '(?im)\[scriptblock\]::create') { $staticRisks.Add([ordered]@{ kind = 'runtime-code-generation'; detail = 'Source constructs script blocks at runtime.' }) }
    if ($sourceText -match '(?im)(?:update-formatdata|generated\.format\.ps1xml)') { $staticRisks.Add([ordered]@{ kind = 'format-data-side-effect'; detail = 'Source generates or updates PowerShell format data during import.' }) }

    $requiredModules = if ($manifest.ContainsKey('RequiredModules')) { @($manifest.RequiredModules | ForEach-Object { if ($_ -is [string]) { $_ } else { $_.ModuleName } }) } else { @() }
    [ordered]@{
        schema = 'https://pwsh-aot-lite.dev/schemas/module-inspection/v1'; inspectedAtUtc = [DateTime]::UtcNow.ToString('O')
        module = [ordered]@{
            name = $Name; version = [string] $manifest.ModuleVersion; moduleType = 'Script'; path = $candidate.Path; rootModule = $rootModule
            guid = [string] $manifest.GUID; author = [string] $manifest.Author; companyName = [string] $manifest.CompanyName; description = [string] $manifest.Description
            requiredModules = $requiredModules
        }
        commands = @($commands); declarationOnly = $true; staticAnalysis = @($staticRisks)
    }
}

function Write-DeclarationOnlyPackage {
    param([Parameter(Mandatory)] $Inspection)

    $safeName = Get-SafePathSegment $Inspection.module.name
    $safeVersion = Get-SafePathSegment $Inspection.module.version
    $resolvedExtensionsRoot = Resolve-Path -LiteralPath $ExtensionsRoot -ErrorAction SilentlyContinue
    $extensionsBasePath = if ($resolvedExtensionsRoot) { $resolvedExtensionsRoot.Path } else { $ExtensionsRoot }
    $packagePath = Join-Path (Join-Path $extensionsBasePath $safeName) $safeVersion
    New-Item -ItemType Directory -Path $packagePath -Force | Out-Null
    $reason = 'Declaration-only static registration. The module was not imported, no exported function was invoked, and this package has no invocation bridge.'
    $extension = [ordered]@{
        schema = 'https://pwsh-aot-lite.dev/schemas/extension/v1'; schemaVersion = 1
        extension = [ordered]@{ id = "legacy-pwsh/$($Inspection.module.name)"; displayName = $Inspection.module.name; version = $Inspection.module.version
            execution = [ordered]@{ kind = 'declaration-only'; status = 'blocked-not-imported'; reason = $reason } }
        commands = @($Inspection.commands | ForEach-Object { [ordered]@{ name = $_.name; commandType = $_.commandType; defaultParameterSet = $_.defaultParameterSet; parameters = $_.parameters; parameterSets = $_.parameterSets; outputTypes = $_.outputTypes; helpUri = $_.help.helpUri } })
    }
    $help = [ordered]@{
        schema = 'https://pwsh-aot-lite.dev/schemas/help/v1'; schemaVersion = 1; module = [ordered]@{ name = $Inspection.module.name; version = $Inspection.module.version }
        commands = @($Inspection.commands | ForEach-Object { [ordered]@{ name = $_.name; synopsis = $_.help.synopsis; description = $_.help.description; notes = $_.help.notes; examples = $_.help.examples; relatedLinks = $_.help.relatedLinks; helpUri = $_.help.helpUri; sourceKind = $_.help.sourceKind } })
    }
    $provenance = [ordered]@{
        schema = 'https://pwsh-aot-lite.dev/schemas/provenance/v1'; registration = [ordered]@{
            registeredAtUtc = [DateTime]::UtcNow.ToString('O'); registrar = 'Register-PwshAotModule.ps1'; method = 'declaration-only-static-ast'
            moduleWasImportedInIsolatedSidecar = $false; exportedCmdletsWereInvoked = $false
            import = [ordered]@{ state = if ($PreviousImportFailure) { 'previous-attempt-failed; not-retried' } else { 'not-attempted' }; failure = $PreviousImportFailure }
        }; sourceModule = $Inspection.module; staticAnalysis = $Inspection.staticAnalysis
    }
    $extension | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $packagePath 'extension.json') -Encoding utf8NoBOM
    $help | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $packagePath 'help.json') -Encoding utf8NoBOM
    $provenance | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $packagePath 'provenance.json') -Encoding utf8NoBOM
    Write-Output $packagePath
}

function ConvertTo-ParameterContract {
    param([Parameter(Mandatory)] $Parameter)

    $parameterAttributes = @($Parameter.Attributes | Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] })
    $validateSet = @($Parameter.Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] } |
        ForEach-Object { @($_.ValidValues) })
    $validatePattern = @($Parameter.Attributes | Where-Object { $_ -is [System.Management.Automation.ValidatePatternAttribute] } |
        ForEach-Object { $_.RegexPattern })

    [ordered]@{
        name = $Parameter.Name
        type = $Parameter.ParameterType.FullName
        aliases = @($Parameter.Aliases)
        isSwitch = $Parameter.ParameterType -eq [System.Management.Automation.SwitchParameter]
        isDynamic = [bool] $Parameter.IsDynamic
        parameterSets = @($parameterAttributes | ForEach-Object {
            [ordered]@{
                name = $_.ParameterSetName
                position = $_.Position
                mandatory = [bool] $_.Mandatory
                valueFromPipeline = [bool] $_.ValueFromPipeline
                valueFromPipelineByPropertyName = [bool] $_.ValueFromPipelineByPropertyName
                valueFromRemainingArguments = [bool] $_.ValueFromRemainingArguments
            }
        })
        validation = [ordered]@{
            validateSet = @($validateSet)
            validatePattern = @($validatePattern)
        }
    }
}

function ConvertTo-CommandContract {
    param([Parameter(Mandatory)] $Command)

    $parameterContracts = @($Command.Parameters.Values |
        Sort-Object Name |
        ForEach-Object { ConvertTo-ParameterContract -Parameter $_ })

    $parameterSetContracts = @($Command.ParameterSets | Sort-Object Name | ForEach-Object {
        [ordered]@{
            name = $_.Name
            isDefault = [bool] $_.IsDefault
            parameters = @($_.Parameters | Sort-Object Name | ForEach-Object { $_.Name })
        }
    })

    $help = Get-Help -Name $Command.Name -Full -ErrorAction Stop
    $helpExamples = Get-ObjectPropertyValue -Object $help -Name 'Examples'
    $exampleItems = if ($helpExamples -is [string] -or $null -eq $helpExamples) {
        @()
    }
    else {
        @($(Get-ObjectPropertyValue -Object $helpExamples -Name 'Example'))
    }
    $examples = @($exampleItems | ForEach-Object {
        [ordered]@{
            title = [string] (Get-ObjectPropertyValue -Object $_ -Name 'Title')
            code = (@(Get-TextValues (Get-ObjectPropertyValue -Object $_ -Name 'Code')) -join [Environment]::NewLine)
            remarks = (@(Get-TextValues (Get-ObjectPropertyValue -Object $_ -Name 'Remarks')) -join [Environment]::NewLine)
        }
    })

    [ordered]@{
        name = $Command.Name
        commandType = [string] $Command.CommandType
        moduleName = $Command.ModuleName
        source = $Command.Source
        # Compiled CmdletInfo objects do not expose ScriptBlock. Access it
        # defensively because this inspector must support both script and
        # binary modules under StrictMode.
        definitionKind = if (Get-ObjectPropertyValue -Object $Command -Name 'ScriptBlock') { 'script-function' } else { 'compiled-command' }
        supportsShouldProcess = [bool] (Get-ObjectPropertyValue -Object $Command -Name 'SupportsShouldProcess')
        defaultParameterSet = Get-ObjectPropertyValue -Object $Command -Name 'DefaultParameterSet'
        parameterSets = $parameterSetContracts
        parameters = $parameterContracts
        outputTypes = @($(Get-ObjectPropertyValue -Object $Command -Name 'OutputType') |
            ForEach-Object { Get-ObjectPropertyValue -Object $_ -Name 'Type' } |
            ForEach-Object { if ($_) { $_.FullName } } | Where-Object { $_ })
        help = [ordered]@{
            synopsis = [string] $help.Synopsis
            description = (@(Get-TextValues (Get-ObjectPropertyValue -Object $help -Name 'Description')) -join [Environment]::NewLine)
            notes = (@(Get-TextValues (Get-ObjectPropertyValue -Object $help -Name 'Notes')) -join [Environment]::NewLine)
            examples = $examples
            relatedLinks = @($(Get-ObjectPropertyValue -Object (Get-ObjectPropertyValue -Object $help -Name 'RelatedLinks') -Name 'NavigationLink') |
                ForEach-Object { Get-ObjectPropertyValue -Object $_ -Name 'Uri' } | Where-Object { $_ })
            helpUri = if (Get-ObjectPropertyValue -Object $Command -Name 'HelpUri') {
                [string] (Get-ObjectPropertyValue -Object $Command -Name 'HelpUri')
            } else { $null }
            sourceKind = if ($help.PSObject.Properties['category']) { [string] $help.category } else { 'generated-baseline' }
        }
    }
}

function Invoke-Inspection {
    $candidate = if ($ModulePath) {
        Get-Item -LiteralPath $ModulePath
    }
    else {
        Get-Module -ListAvailable -Name $Name |
            Sort-Object Version -Descending |
            Select-Object -First 1
    }

    if ($null -eq $candidate) {
        throw "Module '$Name' was not found. Supply -ModulePath or install it into PSModulePath."
    }

    # This function is always run by a fresh `pwsh -NoProfile` child when
    # registering. Importing is therefore an intentional, isolated trust
    # boundary; the target cmdlets are never invoked.
    $module = Import-Module -Name $candidate.Path -PassThru -Force -ErrorAction Stop |
        Where-Object Name -eq $Name |
        Select-Object -First 1
    if ($null -eq $module) {
        $module = Get-Module -Name $Name | Select-Object -First 1
    }
    if ($null -eq $module) {
        throw "Import succeeded but did not expose requested module '$Name'."
    }

    $commands = @($module.ExportedCommands.Values |
        Sort-Object Name |
        ForEach-Object { ConvertTo-CommandContract -Command $_ })

    [ordered]@{
        schema = 'https://pwsh-aot-lite.dev/schemas/module-inspection/v1'
        inspectedAtUtc = [DateTime]::UtcNow.ToString('O')
        module = [ordered]@{
            name = $module.Name
            version = $module.Version.ToString()
            moduleType = [string] $module.ModuleType
            path = $module.Path
            rootModule = [string] $module.RootModule
            guid = [string] $module.Guid
            author = [string] $module.Author
            companyName = [string] $module.CompanyName
            description = [string] $module.Description
            requiredModules = @($module.RequiredModules | ForEach-Object { $_.Name })
        }
        commands = $commands
    }
}

if ($Mode -eq 'Inspect') {
    if ([string]::IsNullOrWhiteSpace($InspectionOutput)) {
        throw '-InspectionOutput is required in Inspect mode.'
    }
    $inspection = Invoke-Inspection
    $inspection | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $InspectionOutput -Encoding utf8NoBOM
    return
}

if ($Mode -eq 'DeclarationOnly') {
    # Do not route this mode through Invoke-Inspection or its child process.
    # This branch is the safe fallback for modules whose import initializer has
    # side effects or requires an unavailable external environment.
    $declaration = Get-DeclarationOnlyInspection
    Write-DeclarationOnlyPackage -Inspection $declaration
    return
}

$pwsh = Get-Command pwsh -CommandType Application -ErrorAction Stop | Select-Object -First 1
$inspectionFile = [System.IO.Path]::GetTempFileName()
try {
    $childArguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $PSCommandPath,
        '-Mode', 'Inspect', '-Name', $Name, '-InspectionOutput', $inspectionFile)
    if ($ModulePath) {
        $childArguments += @('-ModulePath', $ModulePath)
    }

    $child = Start-Process -FilePath $pwsh.Source -ArgumentList $childArguments -Wait -PassThru -NoNewWindow
    if ($child.ExitCode -ne 0) {
        throw "The isolated pwsh inspector failed with exit code $($child.ExitCode)."
    }

    $inspection = Get-Content -LiteralPath $inspectionFile -Raw | ConvertFrom-Json -Depth 30
    $safeName = Get-SafePathSegment $inspection.module.name
    $safeVersion = Get-SafePathSegment $inspection.module.version
    $resolvedExtensionsRoot = Resolve-Path -LiteralPath $ExtensionsRoot -ErrorAction SilentlyContinue
    $extensionsBasePath = if ($resolvedExtensionsRoot) { $resolvedExtensionsRoot.Path } else { $ExtensionsRoot }
    $packageRoot = Join-Path $extensionsBasePath $safeName
    $packagePath = Join-Path $packageRoot $safeVersion
    New-Item -ItemType Directory -Path $packagePath -Force | Out-Null

    $extension = [ordered]@{
        schema = 'https://pwsh-aot-lite.dev/schemas/extension/v1'
        schemaVersion = 1
        extension = [ordered]@{
            id = "legacy-pwsh/$($inspection.module.name)"
            displayName = $inspection.module.name
            version = $inspection.module.version
            execution = [ordered]@{
                kind = 'legacy-pwsh-sidecar'
                status = 'registered-not-invoked'
                reason = 'Contract/help registration only. Execution bridge is intentionally not implemented by this proof.'
            }
        }
        commands = @($inspection.commands | ForEach-Object {
            [ordered]@{
                name = $_.name
                commandType = $_.commandType
                defaultParameterSet = $_.defaultParameterSet
                parameters = $_.parameters
                parameterSets = $_.parameterSets
                outputTypes = $_.outputTypes
                helpUri = $_.help.helpUri
            }
        })
    }
    $help = [ordered]@{
        schema = 'https://pwsh-aot-lite.dev/schemas/help/v1'
        schemaVersion = 1
        module = [ordered]@{ name = $inspection.module.name; version = $inspection.module.version }
        commands = @($inspection.commands | ForEach-Object {
            [ordered]@{
                name = $_.name
                synopsis = $_.help.synopsis
                description = $_.help.description
                notes = $_.help.notes
                examples = $_.help.examples
                relatedLinks = $_.help.relatedLinks
                helpUri = $_.help.helpUri
                sourceKind = $_.help.sourceKind
            }
        })
    }
    $provenance = [ordered]@{
        schema = 'https://pwsh-aot-lite.dev/schemas/provenance/v1'
        schemaVersion = 1
        registration = [ordered]@{
            registeredAtUtc = [DateTime]::UtcNow.ToString('O')
            registrar = 'Register-PwshAotModule.ps1'
            inspector = $pwsh.Source
            inspectorVersion = (& $pwsh.Source -NoLogo -NoProfile -NonInteractive -Command '$PSVersionTable.PSVersion.ToString()')
            moduleWasImportedInIsolatedSidecar = $true
            exportedCmdletsWereInvoked = $false
        }
        sourceModule = $inspection.module
    }

    $extension | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $packagePath 'extension.json') -Encoding utf8NoBOM
    $help | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $packagePath 'help.json') -Encoding utf8NoBOM
    $provenance | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $packagePath 'provenance.json') -Encoding utf8NoBOM

    Write-Output $packagePath
}
finally {
    Remove-Item -LiteralPath $inspectionFile -Force -ErrorAction SilentlyContinue
}
