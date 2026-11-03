<#
.SYNOPSIS
    Checks what the tool emits against the real XrmTools.Meta.Attributes package.

.DESCRIPTION
    The tool's whole premise is that what it writes into your source is Xrm Tools
    compatible. write.ps1 compiles the emitted attributes against assets\XrmToolsMetaAttributes.cs,
    which is this repository's own copy of the shape - so it proves the emitter agrees
    with us, and nothing at all about whether we still agree with upstream.

    This closes that. It builds the same generated corpus twice, once against the real
    package from nuget.org at each pinned version and once against the definitions file
    the tool writes, and asks three questions:

      1. Does it compile? Against every version, with warnings as errors, so an emitted
         attribute that resolves to something deprecated fails here rather than in
         somebody's build.

      2. Is it the same? Both builds print the attributes the compiler bound and the
         attribute objects the runtime constructs, every property of them. Those
         printouts have to match exactly. This is the check that catches a drifted
         default: the emitter leaves ExecutionOrder out when the rank is 1 because
         StepAttribute defaults it to 1, and only constructing one proves that.

      3. What moved? The surface each source brings into a compilation is printed and
         diffed as a report. Upstream growing a property is news rather than a failure,
         but it should not be news a year late.

    The corpus is not the test matrix. registrations.psd1 pins what a real environment
    looks like and write.ps1 judges the tool against it; this one is built to hit every
    branch in AttributeEmitter instead, including the ones no environment would hold
    still for - a step at the retired stage 50, a description with a quote and a newline
    in it, a filter list long enough to argue about wrapping.

    Needs the network, for the package restores. Needs no Dataverse connection.

.PARAMETER Versions
    Package versions to compile against. The default ladder spans the two eras the
    package has had: 1.0.57 is what the definitions file and the docs pin, 1.1.3 is the
    last release to ship its attributes as source files, and 1.1.4 is the first to
    generate them - which is also where they turned internal.

.PARAMETER SkipLatest
    Do not add whatever nuget.org currently calls latest to the ladder. The point of
    testing latest is to hear about a release that broke us; skip it for a run that has
    to be reproducible.

.PARAMETER SkipBuild
    Use the tool DLL already in bin\Debug rather than rebuilding it.

.PARAMETER Keep
    Leave tests\.compat in place afterwards. The generated corpus and every printout are
    in there, which is where to look when something fails.
#>
[CmdletBinding()]
param(
    [string[]] $Versions = @('1.0.57', '1.1.3', '1.1.4'),
    [switch]   $SkipLatest,
    [switch]   $SkipBuild,
    [switch]   $Keep
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$script:failures = @()

function Check {
    param([string] $What, [bool] $Ok, [string] $Detail)

    if ($Ok) {
        Write-Host "  [ ok ] $What" -ForegroundColor DarkGreen
    }
    else {
        Write-Host "  [FAIL] $What" -ForegroundColor Red
        if ($Detail) { Write-Host $Detail -ForegroundColor DarkGray }
        $script:failures += $What
    }
}

# ------------------------------------------------------------------ the tool itself
$project = Join-Path $root '..\PluginStepCodegen\PluginStepCodegen.csproj'
if (-not $SkipBuild) {
    dotnet build $project -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'The tool did not build.' }
}

$dll = Resolve-Path (Join-Path $root '..\PluginStepCodegen\bin\Debug\net48\PluginStepCodegen.dll')
[Reflection.Assembly]::LoadFrom($dll) | Out-Null

# ----------------------------------------------------------------------- the corpus
function New-Image {
    param(
        [int]    $ImageType,
        [string] $Attributes,
        [string] $Name,
        [string] $EntityAlias,
        [string] $MessagePropertyName
    )

    $image = [PluginStepCodegen.Logic.PluginImageInfo]::new()
    $image.ImageType = $ImageType
    $image.Attributes = $Attributes
    $image.Name = $Name
    $image.EntityAlias = $EntityAlias
    $image.MessagePropertyName = $MessagePropertyName
    $image
}

