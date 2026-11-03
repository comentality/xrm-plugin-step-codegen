<#
.SYNOPSIS
    Builds the fixture solution zips from registrations.psd1 and the test plugin assemblies.

.DESCRIPTION
    Turns the test matrix into importable solutions:

      1. builds every fixture assembly and reads its real public key token back off the DLL
         - including the two no solution carries, which register.ps1 registers by hand
      2. resolves each message name to the target environment's sdkmessageid, and the
         signed in user's full name for the impersonating step, so nothing environment
         specific has to be committed
      3. writes the PluginAssembly metadata for each assembly and one
         SdkMessageProcessingStep xml per entry in the matrix
      4. packs one managed solution per publisher, plus the companion described below

    Every assembly's metadata is generated rather than committed, because four copies of
    the same xml is four chances to get one of them subtly wrong. What is worth knowing
    about the shape of it, since getting any of this wrong fails the import with a bare
    NullReferenceException out of GetPluginAssembliesTable and no hint as to why:

      - FullName is an attribute on PluginAssembly and is required. So are
        AssemblyQualifiedName and FriendlyName on each PluginType.
      - Almost everything else about the assembly - name, version, culture, public key
        token - is read out of FullName rather than declared separately.
      - The child elements are a sequence, not a bag: Description, IsolationMode,
        SourceType, IntroducedVersion, IsCustomizable, FileName, PluginTypes, in that
        order. The solution file schema is the authority:
        https://learn.microsoft.com/power-apps/developer/model-driven-apps/customization-solutions-file-schema
      - FileName points at the assembly's path inside the solution zip, and SourceType
        also decides whether SolutionPackager carries the DLL into the zip at all. Without
        it the zip packs happily and the import is what fails, so the pack is checked.
      - IsolationMode 2 is sandbox, which is why every fixture assembly is strong named.

    The companion solution exists because of a wrinkle worth knowing about: the solution
    format cannot carry a step's state, and every step lands Disabled unless the import is
    run with --activate-plugins, which then enables all of them. So the steps the matrix
    marks Disabled go into a solution that register.ps1 imports without that flag.

    Managed, because deleting an unmanaged solution leaves every component behind in the
    Default solution - unregister.ps1 would unregister nothing.

    register.ps1 calls this; run it directly to inspect the zips without importing.
#>
[CmdletBinding()]
param(
    # Environment URL or id. Defaults to the active organization of the pac auth profile.
    [string] $Environment,

    # Reuse the assemblies already in bin\Release instead of rebuilding them.
    [switch] $SkipAssemblyBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
. (Join-Path $root 'matrix.ps1')

$manifest = Import-PowerShellDataFile (Join-Path $root 'registrations.psd1')
$obj      = Join-Path $root 'obj'

$envArgs = if ($Environment) { @('--environment', $Environment) } else { @() }

function Invoke-Pac {
    param([Parameter(Mandatory)] [string[]] $Arguments)

    $output = & pac @Arguments 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        throw "pac $($Arguments -join ' ') failed:`r`n$($output -join "`r`n")"
    }

    $output
}

# pac has no structured output, so pull the id out of the fixed width table it prints.
function Get-IdByName {
    param([string[]] $Output, [string] $Name)

    $row = $Output | Where-Object { $_ -match '^([0-9a-fA-F-]{36})\s+(.+?)\s*$' -and $Matches[2] -eq $Name }
    if (-not $row) {
        throw "Could not find message '$Name' in the environment."
    }

    [void]($row -match '^([0-9a-fA-F-]{36})')
    $Matches[1]
}

# ---------------------------------------------------------------- assemblies

New-Item -ItemType Directory -Path $obj -Force | Out-Null

# Everything derived per assembly, keyed by assembly name: where the DLL is, what it turned
# out to be signed with, and the ids of its plugin types.
$built = @{}

