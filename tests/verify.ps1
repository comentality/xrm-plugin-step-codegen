<#
.SYNOPSIS
    Checks that the environment actually matches registrations.psd1.

.DESCRIPTION
    Run after register.ps1, before pointing the tool at the environment: if this
    fails, anything the tool writes is being judged against the wrong registration.

    Every check is a FetchXML query whose filter spells out what the assembly, step or
    image is supposed to be, asking the server to agree rather than parsing values back
    out of pac's fixed width tables. A row means the environment matches; no row means it
    does not, and the check names what it was looking for.

    With several assemblies in play, every step query is also tied to the assembly it
    belongs to. AccountTwinSyncPlugin is registered from two of them, so a check that only
    named the plugin type would happily pass against the wrong one.
#>
[CmdletBinding()]
param([string] $Environment)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
. (Join-Path $root 'matrix.ps1')

$manifest = Import-PowerShellDataFile (Join-Path $root 'registrations.psd1')
$envArgs = if ($Environment) { @('--environment', $Environment) } else { @() }
$scratch = Join-Path $root 'obj\verify.fetch.xml'
New-Item -ItemType Directory -Path (Join-Path $root 'obj') -Force | Out-Null

$script:passed = 0
$script:failures = @()

function Invoke-Fetch {
    param([string] $Xml)

    Set-Content -Path $scratch -Value $Xml -Encoding UTF8
    $output = & pac @(@('env', 'fetch') + $envArgs + @('--xmlFile', $scratch)) 2>&1 |
        ForEach-Object { "$_" }

    if ($LASTEXITCODE -ne 0) {
        throw "pac env fetch failed:`r`n$($output -join "`r`n")"
    }

    $output -join "`n"
}

function Test-Fetch {
    param([string] $Description, [string] $Xml, [string[]] $Expect)

    $output = Invoke-Fetch $Xml
    $missing = @($Expect | Where-Object { $output -notmatch [regex]::Escape($_) })

    if ($missing.Count -eq 0) {
        $script:passed++
        Write-Host "  ok    $Description"
    } else {
        $script:failures += "$Description (no row with $($missing -join ', '))"
        Write-Host "  FAIL  $Description" -ForegroundColor Red
    }
}

function Test-Count {
    param([string] $Description, [int] $Found, [int] $Expected)

    if ($Found -eq $Expected) {
        $script:passed++
        Write-Host "  ok    $Description"
    } else {
        $script:failures += "$Description - found $Found, expected $Expected"
        Write-Host "  FAIL  $Description - found $Found, expected $Expected" -ForegroundColor Red
    }
}

function Get-Plural {
    param([int] $Count, [string] $Noun)

    if ($Count -eq 1) { "1 $Noun" } else { "$Count $Noun`s" }
}