function New-Step {
    param(
        [string]   $Message,
        [string]   $Entity,
        [string]   $Filter,
        [int]      $Stage = 40,
        [int]      $Mode = 0,
        [int]      $Rank = 1,
        [string]   $Name,
        [string]   $Description,
        [string]   $Configuration,
        [switch]   $AsyncAutoDelete,
        [object[]] $Images = @()
    )

    $step = [PluginStepCodegen.Logic.PluginStepInfo]::new()
    $step.MessageName = $Message
    $step.PrimaryEntityName = $Entity
    $step.FilteringAttributes = $Filter
    $step.Stage = $Stage
    $step.Mode = $Mode
    $step.Rank = $Rank
    $step.Name = $Name
    $step.Description = $Description
    $step.Configuration = $Configuration
    $step.AsyncAutoDelete = [bool] $AsyncAutoDelete
    foreach ($image in $Images) { $step.Images.Add($image) }
    $step
}

function New-Type {
    param(
        [string]   $Name,
        [string]   $Description,
        [object[]] $Steps = @()
    )

    $type = [PluginStepCodegen.Logic.PluginTypeInfo]::new()
    $type.TypeName = "Corpus.$Name"
    $type.FriendlyName = $Name
    $type.Description = $Description
    foreach ($step in $Steps) { $type.Steps.Add($step) }
    $type
}