foreach ($assembly in $manifest.Assemblies) {
    $project = Join-Path $root $assembly.Project
    $dll     = Join-Path (Split-Path $project -Parent) "bin\Release\net462\$($assembly.Name).dll"

    if (-not $SkipAssemblyBuild) {
        Write-Host "Building $($assembly.Name)..."
        # Out-Host, not the pipeline: this script returns the zip paths, and anything else
        # that reaches the pipeline ends up bundled in with them.
        & dotnet build $project -c Release -v quiet --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "$($assembly.Name) failed to build." }
    }

    if (-not (Test-Path $dll)) {
        throw "$($assembly.Name) not found at $dll. Run without -SkipAssemblyBuild."
    }

    # Read rather than declared: the token in the metadata has to be the one the DLL is
    # actually signed with, and asking the file is the only way to be sure of that.
    $token = -join ([Reflection.AssemblyName]::GetAssemblyName($dll).GetPublicKeyToken() |
        ForEach-Object { $_.ToString('x2') })
    if (-not $token) {
        throw "$($assembly.Name) is not strong named. Sandbox isolation requires it."
    }

    $typeIds = @{}
    foreach ($type in $assembly.Types) {
        $typeIds[$type.Name] = Get-TypeId $assembly $type
    }

    $folder = Get-AssemblyFolder $assembly
    $built[$assembly.Name] = [pscustomobject]@{
        Dll      = $dll
        Token    = $token
        FullName = Get-AssemblyFullName $assembly $token
        TypeIds  = $typeIds
        Folder   = $folder
        # The path inside the zip, which FileName has to point at and the pack is checked
        # against. Forward slashes both times; a zip has no other kind.
        ZipPath  = ($folder -replace '\\', '/') + "/$($assembly.Name).dll"
    }
}

# ---------------------------------------------------------------- environment lookups

$allSteps = Get-AllSteps $manifest

Write-Host 'Resolving message ids...'
$wanted = $allSteps.Step.Message | Sort-Object -Unique
$fetchFile = Join-Path $obj 'messages.fetch.xml'
Set-Content -Path $fetchFile -Encoding UTF8 -Value @"
<fetch>
  <entity name="sdkmessage">
    <attribute name="sdkmessageid" />
    <attribute name="name" />
    <filter>
      <condition attribute="name" operator="in">
$($wanted | ForEach-Object { "        <value>$_</value>" } | Out-String)      </condition>
    </filter>
  </entity>
</fetch>
"@

$messageIds = @{}
$rows = Invoke-Pac (@('env', 'fetch') + $envArgs + @('--xmlFile', $fetchFile))
foreach ($name in $wanted) { $messageIds[$name] = Get-IdByName -Output $rows -Name $name }

# The schema carries impersonation as ImpersonatingUserIdName, so what is needed is the
# user's full name - which is also exactly what the tool reads back and prints. Only
# the steps that go into a solution need it; an unmanaged one is written with the id
# instead, which unmanaged.ps1 asks WhoAmI for.
$impersonatedUserName = $null
if ($allSteps | Where-Object { $_.Assembly.Solution -and (Get-StepValue $_.Step 'Impersonate' $false) }) {
    $who = Invoke-Pac (@('env', 'who') + $envArgs)
    $line = $who | Where-Object { $_ -match 'User ID:\s+([0-9a-fA-F-]{36})' }
    if (-not $line) { throw 'Could not read the signed in user id from pac env who.' }
    [void]($line -match '([0-9a-fA-F-]{36})')
    $userId = $Matches[1]

    $userFetch = Join-Path $obj 'user.fetch.xml'
    Set-Content -Path $userFetch -Encoding UTF8 -Value @"
<fetch>
  <entity name="systemuser">
    <attribute name="fullname" />
    <filter>
      <condition attribute="systemuserid" operator="eq" value="$userId" />
    </filter>
  </entity>
</fetch>
"@

    $userRows = Invoke-Pac (@('env', 'fetch') + $envArgs + @('--xmlFile', $userFetch))
    # fetch always prints the primary key alongside whatever was asked for, so the row is
    # the name with the id stuck on one end of it.
    $impersonatedUserName = (($userRows |
        Where-Object { $_ -match '[0-9a-fA-F-]{36}' -and $_ -notmatch '^(Connected|Microsoft|Version)' } |
        Select-Object -Last 1) -replace '[0-9a-fA-F-]{36}', '').Trim()
    if (-not $impersonatedUserName) { throw "Could not read the full name of user $userId." }

    Write-Host "Impersonating step will run as '$impersonatedUserName'."
}

# ---------------------------------------------------------------- assembly metadata