function Measure-Guids {
    param([string] $Output)

    ([regex]::Matches($Output, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}')).Count
}

# A condition on a value that may legitimately be empty. Dataverse stores an empty string
# as null, so "no filtering attributes" has to be asked for as null, not as "".
function New-Condition {
    param([string] $Attribute, [string] $Value)

    if ([string]::IsNullOrEmpty($Value)) {
        "      <condition attribute=`"$Attribute`" operator=`"null`" />"
    } else {
        "      <condition attribute=`"$Attribute`" operator=`"eq`" value=`"$(ConvertTo-XmlText $Value)`" />"
    }
}

# XML normalises tabs and newlines inside an attribute value into spaces, so free text that
# contains either cannot be matched exactly. Match the part before the first one instead.
function New-TextCondition {
    param([string] $Attribute, [string] $Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return New-Condition $Attribute $Value
    }

    $head = ($Value -split "[`t`r`n]")[0]
    if ($head -eq $Value) {
        New-Condition $Attribute $Value
    } else {
        "      <condition attribute=`"$Attribute`" operator=`"like`" value=`"$(ConvertTo-XmlText $head)%`" />"
    }
}

$allSteps = Get-AllSteps $manifest

# A step's filtered entity lives on sdkmessagefilter as primaryobjecttypecode, which is an
# integer as far as a condition is concerned, and which pac renders as the entity's display
# name ("Note" for annotation) rather than its logical name. So neither the obvious filter
# nor the obvious string comparison works: resolve the codes first and filter on those.
$entities = @($allSteps | ForEach-Object { Get-StepEntity $_.Step } | Where-Object { $_ -ne 'none' } | Sort-Object -Unique)
$objectTypeCodes = @{}
if ($entities.Count -gt 0) {
    $rows = Invoke-Fetch @"
<fetch>
  <entity name="entity">
    <attribute name="logicalname" />
    <attribute name="objecttypecode" />
    <filter>
      <condition attribute="logicalname" operator="in">
$($entities | ForEach-Object { "        <value>$_</value>" } | Out-String)      </condition>
    </filter>
  </entity>
</fetch>
"@

    foreach ($line in $rows -split "`n") {
        # pac prints large codes with a thousands separator, hence the comma strip.
        if ($line -match '^(\S+)\s+([\d,]+)\s+[0-9a-fA-F-]{36}') {
            $objectTypeCodes[$Matches[1]] = $Matches[2].Replace(',', '')
        }
    }

    foreach ($name in $entities) {
        if (-not $objectTypeCodes.ContainsKey($name)) {
            throw "Could not resolve an object type code for '$name'. Is the table in this environment?"
        }
    }
}

# The dynamic filters were expanded against the live table when register.ps1 wrote them,
# so the expected value is recomputed the same way here - through the same functions, off
# the same metadata. If a column arrived in between, this fails and re-running
# register.ps1 is the fix.
$liveColumns = @{}
$dynamicEntities = Get-DynamicColumnEntities $manifest
if ($dynamicEntities.Count -gt 0) {
    . (Join-Path $root 'dataverse.ps1')
    Connect-Dataverse -Environment $Environment | Out-Null
    foreach ($name in $dynamicEntities) {
        $liveColumns[$name] = Get-DataverseEntityColumns $name
    }
}

Write-Host "Checking $($manifest.Assemblies.Count) assemblies and $($allSteps.Count) steps against the environment..."

foreach ($assembly in $manifest.Assemblies) {
    Write-Host ''
    Write-Host $assembly.Name

    # ------------------------------------------------------------ the assembly itself

    # Managed or not is the one thing that separates the assembly somebody registered by
    # hand from the four that arrived in solutions. The tool does not read it and is
    # not supposed to start; checking it here is what says the two routes really did
    # produce two different kinds of record.
    $managed = if ($assembly.Solution) { 'true' } else { 'false' }

    Test-Fetch -Description "assembly $($assembly.Name) is visible to the tool and ismanaged $managed" `
        -Expect (Get-AssemblyId $assembly) -Xml @"
<fetch>
  <entity name="pluginassembly">
    <attribute name="pluginassemblyid" />
    <filter>
      <condition attribute="name" operator="eq" value="$($assembly.Name)" />
      <condition attribute="pluginassemblyid" operator="eq" value="$(Get-AssemblyId $assembly)" />
      <condition attribute="isolationmode" operator="eq" value="2" />
      <condition attribute="ismanaged" operator="eq" value="$managed" />
      <!-- The condition the tool's own assembly query uses. -->
      <condition attribute="ishidden" operator="eq" value="false" />
    </filter>
  </entity>
</fetch>
"@

    # Every type the matrix declares, and no others. A type in the environment that is not
    # in registrations.psd1 would show up in the tool and be judged against nothing.
    $typeRows = Invoke-Fetch @"
<fetch>
  <entity name="plugintype">
    <attribute name="plugintypeid" />
    <link-entity name="pluginassembly" from="pluginassemblyid" to="pluginassemblyid" alias="a">
      <filter>
        <condition attribute="name" operator="eq" value="$($assembly.Name)" />
      </filter>
    </link-entity>
  </entity>
</fetch>
"@

    Test-Count -Description "  exactly $(Get-Plural $assembly.Types.Count 'plugin type')" `
        -Found (Measure-Guids $typeRows) -Expected $assembly.Types.Count

    # ------------------------------------------------------------ steps

    foreach ($step in $assembly.Steps) {
        $stepId = Get-StepId $assembly $step
        $entity = Get-StepEntity $step
        $state = if (Get-StepValue $step 'Disabled' $false) { 1 } else { 0 }

        $conditions = @(
            "      <condition attribute=`"sdkmessageprocessingstepid`" operator=`"eq`" value=`"$stepId`" />"
            "      <condition attribute=`"stage`" operator=`"eq`" value=`"$($step.Stage)`" />"
            "      <condition attribute=`"mode`" operator=`"eq`" value=`"$($step.Mode)`" />"
            "      <condition attribute=`"rank`" operator=`"eq`" value=`"$($step.Rank)`" />"
            "      <condition attribute=`"statecode`" operator=`"eq`" value=`"$state`" />"
            "      <condition attribute=`"name`" operator=`"eq`" value=`"$(ConvertTo-XmlText (Get-StepName $step))`" />"
            "      <condition attribute=`"asyncautodelete`" operator=`"eq`" value=`"$(([bool](Get-StepValue $step 'AsyncAutoDelete' $false)).ToString().ToLower())`" />"
            (New-Condition 'filteringattributes' (Get-StepFilter $step $liveColumns))
            (New-TextCondition 'description' (Get-StepValue $step 'Description'))
            (New-TextCondition 'configuration' (Get-StepValue $step 'Configuration'))
        )

        # The message name and the filtered entity live on linked records, not on the step.
        # The plugin type is qualified by its assembly, because a type name on its own is
        # not unique across the fixture. The link is on PluginTypeId and is inner, which is
        # the check that matters most for a step registered by hand: it is written with the
        # plugin type on EventHandler, and if the platform ever stopped deriving
        # PluginTypeId from that, the tool would find no steps and this would say so.
        $links = @"
    <link-entity name="sdkmessage" from="sdkmessageid" to="sdkmessageid" alias="m">
      <filter>
        <condition attribute="name" operator="eq" value="$($step.Message)" />
      </filter>
    </link-entity>
    <link-entity name="plugintype" from="plugintypeid" to="plugintypeid" alias="t">
      <filter>
        <condition attribute="typename" operator="eq" value="$($step.Type)" />
      </filter>
      <link-entity name="pluginassembly" from="pluginassemblyid" to="pluginassemblyid" alias="ta">
        <filter>
          <condition attribute="name" operator="eq" value="$($assembly.Name)" />
        </filter>
      </link-entity>
    </link-entity>
"@

        if ($entity -eq 'none') {
            # A step on a global message has no filter at all.
            $conditions += '      <condition attribute="sdkmessagefilterid" operator="null" />'
        } else {
            $links += @"
    <link-entity name="sdkmessagefilter" from="sdkmessagefilterid" to="sdkmessagefilterid" alias="f">
      <filter>
        <condition attribute="primaryobjecttypecode" operator="eq" value="$($objectTypeCodes[$entity])" />
      </filter>
    </link-entity>
"@
        }

        if (Get-StepValue $step 'Impersonate' $false) {
            # Whoever registered is whoever the step impersonates, so there is no fixed name
            # to assert; that the link resolves at all is the fact worth checking.
            $links += @"
    <link-entity name="systemuser" from="systemuserid" to="impersonatinguserid" alias="u" link-type="inner">
      <attribute name="fullname" />
    </link-entity>
"@
        } else {
            $conditions += '      <condition attribute="impersonatinguserid" operator="null" />'
        }

        $what = '  {0} {1} {2}{3} rank {4}' -f $step.Id, (Get-FriendlyName $assembly $step.Type),
            $step.Message, $(if ($entity -eq 'none') { '' } else { " of $entity" }), $step.Rank

        Test-Fetch -Description $what -Expect $stepId -Xml @"
<fetch>
  <entity name="sdkmessageprocessingstep">
    <attribute name="sdkmessageprocessingstepid" />
    <filter type="and">
$($conditions -join "`r`n")
    </filter>
$links
  </entity>
</fetch>
"@

        $images = Get-StepImages $step
        for ($i = 0; $i -lt $images.Count; $i++) {
            $image = $images[$i]
            $imageId = Get-ImageId $assembly $step ($i + 1)

            $imageConditions = @(
                "      <condition attribute=`"sdkmessageprocessingstepimageid`" operator=`"eq`" value=`"$imageId`" />"
                "      <condition attribute=`"sdkmessageprocessingstepid`" operator=`"eq`" value=`"$stepId`" />"
                "      <condition attribute=`"imagetype`" operator=`"eq`" value=`"$($image.Type)`" />"
                "      <condition attribute=`"name`" operator=`"eq`" value=`"$(ConvertTo-XmlText $image.Name)`" />"
                "      <condition attribute=`"entityalias`" operator=`"eq`" value=`"$(ConvertTo-XmlText $image.Alias)`" />"
                "      <condition attribute=`"messagepropertyname`" operator=`"eq`" value=`"$(Get-StepValue $image 'Property' 'Target')`" />"
                (New-Condition 'attributes' (Get-ImageAttributes $step $image $liveColumns))
            )

            Test-Fetch -Description "    image $($image.Name) on $($step.Id)" -Expect $imageId -Xml @"
<fetch>
  <entity name="sdkmessageprocessingstepimage">
    <attribute name="sdkmessageprocessingstepimageid" />
    <filter type="and">
$($imageConditions -join "`r`n")
    </filter>
  </entity>
</fetch>
"@
        }
    }

    # ------------------------------------------------------------ nothing extra

    # The suite is only meaningful if the environment holds exactly the matrix and no more,
    # so count what is there rather than only checking that each expected step exists.
    $stepRows = Invoke-Fetch @"
<fetch>
  <entity name="sdkmessageprocessingstep">
    <attribute name="sdkmessageprocessingstepid" />
    <link-entity name="plugintype" from="plugintypeid" to="plugintypeid" alias="t">
      <link-entity name="pluginassembly" from="pluginassemblyid" to="pluginassemblyid" alias="a">
        <filter>
          <condition attribute="name" operator="eq" value="$($assembly.Name)" />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>
"@

    Test-Count -Description "  exactly $(Get-Plural $assembly.Steps.Count 'step')" `
        -Found (Measure-Guids $stepRows) -Expected $assembly.Steps.Count

    # ------------------------------------------------------------ the stepless types

    # Types with no steps are what the tool is required to leave out of its list, so
    # each one has to still be there and still have nothing against it.
    $registered = @($assembly.Steps | ForEach-Object { $_.Type } | Sort-Object -Unique)
    foreach ($type in $assembly.Types) {
        if ($registered -contains $type.Name) {
            continue
        }

        $rows = Invoke-Fetch @"
<fetch>
  <entity name="plugintype">
    <attribute name="plugintypeid" />
    <filter>
      <condition attribute="plugintypeid" operator="eq" value="$(Get-TypeId $assembly $type)" />
    </filter>
    <link-entity name="sdkmessageprocessingstep" from="plugintypeid" to="plugintypeid" alias="s" link-type="inner">
      <attribute name="sdkmessageprocessingstepid" />
    </link-entity>
  </entity>
</fetch>
"@

        if ($rows -match 'No results returned') {
            $script:passed++
            Write-Host "  ok    $($type.Name) is registered with no steps"
        } else {
            $script:failures += "$($type.Name) has steps and should not"
            Write-Host "  FAIL  $($type.Name) has steps and should not" -ForegroundColor Red
        }
    }
}

# ---------------------------------------------------------------- result

Write-Host ''
if ($script:failures.Count -eq 0) {
    Write-Host "All $($script:passed) checks passed." -ForegroundColor Green
    exit 0
}

Write-Host "$($script:failures.Count) of $($script:passed + $script:failures.Count) checks failed:" -ForegroundColor Red
$script:failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
exit 1