# One class per decision AttributeEmitter makes. Named for the decision rather than for
# anything a plugin would be called, because that is what a failure here is about.
$corpus = @(
    # [Plugin], with and without the one property it can carry.
    New-Type -Name 'PluginBare' -Steps @(
        (New-Step -Message 'Create' -Entity 'account' -Stage 20)
    )
    New-Type -Name 'PluginDescribed' -Description 'Keeps account data consistent.' -Steps @(
        (New-Step -Message 'Create' -Entity 'account' -Stage 20)
    )

    # The three [Step] constructors, and the two ways of having no entity.
    New-Type -Name 'StepWithEntity' -Steps @(
        (New-Step -Message 'Update' -Entity 'contact' -Stage 40)
    )
    New-Type -Name 'StepWithFilter' -Steps @(
        (New-Step -Message 'Update' -Entity 'contact' -Filter 'firstname,lastname' -Stage 40)
    )
    New-Type -Name 'StepGlobalMessage' -Steps @(
        (New-Step -Message 'ilac_DoTheThing' -Stage 20)
    )
    # spkl writes "none" for a global message; the emitter has to read that as no entity
    # and reach for the constructor that takes none, not write the word through.
    New-Type -Name 'StepEntityNone' -Steps @(
        (New-Step -Message 'ilac_DoTheThing' -Entity 'none' -Stage 20)
    )
    # A filter with no entity to filter on. There is no constructor overload for it, so
    # the filter has to be dropped rather than shifted into a slot that means something
    # else.
    New-Type -Name 'StepGlobalMessageWithFilter' -Steps @(
        (New-Step -Message 'ilac_DoTheThing' -Filter 'ilac_name' -Stage 20)
    )

    # Every stage, including the retired one. 50 has no member the emitter may name -
    # upstream calls it DepecratedPostOperation and marks it [Obsolete(error: true)] -
    # so it is written as a cast, and this is what proves the cast still compiles.
    New-Type -Name 'StageEvery' -Steps @(
        (New-Step -Message 'Create' -Entity 'account' -Stage 10)
        (New-Step -Message 'Create' -Entity 'account' -Stage 20)
        (New-Step -Message 'Create' -Entity 'account' -Stage 30)
        (New-Step -Message 'Create' -Entity 'account' -Stage 40)
        (New-Step -Message 'Create' -Entity 'account' -Stage 50)
    )

    New-Type -Name 'ModeBoth' -Steps @(
        (New-Step -Message 'Create' -Entity 'account' -Stage 40 -Mode 0)
        (New-Step -Message 'Update' -Entity 'account' -Stage 40 -Mode 1)
    )

    # Every named argument at once, which is also the only way to get a line long enough
    # to wrap.
    New-Type -Name 'StepEveryNamedArgument' -Steps @(
        (New-Step -Message 'Update' -Entity 'account' -Filter 'name,address1_line1' -Stage 40 -Mode 1 `
            -Rank 25 -Name 'Recalculate rollups' -Description 'Runs after the write completes.' `
            -Configuration 'https://example.invalid/hook' -AsyncAutoDelete)
    )

    # The name Dataverse generates when nobody types one is suppressed; a typed one is
    # kept. Both spellings of the generated name, with an entity and without.
    New-Type -Name 'StepGeneratedName' -Steps @(
        (New-Step -Message 'Create' -Entity 'account' -Stage 40 -Name 'Corpus.StepGeneratedName: Create of account')
        (New-Step -Message 'ilac_DoTheThing' -Stage 40 -Name 'Corpus.StepGeneratedName: ilac_DoTheThing')
        (New-Step -Message 'Update' -Entity 'account' -Stage 40 -Name 'Recalculate rollups')
    )

    # Free text that has to survive being written as a C# literal.
    New-Type -Name 'StepEscapedText' -Description 'A description with \ and " in it.' -Steps @(
        (New-Step -Message 'Update' -Entity 'account' -Stage 40 `
            -Description "quote `" backslash \ tab`t return`r newline`n end" `
            -Configuration 'C:\config\path.json')
    )

    # Images: both constructors, all three types, every named argument.
    New-Type -Name 'ImageEveryShape' -Steps @(
        (New-Step -Message 'Update' -Entity 'account' -Stage 40 -Images @(
            (New-Image -ImageType 0)
            (New-Image -ImageType 1 -Attributes 'name,accountnumber')
            (New-Image -ImageType 2 -Attributes 'name' -Name 'Before' -EntityAlias 'Before' -MessagePropertyName 'Account')
        ))
    )

    # An image whose name and alias are the defaults the attribute already applies, and a
    # MessagePropertyName that is the default Target. None of the three may be emitted -
    # and the EVALUATED section is what proves the defaults really are those.
    New-Type -Name 'ImageDefaults' -Steps @(
        (New-Step -Message 'Update' -Entity 'account' -Stage 40 -Images @(
            (New-Image -ImageType 0 -Attributes 'name' -Name 'PreImage' -EntityAlias 'PreImage' -MessagePropertyName 'Target')
            (New-Image -ImageType 1 -Attributes 'name' -Name 'PostImage' -EntityAlias 'PostImage')
            (New-Image -ImageType 2 -Attributes 'name' -Name 'Both' -EntityAlias 'Both')
        ))
    )

    # A long line with nothing named in it. The wrap only moves named arguments, so this
    # one stays on one line however long the filter gets.
    New-Type -Name 'StepLongFilterNoNamed' -Steps @(
        (New-Step -Message 'Update' -Entity 'account' -Stage 40 -Filter (
            'accountcategorycode,accountnumber,accountratingcode,address1_city,address1_country,' +
            'address1_line1,address1_line2,address1_postalcode,address1_stateorprovince,creditlimit,' +
            'customersizecode,description,emailaddress1,fax,industrycode,name,numberofemployees'))
    )

    # Several steps with their images interleaved. [Image] binds to the nearest preceding
    # [Step], so this is the class where the DECLARED section earns its keep: if the
    # order the emitter chose did not survive the compiler, this is where it shows.
    New-Type -Name 'OrderInterleaved' -Steps @(
        (New-Step -Message 'Create' -Entity 'account' -Stage 20 -Images @(
            (New-Image -ImageType 1 -Attributes 'name')
        ))
        (New-Step -Message 'Update' -Entity 'account' -Stage 40 -Rank 2 -Images @(
            (New-Image -ImageType 0 -Attributes 'name')
            (New-Image -ImageType 1 -Attributes 'accountnumber')
        ))
        (New-Step -Message 'Delete' -Entity 'account' -Stage 40 -Rank 3)
    )
)

# -------------------------------------------------------------------- generate Corpus.g.cs
$emitted = [ordered] @{}
$source = New-Object System.Text.StringBuilder
[void] $source.AppendLine('// Generated by tests\compat.ps1 from the tool''s own AttributeEmitter.')
[void] $source.AppendLine('// Every class here is one decision the emitter makes. Do not edit; do not commit.')
[void] $source.AppendLine()
[void] $source.AppendLine('using XrmTools.Meta.Attributes;')
[void] $source.AppendLine()
[void] $source.AppendLine('namespace Corpus')
[void] $source.AppendLine('{')

foreach ($type in $corpus) {
    $lines = @([PluginStepCodegen.Logic.AttributeEmitter]::Emit($type))
    $emitted[$type.FriendlyName] = $lines

    foreach ($line in $lines) {
        foreach ($physical in $line -split "`r?`n") {
            [void] $source.AppendLine('    ' + $physical)
        }
    }

    [void] $source.AppendLine('    internal sealed class ' + $type.FriendlyName + ' { }')
    [void] $source.AppendLine()
}

[void] $source.AppendLine('}')

$work = Join-Path $root '.compat'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Path $work -Force | Out-Null

$corpusFile = Join-Path $work 'Corpus.g.cs'
Set-Content -Path $corpusFile -Value $source.ToString() -Encoding UTF8 -NoNewline

$attributeCount = ($emitted.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
Write-Host ''
Write-Host "Corpus: $($corpus.Count) classes, $attributeCount attributes." -ForegroundColor Cyan

# ---------------------------------------------------------------------- the sources
if (-not $SkipLatest) {
    $index = Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/xrmtools.meta.attributes/index.json'
    $latest = @($index.versions | Where-Object { $_ -notmatch '-' })[-1]
    if ($Versions -notcontains $latest) { $Versions = @($Versions) + $latest }
    Write-Host "Latest on nuget.org: $latest" -ForegroundColor Cyan
}

$sources = @(
    [pscustomobject] @{ Label = 'local'; Args = @('-p:CompatSource=Local') }
)
foreach ($version in $Versions) {
    $sources += [pscustomobject] @{
        Label = "package $version"
        Args  = @('-p:CompatSource=Package', "-p:XrmToolsVersion=$version")
    }
}

# ------------------------------------------------------------------- build and run
Write-Host ''
Write-Host 'Compiling the corpus against each definitions source' -ForegroundColor Cyan

$dumps = [ordered] @{}
foreach ($item in $sources) {
    $folder = Join-Path $work ($item.Label -replace '[^\w.]', '-')
    New-Item -ItemType Directory -Path $folder -Force | Out-Null

    Copy-Item (Join-Path $root 'compat\Probe.csproj') $folder
    Copy-Item (Join-Path $root 'compat\Dump.cs') $folder
    Copy-Item $corpusFile $folder
    if ($item.Label -eq 'local') {
        Copy-Item (Join-Path $root '..\assets\XrmToolsMetaAttributes.cs') $folder
    }

    $log = & dotnet build (Join-Path $folder 'Probe.csproj') -c Release --nologo -v q @($item.Args) 2>&1
    $built = $LASTEXITCODE -eq 0
    Check "the emitted attributes compile against $($item.Label)" $built (($log | Out-String).TrimEnd())
    if (-not $built) { continue }

    $out = & dotnet (Join-Path $folder 'bin\Release\net8.0\Probe.dll') 2>&1
    if ($LASTEXITCODE -ne 0) {
        Check "the probe runs against $($item.Label)" $false (($out | Out-String).TrimEnd())
        continue
    }

    $text = ($out | Out-String) -replace "`r`n", "`n"
    $dumps[$item.Label] = $text
    Set-Content -Path (Join-Path $work "dump-$($item.Label -replace '[^\w.]', '-').txt") -Value $text -Encoding UTF8 -NoNewline
}

function Get-Section {
    param([string] $Text, [string] $Name)

    $lines = $Text -split "`n"
    $keep = @()
    $inside = $false
    foreach ($line in $lines) {
        if ($line -match '^== (\w+)$') {
            $inside = $Matches[1] -eq $Name
            continue
        }
        if ($inside) { $keep += $line }
    }

    ($keep -join "`n").Trim()
}

function Compare-Section {
    param([string] $Name, [string] $Expected, [string] $Actual)

    if ($Expected -eq $Actual) { return $null }

    $left = $Expected -split "`n"
    $right = $Actual -split "`n"
    $report = @()
    for ($i = 0; $i -lt [Math]::Max($left.Count, $right.Count); $i++) {
        $a = if ($i -lt $left.Count) { $left[$i] } else { '<missing>' }
        $b = if ($i -lt $right.Count) { $right[$i] } else { '<missing>' }
        if ($a -ne $b) {
            $report += "         line $($i + 1):"
            $report += "           local:   $a"
            $report += "           package: $b"
            if ($report.Count -ge 30) { $report += '         ...'; break }
        }
    }

    ($report -join "`n")
}

# --------------------------------------------------------------- the same, or not
Write-Host ''
Write-Host 'Comparing the local definitions against the package' -ForegroundColor Cyan

if (-not $dumps.Contains('local')) {
    Check 'the local definitions produced a printout to compare against' $false
}
else {
    $baseline = $dumps['local']
    foreach ($label in @($dumps.Keys)) {
        if ($label -eq 'local') { continue }

        foreach ($section in @('DECLARED', 'EVALUATED')) {
            $expected = Get-Section -Text $baseline -Name $section
            $actual = Get-Section -Text $dumps[$label] -Name $section
            $diff = Compare-Section -Name $section -Expected $expected -Actual $actual
            Check "$section is identical between local and $label" ($null -eq $diff) $diff
        }
    }
}

# -------------------------------------------------------------------- what moved
#
# The definitions file is a subset on purpose: a tool that documents step registrations
# has no use for [CustomApi] or [Dependency], and carrying them would be four more things
# to keep in step for nothing. So the surface is judged in two halves.
#
#   A type the definitions file declares has to match upstream member for member. That is
#   the promise its header makes - delete this file, reference the package, nothing
#   changes - and it is a failure when it stops being true.
#
#   A type only upstream has is listed and left alone. It is worth knowing that [Solution]
#   exists; it is not worth failing over.
#
# Accessibility is reported separately rather than as a member difference. Upstream shipped
# these public until 1.1.4 and internal since, the definitions file is internal to match the
# newer default, and a difference against an older version is expected rather than drift.
function Split-Surface {
    param([string] $Text)

    $types = [ordered] @{}
    $current = $null
    foreach ($line in ((Get-Section -Text $Text -Name 'SURFACE') -split "`n")) {
        if ($line -match '^(public|internal) (enum|class|interface) (\w+)$') {
            $current = "$($Matches[2]) $($Matches[3])"
            $types[$current] = [pscustomobject] @{
                Accessibility = $Matches[1]
                Members       = [System.Collections.Generic.List[string]]::new()
            }
            continue
        }

        if ($current -and $line.Trim()) { $types[$current].Members.Add($line.Trim()) }
    }

    $types
}

Write-Host ''
Write-Host 'Surface report' -ForegroundColor Cyan

if ($dumps.Contains('local')) {
    $mine = Split-Surface -Text $dumps['local']

    foreach ($label in @($dumps.Keys)) {
        if ($label -eq 'local') { continue }

        $theirs = Split-Surface -Text $dumps[$label]
        $drift = @()
        $missing = @()

        foreach ($name in $mine.Keys) {
            if (-not $theirs.Contains($name)) {
                $missing += $name
                continue
            }

            $onlyTheirs = @($theirs[$name].Members | Where-Object { $mine[$name].Members -notcontains $_ })
            $onlyMine = @($mine[$name].Members | Where-Object { $theirs[$name].Members -notcontains $_ })
            foreach ($member in $onlyTheirs) { $drift += "           $name - only upstream: $member" }
            foreach ($member in $onlyMine) { $drift += "           $name - only ours:     $member" }
        }

        foreach ($name in $missing) { $drift += "           $name - upstream does not have this type at all" }

        Check "every type the definitions file declares matches $label" ($drift.Count -eq 0) ($drift -join "`n")

        $extra = @($theirs.Keys | Where-Object { -not $mine.Contains($_) })
        if ($extra.Count -gt 0) {
            Write-Host "         $label also carries $($extra.Count) type(s) the definitions file leaves out:" -ForegroundColor DarkGray
            Write-Host "           $(($extra | ForEach-Object { ($_ -split ' ')[1] }) -join ', ')" -ForegroundColor DarkGray
        }

        $flipped = @($mine.Keys | Where-Object {
            $theirs.Contains($_) -and $theirs[$_].Accessibility -ne $mine[$_].Accessibility
        })
        if ($flipped.Count -gt 0) {
            $to = $theirs[$flipped[0]].Accessibility
            Write-Host "         $label declares $($flipped.Count) of them $to, the definitions file internal." -ForegroundColor DarkGray
        }
    }
}

# ------------------------------------------------------------------------ verdict
Write-Host ''
if ($script:failures.Count -eq 0) {
    Write-Host "All checks passed. The corpus compiles and means the same thing against $($sources.Count) sources." -ForegroundColor Green
}
else {
    Write-Host "$($script:failures.Count) check(s) failed:" -ForegroundColor Red
    foreach ($failure in $script:failures) { Write-Host "  - $failure" -ForegroundColor Red }
}

if ($Keep) {
    Write-Host ''
    Write-Host "Working folder kept at $work" -ForegroundColor DarkGray
}
elseif ($script:failures.Count -eq 0) {
    Remove-Item $work -Recurse -Force
}
else {
    Write-Host ''
    Write-Host "Working folder kept at $work" -ForegroundColor DarkGray
}

exit $script:failures.Count