function New-AssemblyDataXml {
    param([hashtable] $Assembly)

    $info = $built[$Assembly.Name]

    $xml = New-Object System.Text.StringBuilder
    [void]$xml.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$xml.AppendLine("<PluginAssembly FullName=`"$(ConvertTo-XmlText $info.FullName)`" PluginAssemblyId=`"{$(Get-AssemblyId $Assembly)}`" CustomizationLevel=`"1`">")
    [void]$xml.AppendLine('  <Description>Empty plugins used by the Plugin Step Codegen end to end suite.</Description>')
    [void]$xml.AppendLine('  <IsolationMode>2</IsolationMode>')
    [void]$xml.AppendLine('  <SourceType>0</SourceType>')
    [void]$xml.AppendLine('  <IntroducedVersion>1.0</IntroducedVersion>')
    [void]$xml.AppendLine('  <IsCustomizable>1</IsCustomizable>')
    [void]$xml.AppendLine("  <FileName>/$($info.ZipPath)</FileName>")
    [void]$xml.AppendLine('  <PluginTypes>')

    foreach ($type in $Assembly.Types) {
        $qualified = "$($type.Name), $($info.FullName)"
        # Unbraced ids here, which is how the server writes them back out on export.
        [void]$xml.AppendLine("    <PluginType Name=`"$(ConvertTo-XmlText $type.Name)`" AssemblyQualifiedName=`"$(ConvertTo-XmlText $qualified)`" PluginTypeId=`"{$($info.TypeIds[$type.Name])}`">")
        $description = Get-StepValue $type 'Description'
        if ($description) {
            [void]$xml.AppendLine("      <Description>$(ConvertTo-XmlText $description)</Description>")
        }
        [void]$xml.AppendLine("      <FriendlyName>$(ConvertTo-XmlText (Get-FriendlyName $Assembly $type.Name))</FriendlyName>")
        [void]$xml.AppendLine('    </PluginType>')
    }

    [void]$xml.AppendLine('  </PluginTypes>')
    [void]$xml.AppendLine('</PluginAssembly>')
    $xml.ToString()
}

# ---------------------------------------------------------------- step xml

function New-StepXml {
    param([hashtable] $Assembly, [hashtable] $Step)

    $info = $built[$Assembly.Name]
    $typeName = $Step.Type
    if (-not $info.TypeIds.ContainsKey($typeName)) {
        throw "$typeName has a step in $($Assembly.Name) but is not in that assembly's Types."
    }

    $typeId = $info.TypeIds[$typeName]
    $stepId = Get-StepId $Assembly $Step
    $entity = Get-StepEntity $Step
    $name = Get-StepName $Step

    # Get-StepFilter with no columns to expand against, so a dynamic spelling on a step
    # that is headed into a zip throws here instead of packing an empty filter: the
    # solution is built before any environment is in sight.
    $filter = Get-StepFilter $Step
    $description = Get-StepValue $Step 'Description'
    $configuration = Get-StepValue $Step 'Configuration'
    $autoDelete = if (Get-StepValue $Step 'AsyncAutoDelete' $false) { 1 } else { 0 }

    # Child elements are an xs:sequence, so the order below is the schema's and not a
    # matter of taste. EventHandler is the plugin type the step runs and 4602 is the object
    # type code that says so; leave the pair out and the import fails with nothing but the
    # word "EventHandlerTypeCode".
    $xml = New-Object System.Text.StringBuilder
    [void]$xml.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$xml.AppendLine("<SdkMessageProcessingStep SdkMessageProcessingStepId=`"{$stepId}`" Name=`"$(ConvertTo-XmlText $name)`">")
    # Qualified by the assembly, which is what tells two registrations of the same type
    # name - AccountTwinSyncPlugin is in two assemblies - apart.
    [void]$xml.AppendLine("  <PluginTypeName>$(ConvertTo-XmlText ($typeName + ', ' + $info.FullName))</PluginTypeName>")
    [void]$xml.AppendLine("  <PluginTypeId>$typeId</PluginTypeId>")

    # "none" is how Dataverse stores "no entity" on the filter, but the importer resolves
    # PrimaryEntity through the metadata cache, where there is no entity called none. A
    # step on a global message simply leaves it out.
    if ($entity -ne 'none') {
        [void]$xml.AppendLine("  <PrimaryEntity>$entity</PrimaryEntity>")
    }

    [void]$xml.AppendLine("  <AsyncAutoDelete>$autoDelete</AsyncAutoDelete>")

    # Omitted rather than written empty. An empty element lands as an empty string, and a
    # step with a zero length description is not the same thing as a step with none.
    if ($configuration) {
        [void]$xml.AppendLine("  <Configuration>$(ConvertTo-XmlText $configuration)</Configuration>")
    }
    if ($description) {
        [void]$xml.AppendLine("  <Description>$(ConvertTo-XmlText $description)</Description>")
    }
    if ($filter) {
        [void]$xml.AppendLine("  <FilteringAttributes>$(ConvertTo-XmlText $filter)</FilteringAttributes>")
    }

    # Impersonation travels by name, not by id, so it survives a move between environments.
    if (Get-StepValue $Step 'Impersonate' $false) {
        [void]$xml.AppendLine("  <ImpersonatingUserIdName>$(ConvertTo-XmlText $impersonatedUserName)</ImpersonatingUserIdName>")
    }

    [void]$xml.AppendLine('  <InvocationSource>0</InvocationSource>')
    [void]$xml.AppendLine("  <Mode>$($Step.Mode)</Mode>")
    [void]$xml.AppendLine("  <Rank>$($Step.Rank)</Rank>")
    [void]$xml.AppendLine("  <SdkMessageId>$($messageIds[$Step.Message])</SdkMessageId>")
    [void]$xml.AppendLine("  <EventHandler>$typeId</EventHandler>")
    [void]$xml.AppendLine('  <EventHandlerTypeCode>4602</EventHandlerTypeCode>')
    [void]$xml.AppendLine("  <Stage>$($Step.Stage)</Stage>")
    [void]$xml.AppendLine('  <IsCustomizable>1</IsCustomizable>')
    [void]$xml.AppendLine('  <IsHidden>0</IsHidden>')
    [void]$xml.AppendLine('  <SupportedDeployment>0</SupportedDeployment>')
    [void]$xml.AppendLine('  <IntroducedVersion>1.0</IntroducedVersion>')

    # There is deliberately no StateCode here. The schema has no element for it and the
    # importer ignores one if you invent it; which solution a step lands in is what decides
    # its state.

    $images = Get-StepImages $Step
    if ($images.Count -eq 0) {
        [void]$xml.AppendLine('  <SdkMessageProcessingStepImages />')
    } else {
        [void]$xml.AppendLine('  <SdkMessageProcessingStepImages>')
        for ($i = 0; $i -lt $images.Count; $i++) {
            $image = $images[$i]
            $imageId = Get-ImageId $Assembly $Step ($i + 1)
            $attributes = Get-ImageAttributes $Step $image
            $property = Get-StepValue $image 'Property' 'Target'

            [void]$xml.AppendLine("    <SdkMessageProcessingStepImage Name=`"$(ConvertTo-XmlText $image.Name)`">")
            [void]$xml.AppendLine("      <SdkMessageProcessingStepImageId>{$imageId}</SdkMessageProcessingStepImageId>")
            [void]$xml.AppendLine("      <Attributes>$(ConvertTo-XmlText $attributes)</Attributes>")
            [void]$xml.AppendLine("      <EntityAlias>$(ConvertTo-XmlText $image.Alias)</EntityAlias>")
            [void]$xml.AppendLine("      <ImageType>$($image.Type)</ImageType>")
            [void]$xml.AppendLine("      <MessagePropertyName>$property</MessagePropertyName>")
            [void]$xml.AppendLine('      <IsCustomizable>1</IsCustomizable>')
            [void]$xml.AppendLine('      <IntroducedVersion>1.0</IntroducedVersion>')
            [void]$xml.AppendLine('    </SdkMessageProcessingStepImage>')
        }
        [void]$xml.AppendLine('  </SdkMessageProcessingStepImages>')
    }

    [void]$xml.AppendLine('</SdkMessageProcessingStep>')
    $xml.ToString()
}

# ---------------------------------------------------------------- packing

$template = Get-Content (Join-Path $root 'solution\Solution.template.xml') -Raw

function New-FixtureSolution {
    param(
        [hashtable] $Solution,
        # The assemblies this solution owns. The companion owns none: it only registers
        # steps against plugin types another solution already installed.
        [hashtable[]] $Assemblies,
        # Each entry pairs a step with the assembly it belongs to.
        [object[]] $Steps
    )

    $publisher = $manifest.Publishers[$Solution.Publisher]
    if (-not $publisher) {
        throw "Solution $($Solution.Name) names publisher '$($Solution.Publisher)', which is not in Publishers."
    }

    $stage = Join-Path $obj "src-$($Solution.Name)"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item (Join-Path $root 'solution\src\*') $stage -Recurse -Force

    $rootComponents = New-Object System.Text.StringBuilder

    foreach ($assembly in $Assemblies) {
        $info = $built[$assembly.Name]
        $folder = Join-Path $stage $info.Folder
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        Copy-Item $info.Dll $folder -Force
        Set-Content -Path (Join-Path $folder "$($assembly.Name).dll.data.xml") `
            -Value (New-AssemblyDataXml $assembly) -Encoding UTF8 -NoNewline

        # Both identifiers, because the two halves of the toolchain disagree:
        # SolutionPackager validates the assembly root component by id, the server matches
        # it by schema name.
        [void]$rootComponents.AppendLine(
            "      <RootComponent type=`"91`" id=`"{$(Get-AssemblyId $assembly)}`" schemaName=`"$(ConvertTo-XmlText $info.FullName)`" behavior=`"0`" />")
    }

    if ($Steps.Count -gt 0) {
        $stepFolder = Join-Path $stage 'SdkMessageProcessingSteps'
        New-Item -ItemType Directory -Path $stepFolder -Force | Out-Null

        foreach ($entry in $Steps) {
            # The block keeps file names unique: step ids only run unique within an assembly.
            $fileName = '{0}{1}-{2}.xml' -f $entry.Assembly.Block, $entry.Step.Id,
                (Get-FriendlyName $entry.Assembly $entry.Step.Type)
            Set-Content -Path (Join-Path $stepFolder $fileName) `
                -Value (New-StepXml $entry.Assembly $entry.Step) -Encoding UTF8 -NoNewline
            [void]$rootComponents.AppendLine(
                "      <RootComponent type=`"92`" id=`"{$(Get-StepId $entry.Assembly $entry.Step)}`" behavior=`"0`" />")
        }
    }

    $tokens = [ordered]@{
        '{{SolutionUniqueName}}'         = $Solution.Name
        '{{SolutionTitle}}'              = ConvertTo-XmlText $Solution.Title
        '{{PublisherUniqueName}}'        = $publisher.UniqueName
        '{{PublisherName}}'              = ConvertTo-XmlText $publisher.Name
        '{{PublisherPrefix}}'            = $publisher.Prefix
        '{{PublisherOptionValuePrefix}}' = $publisher.OptionValuePrefix
        '{{RootComponents}}'             = $rootComponents.ToString().TrimEnd()
    }

    $solutionXml = $template
    foreach ($token in $tokens.Keys) {
        $solutionXml = $solutionXml.Replace($token, $tokens[$token])
    }

    if ($solutionXml -match '\{\{') {
        throw "Solution.template.xml still has an unreplaced token after building $($Solution.Name)."
    }

    Set-Content -Path (Join-Path $stage 'Other\Solution.xml') -Value $solutionXml -Encoding UTF8 -NoNewline

    $zip = Join-Path $obj "$($Solution.Name).zip"
    Write-Host "Packing $($Assemblies.Count) assembly/assemblies and $($Steps.Count) step(s) into $($Solution.Name).zip..."
    Invoke-Pac @('solution', 'pack', '--zipfile', $zip, '--folder', $stage, '--packagetype', 'Managed') | Out-Null

    # SolutionPackager only carries an assembly into the zip if it could make sense of the
    # metadata, and says nothing when it could not - the import is then what fails, with a
    # NullReferenceException that names nothing. Catch it here instead.
    if ($Assemblies.Count -gt 0) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            foreach ($assembly in $Assemblies) {
                $path = $built[$assembly.Name].ZipPath
                if (-not ($archive.Entries | Where-Object { $_.FullName -eq $path })) {
                    throw "SolutionPackager left $path out of $zip. Check the generated $($assembly.Name).dll.data.xml against the solution file schema."
                }
            }
        } finally {
            $archive.Dispose()
        }
    }

    $zip
}

$main = @()
foreach ($solution in $manifest.Solutions) {
    $assemblies = @($manifest.Assemblies | Where-Object { $_.Solution -eq $solution.Name })
    if ($assemblies.Count -eq 0) {
        throw "Solution $($solution.Name) has no assemblies."
    }

    $steps = @($allSteps |
        Where-Object { $_.Assembly.Solution -eq $solution.Name -and -not (Get-StepValue $_.Step 'Disabled' $false) })

    $main += New-FixtureSolution -Solution $solution -Assemblies $assemblies -Steps $steps
}

$companion = $null
# An unmanaged assembly's steps are written disabled on the record, so they have no
# business in the companion solution - the companion exists only because a solution
# cannot say so.
$disabled = @($allSteps | Where-Object { $_.Assembly.Solution -and (Get-StepValue $_.Step 'Disabled' $false) })
if ($disabled.Count -gt 0) {
    $companion = New-FixtureSolution -Solution $manifest.DisabledSolution -Assemblies @() -Steps $disabled
}

[pscustomobject]@{
    Main      = $main
    Companion = $companion
    # Every assembly, packed or not, and what it turned out to be: register.ps1 hands this
    # to the unmanaged registration, which needs the DLL and the token it is really signed
    # with and must not go looking for either a second time.
    Built     = $built
}
