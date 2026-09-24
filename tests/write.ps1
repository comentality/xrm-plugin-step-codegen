<#
.SYNOPSIS
    Headless end to end check of the write path, judged against registrations.psd1.

.DESCRIPTION
    register.ps1 and verify.ps1 pin what the environment looks like; this pins what the
    tool writes when pointed at it. It loads the tool's own DLL, rebuilds from
    registrations.psd1 exactly the PluginTypeInfo objects RegistrationQuery would have
    read out of that environment, and runs the real emit and write path over sandbox
    copies of tests\src - so everything from "which file declares this class" to "does
    the emitted attribute compile" is exercised without a Dataverse connection.

    The one thing reconstructed rather than read is the impersonating user's full name,
    which is whoever register.ps1 was signed in as; a stand-in name goes through the same
    plumbing.

    Three scenarios, matching the write reports in README.md:

      A. The default list - the two unmanaged assemblies - written in attribute mode
         twice and comment mode twice. Everything resolves to a file, the second pass of
         each mode changes nothing, and the sandbox TestPlugins project still compiles.

      B. All six assemblies. Bravo and Ghost have no source, Twin is written once per
         assembly and cannot settle, and nothing is ambiguous: the short names Duplicate
         and Rival match two files each, and the registered namespace picks the right one.

      C. The default list in comment mode with the experimental "(all columns except: ...)"
         rendering on: the emitter is handed the stand-in column universes the tool would
         have fetched, and NearlyAllColumns collapses to its exceptions while every other
         list stays exactly as it reads in scenario A.

    Sandboxes live under tests\obj\write and are rebuilt on every run.
#>
[CmdletBinding()]
param([switch] $SkipBuild)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
. (Join-Path $root 'matrix.ps1')

$manifest = Import-PowerShellDataFile (Join-Path $root 'registrations.psd1')

# ------------------------------------------------------------------ the tool itself
$project = Join-Path $root '..\PluginStepCodegen\PluginStepCodegen.csproj'
if (-not $SkipBuild) {
    dotnet build $project -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'The tool did not build.' }
}

$dll = Resolve-Path (Join-Path $root '..\PluginStepCodegen\bin\Debug\net48\PluginStepCodegen.dll')
[Reflection.Assembly]::LoadFrom($dll) | Out-Null

# ------------------------------------------------------------------------ sandboxes
# Not under tests\obj: the tool skips any path with \obj\ in it as build output, so a
# sandbox there would make every class report "no matching .cs file".
$work = Join-Path $root '.write'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }

# TestPlugins.csproj links ..\..\..\assets\XrmToolsMetaAttributes.cs - the very file the
# tool emits - so the compile check needs the assets folder two levels above each
# sandbox's src, exactly where the repository keeps it relative to tests\src.
New-Item -ItemType Directory -Path (Join-Path $work 'assets') -Force | Out-Null
Copy-Item (Join-Path $root '..\assets\XrmToolsMetaAttributes.cs') (Join-Path $work 'assets')

function New-Sandbox {
    param([string] $Name)

    $dest = Join-Path $work "$Name\src"
    robocopy (Join-Path $root 'src') $dest /MIR /XD obj bin /XF *.bak /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with $LASTEXITCODE" }
    $dest
}

function Get-SourceHashes {
    param([string] $Folder)

    $hashes = @{}
    foreach ($file in Get-ChildItem $Folder -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }) {
        $hashes[$file.FullName.Substring($Folder.Length + 1)] = (Get-FileHash $file.FullName).Hash
    }
    $hashes
}

# --------------------------------------------------------- the stand-in column universes
# The dynamic filters expand against the live table when register.ps1 writes them; here
# they expand against these, the same way the impersonating user's name is a stand-in.
# Each is a plausible slice of the real table, sized so that every literal list in the
# matrix stays on the same side of the quarter rule it is on in a real environment - the
# WideRegistration ten names are a minority of account's columns here too.
function New-StandInColumns {
    param([string[]] $Updatable, [string[]] $ReadOnly)

    $filter = @($Updatable)
    [Array]::Sort($filter, [StringComparer]::Ordinal)
    $image = @($Updatable + $ReadOnly)
    [Array]::Sort($image, [StringComparer]::Ordinal)
    @{ Filter = $filter; Image = $image }
}

$standInColumns = @{
    account = New-StandInColumns -ReadOnly @(
        'accountid', 'createdby', 'createdon', 'modifiedby', 'modifiedon'
    ) -Updatable @(
        'accountcategorycode', 'accountnumber', 'accountratingcode', 'address1_city',
        'address1_country', 'address1_line1', 'address1_line2', 'address1_postalcode',
        'address1_stateorprovince', 'creditlimit', 'customersizecode', 'description',
        'emailaddress1', 'fax', 'industrycode', 'name', 'numberofemployees', 'ownerid',
        'ownershipcode', 'parentaccountid', 'revenue', 'sic', 'telephone1',
        'tickersymbol', 'websiteurl'
    )
    annotation = New-StandInColumns -ReadOnly @(
        'annotationid', 'createdby', 'createdon', 'modifiedby', 'modifiedon'
    ) -Updatable @(
        'documentbody', 'filename', 'isdocument', 'langid', 'mimetype', 'notetext',
        'objectid', 'objecttypecode', 'ownerid', 'subject'
    )
    contact = New-StandInColumns -ReadOnly @(
        'contactid', 'createdby', 'createdon', 'modifiedby', 'modifiedon'
    ) -Updatable @(
        'assistantname', 'description', 'emailaddress1', 'firstname', 'jobtitle',
        'lastname', 'middlename', 'mobilephone', 'ownerid', 'parentcustomerid',
        'salutation', 'telephone1'
    )
    task = New-StandInColumns -ReadOnly @(
        'activityid', 'createdby', 'createdon', 'modifiedby', 'modifiedon'
    ) -Updatable @(
        'actualdurationminutes', 'category', 'description', 'ownerid', 'prioritycode',
        'regardingobjectid', 'scheduledend', 'scheduledstart', 'subcategory', 'subject'
    )
}

# The same universes in the shape RemarksEmitter takes, for the run that hands the
# emitter the columns the tool would have fetched.
function ConvertTo-EmitterColumns {
    param([hashtable] $StandIn)

    $dictionary = New-Object 'System.Collections.Generic.Dictionary[string,PluginStepCodegen.Logic.EntityColumnsInfo]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($entity in $StandIn.Keys) {
        $info = New-Object PluginStepCodegen.Logic.EntityColumnsInfo
        foreach ($column in $StandIn[$entity].Image) { $info.ImageColumns.Add($column) }
        foreach ($column in $StandIn[$entity].Filter) { $info.FilterColumns.Add($column) }
        $dictionary[$entity] = $info
    }
    , $dictionary
}

# ------------------------------------------------- registrations.psd1 -> the tool's model
# Mirrors RegistrationQuery: only types with steps, types by name, steps in execution order
# - stage, rank, message, and the table as the last tiebreak; a step keeps the name
# Dataverse generated unless the fixture typed one. Regrouping by table is the summary
# comment's own doing, further down, and this must not do it here or the comment's order
# would be pinned against an order the tool never sees.
function Get-TypeModels {
    param([hashtable] $Assembly)

    # A description imported in a solution loses its CRLF: XML normalises line endings
    # inside an element. The unmanaged route keeps them. See Contoso.Crm.Charlie.
    $managed = $null -ne $Assembly.Solution

    foreach ($t in $Assembly.Types | Sort-Object { $_.Name }) {
        $steps = @($Assembly.Steps | Where-Object { $_.Type -eq $t.Name })
        if ($steps.Count -eq 0) { continue }

        $type = New-Object PluginStepCodegen.Logic.PluginTypeInfo
        $type.Id = [guid](Get-TypeId $Assembly $t)
        $type.AssemblyId = [guid](Get-AssemblyId $Assembly)
        $type.TypeName = $t.Name
        $type.FriendlyName = Get-FriendlyName $Assembly $t.Name
        if ($t.ContainsKey('Description')) { $type.Description = $t.Description }

        $ordered = $steps | Sort-Object { $_.Stage }, { $_.Rank }, { $_.Message }, { Get-StepEntity $_ }

        foreach ($s in $ordered) {
            $step = New-Object PluginStepCodegen.Logic.PluginStepInfo
            $step.Id = [guid](Get-StepId $Assembly $s)
            $step.MessageName = $s.Message
            $step.PrimaryEntityName = if ($s.ContainsKey('Entity')) { $s.Entity } else { $null }
            $step.Stage = $s.Stage
            $step.Mode = $s.Mode
            $step.Rank = $s.Rank
            $step.Name = Get-StepName $s
            $step.FilteringAttributes = Get-StepFilter $s $standInColumns
            $step.Configuration = Get-StepValue $s 'Configuration' $null
            $step.AsyncAutoDelete = [bool](Get-StepValue $s 'AsyncAutoDelete' $false)
            $step.IsDisabled = [bool](Get-StepValue $s 'Disabled' $false)
            if (Get-StepValue $s 'Impersonate' $false) { $step.ImpersonatingUser = 'E2E Test User' }

            $description = Get-StepValue $s 'Description' $null
            if ($null -ne $description) {
                $step.Description = if ($managed) { $description -replace "`r`n", "`n" } else { $description }
            }

            foreach ($i in (Get-StepImages $s) | Sort-Object { $_.Type }) {
                $image = New-Object PluginStepCodegen.Logic.PluginImageInfo
                $image.ImageType = $i.Type
                $image.EntityAlias = $i.Alias
                $image.Name = $i.Name
                $image.Attributes = Get-ImageAttributes $s $i $standInColumns
                $image.MessagePropertyName = Get-StepValue $i 'Property' 'Target'
                $step.Images.Add($image)
            }

            $type.Steps.Add($step)
        }

        $type
    }
}

# Assemblies in the order the tool lists them - by name - each contributing its types.
function Get-CheckedTypes {
    param([scriptblock] $Where)

    , @(foreach ($assembly in $manifest.Assemblies | Where-Object $Where | Sort-Object { $_.Name }) {
        Get-TypeModels $assembly
    })
}

# --------------------------------------------------------------------- one Write press
# What BtnWrite_Click does, by calling what it calls: SourceWriter reads the folder once
# and holds every class in the batch against it, and hands back the report the preview
# pane prints. Only the output mode is decided here, which is the one thing the button
# reads off its own radio buttons.
function Invoke-WriteRun {
    param([string] $Folder, $Types, [ValidateSet('Attributes', 'Comment')] [string] $Mode, $Columns = $null)

    # One object rather than two lists: a scriptblock returning a List<string> unrolls it
    # into an object[] on the way out, which does not bind back to IEnumerable<string>.
    $output = [Func[PluginStepCodegen.Logic.PluginTypeInfo, PluginStepCodegen.Logic.ClassOutput]] {
        param($type)

        $given = New-Object PluginStepCodegen.Logic.ClassOutput
        if ($Mode -eq 'Comment') { $given.Remarks = [PluginStepCodegen.Logic.RemarksEmitter]::Emit($type, $Columns) }
        if ($Mode -eq 'Attributes') { $given.Attributes = [PluginStepCodegen.Logic.AttributeEmitter]::Emit($type) }
        $given
    }

    $typeList = New-Object 'System.Collections.Generic.List[PluginStepCodegen.Logic.PluginTypeInfo]'
    foreach ($type in $Types) { $typeList.Add($type) }

    $report = [PluginStepCodegen.Logic.SourceWriter]::Write($Folder, $typeList, $false, $output)

    [ordered]@{
        Updated   = @($report.Written)
        Unchanged = @($report.Unchanged)
        NotFound  = @($report.NotFound)
        Ambiguous = @($report.Ambiguous)
        Failed    = @($report.Failed)
    }
}

# ------------------------------------------------------------------------- the checks
$script:passed = 0
$script:failures = @()

function Check {
    param([string] $Description, [bool] $Condition, [string] $Detail = '')

    if ($Condition) {
        $script:passed++
        Write-Host "  ok    $Description"
    } else {
        $script:failures += "$Description $Detail".Trim()
        Write-Host "  FAIL  $Description  $Detail" -ForegroundColor Red
    }
}

function Format-Report {
    param($Report)

    $counts = (@('Updated', 'Unchanged', 'NotFound', 'Ambiguous', 'Failed') |
        ForEach-Object { "$_=$(@($Report[$_]).Count)" }) -join ' '
    if (@($Report.Failed).Count -gt 0) { $counts += "; first failure: $($Report.Failed[0])" }
    $counts
}

function Test-FileContains {
    param([string] $Folder, [string] $Path, [string] $Needle, [bool] $Expected = $true)

    $content = Get-Content (Join-Path $Folder $Path) -Raw
    $verb = if ($Expected) { 'contains' } else { 'does not contain' }
    Check "$Path $verb $Needle" (($content.Contains($Needle)) -eq $Expected)
}

# =========================================================== A. the default list
Write-Host "Scenario A - the default list, attribute mode then comment mode, twice each"

$sandboxA = New-Sandbox 'a'
$before = Get-SourceHashes $sandboxA
$defaultTypes = Get-CheckedTypes { $null -eq $_.Solution }

Check 'seventeen classes in the default list' ($defaultTypes.Count -eq 17) "(got $($defaultTypes.Count))"

$a1 = Invoke-WriteRun $sandboxA $defaultTypes 'Attributes'
Check 'attributes: all seventeen updated, nothing skipped' `
    (@($a1.Updated).Count -eq 17 -and @($a1.Ambiguous).Count -eq 0 -and
     @($a1.NotFound).Count -eq 0 -and @($a1.Failed).Count -eq 0) "($(Format-Report $a1))"

$a2 = Invoke-WriteRun $sandboxA $defaultTypes 'Attributes'
Check 'attributes again: all seventeen already up to date' `
    (@($a2.Unchanged).Count -eq 17 -and @($a2.Updated).Count -eq 0) "($(Format-Report $a2))"

# --- The generic plugin: five tables, ten steps, and the two orders they come out in.
# The attributes are what Xrm Tools reads back, so they stay in execution order however
# it interleaves the tables. Read now, before the comment run rewrites the file.
$stamper = Get-Content (Join-Path $sandboxA 'TestPlugins\Plugins\StatusDateStamper.cs') -Raw
$stamperSteps = @([regex]::Matches($stamper, '\[Step\("(?<message>\w+)", "(?<entity>\w+)"') |
    ForEach-Object { "$($_.Groups['entity'].Value)/$($_.Groups['message'].Value)" })
$expectedStamper = @(
    'annotation/Update'                          # the only PreOperation step
    'account/Create', 'annotation/Create', 'contact/Create', 'email/Create', 'task/Create'
    'account/Update', 'email/Update', 'task/Update'
    'contact/Update'                             # rank 2, so last
)
Check 'the attributes stay in execution order, tables interleaved' `
    (($stamperSteps -join ' ') -eq ($expectedStamper -join ' ')) "(got $($stamperSteps -join ' '))"

$a3 = Invoke-WriteRun $sandboxA $defaultTypes 'Comment'
Check 'comment: all seventeen updated' (@($a3.Updated).Count -eq 17) "($(Format-Report $a3))"

$a4 = Invoke-WriteRun $sandboxA $defaultTypes 'Comment'
Check 'comment again: all seventeen already up to date' `
    (@($a4.Unchanged).Count -eq 17 -and @($a4.Updated).Count -eq 0) "($(Format-Report $a4))"

# The same ten steps, off the same list, in the order the comment reads them: by table,
# alphabetically, each table keeping the execution order it arrived in - which is why
# annotation reads update before create.
Test-FileContains $sandboxA 'TestPlugins\Plugins\StatusDateStamper.cs' `
    '/// Sync Post-Create of account (order 1): (all columns)'
$stamperComment = Get-Content (Join-Path $sandboxA 'TestPlugins\Plugins\StatusDateStamper.cs') -Raw
$commentSteps = @([regex]::Matches($stamperComment, '///\s+(?:Sync|Async) \w+-(?<message>\w+) of (?<entity>\w+)') |
    ForEach-Object { "$($_.Groups['entity'].Value)/$($_.Groups['message'].Value)" })
$expectedComment = @(
    'account/Create', 'account/Update'
    'annotation/Update', 'annotation/Create'   # PreOperation before PostOperation
    'contact/Create', 'contact/Update'         # rank 1 before rank 2
    'email/Create', 'email/Update'
    'task/Create', 'task/Update'
)
Check 'the comment regroups the same steps by table' `
    (($commentSteps -join ' ') -eq ($expectedComment -join ' ')) "(got $($commentSteps -join ' '))"

# The attributes written by the earlier run are still in the file and still in their own
# order: the two modes are independent, and regrouping is only one of them doing it.
$stamperStill = @([regex]::Matches($stamperComment, '\[Step\("(?<message>\w+)", "(?<entity>\w+)"') |
    ForEach-Object { "$($_.Groups['entity'].Value)/$($_.Groups['message'].Value)" })
Check 'writing the comment left the attribute order alone' `
    (($stamperStill -join ' ') -eq ($expectedStamper -join ' ')) "(got $($stamperStill -join ' '))"

# A step with no table to group under still emits both ways: the entity is what the
# ordering now keys on, and a global message has none.
Test-FileContains $sandboxA 'TestPlugins\Plugins\GlobalMessageHandler.cs' `
    '[Step("Associate", Stages.PreValidation, ExecutionMode.Synchronous)]'
Test-FileContains $sandboxA 'TestPlugins\Plugins\GlobalMessageHandler.cs' `
    '/// Sync PreValidation-Associate (order 1)'

# The namespace settles both short name collisions in this view.
Test-FileContains $sandboxA 'TestPlugins\Plugins\Duplicates\AlphaDuplicate.cs' `
    '[Step("Create", "annotation", Stages.PostOperation, ExecutionMode.Synchronous)]'
Test-FileContains $sandboxA 'TestPlugins\Plugins\Rival.cs' `
    '[Step("Delete", "task", Stages.PreOperation, ExecutionMode.Synchronous)]'

# The impersonation stand-in went through the same plumbing the real name would.
Test-FileContains $sandboxA 'TestPlugins\Plugins\DisabledAndImpersonated.cs' 'E2E Test User'

# Exactly the seventeen files, and nothing else, changed.
$expectedChangedA = @(
    'TestPlugins\Plugins\SimpleCreate.cs'
    'TestPlugins\Plugins\StatusDateStamper.cs'
    'TestPlugins\Plugins\FilteredUpdate.cs'
    'TestPlugins\Plugins\AsyncWorker.cs'
    'TestPlugins\Plugins\GlobalMessageHandler.cs'
    'TestPlugins\Plugins\ImageShapes.cs'
    'TestPlugins\Plugins\DisabledAndImpersonated.cs'
    'TestPlugins\Plugins\EscapedText.cs'
    'TestPlugins\Plugins\WideRegistration.cs'
    'TestPlugins\Plugins\HandWritten.cs'
    'TestPlugins\Plugins\Duplicates\AlphaDuplicate.cs'
    'TestPlugins\Plugins\NearlyAllColumns.cs'
    'TestPlugins\Plugins\Rival.cs'
    'Shared\Twin.cs'
    'WorkInProgressPlugins\NewFeature.cs'
    'WorkInProgressPlugins\HalfFinished.cs'
    'WorkInProgressPlugins\Scratch.cs'
)
$after = Get-SourceHashes $sandboxA
$changed = @($before.Keys | Where-Object { $before[$_] -ne $after[$_] } | Sort-Object)
$unexpected = @($changed | Where-Object { $expectedChangedA -notcontains $_ })
$unwritten = @($expectedChangedA | Where-Object { $changed -notcontains $_ })
Check 'exactly the seventeen expected files changed' `
    ($unexpected.Count -eq 0 -and $unwritten.Count -eq 0) `
    "(unexpected: $($unexpected -join ', '); missed: $($unwritten -join ', '))"

Check 'no .bak copy was left anywhere' `
    (@(Get-ChildItem $sandboxA -Recurse -Filter *.bak).Count -eq 0)

# The emitted attributes compile - against the very definitions file the tool drops.
dotnet build (Join-Path $sandboxA 'TestPlugins') -c Debug --nologo -v q | Out-Null
Check 'the written TestPlugins project compiles' ($LASTEXITCODE -eq 0)

# ========================================================== B. all six assemblies
Write-Host "Scenario B - all six assemblies, attribute mode, twice"

$sandboxB = New-Sandbox 'b'
$beforeB = Get-SourceHashes $sandboxB
$allTypes = Get-CheckedTypes { $true }

Check 'twenty four classes with all assemblies ticked' ($allTypes.Count -eq 24) "(got $($allTypes.Count))"

$b1 = Invoke-WriteRun $sandboxB $allTypes 'Attributes'
Check 'twenty two updated, none ambiguous' `
    (@($b1.Updated).Count -eq 22 -and @($b1.Ambiguous).Count -eq 0 -and @($b1.Failed).Count -eq 0) `
    "($(Format-Report $b1))"
Check 'only Bravo and Ghost have no source' `
    ((@($b1.NotFound) | Sort-Object) -join ',' -eq 'Bravo,Ghost') "(got $($b1.NotFound -join ', '))"
Check 'Twin written once per assembly' (@($b1.Updated | Where-Object { $_ -eq 'Twin' }).Count -eq 2)

$b2 = Invoke-WriteRun $sandboxB $allTypes 'Attributes'
Check 'second run: only Twin cannot settle' `
    ((@($b2.Updated) -join ',') -eq 'Twin,Twin' -and @($b2.Unchanged).Count -eq 20) "($(Format-Report $b2))"

# Each Rival registration landed in its own file, never the other's.
Test-FileContains $sandboxB 'TestPlugins\Plugins\Rival.cs' `
    '[Step("Delete", "task", Stages.PreOperation, ExecutionMode.Synchronous)]'
Test-FileContains $sandboxB 'TestPlugins\Plugins\Rival.cs' '"contact"' $false
Test-FileContains $sandboxB 'ContosoPlugins\Rival.cs' `
    '[Step("Delete", "contact", Stages.PreOperation, ExecutionMode.Synchronous)]'
Test-FileContains $sandboxB 'ContosoPlugins\Rival.cs' '"task"' $false

# Twin holds the registration written last: TestPlugins', in assembly order.
Test-FileContains $sandboxB 'Shared\Twin.cs' 'ExecutionOrder = 3'
Test-FileContains $sandboxB 'Shared\Twin.cs' 'ExecutionOrder = 4' $false

# The files no run may touch came back byte for byte identical.
$afterB = Get-SourceHashes $sandboxB
foreach ($path in @(
    'TestPlugins\Plugins\Duplicates\BetaDuplicate.cs'
    'TestPlugins\Plugins\NeverRegistered.cs'
    'ContosoPlugins\StaleDoc.cs'
    'ContosoPlugins\Untouched.cs'
)) {
    Check "$path untouched" ($beforeB[$path] -eq $afterB[$path])
}

# ==================================== C. comment mode with the column universes
Write-Host "Scenario C - comment mode with the experimental except rendering, twice"

$sandboxC = New-Sandbox 'c'
$emitterColumns = ConvertTo-EmitterColumns $standInColumns

$c1 = Invoke-WriteRun $sandboxC $defaultTypes 'Comment' $emitterColumns
Check 'comment with columns: all seventeen updated' `
    (@($c1.Updated).Count -eq 17 -and @($c1.Failed).Count -eq 0) "($(Format-Report $c1))"

$c2 = Invoke-WriteRun $sandboxC $defaultTypes 'Comment' $emitterColumns
Check 'comment with columns again: all seventeen already up to date' `
    (@($c2.Unchanged).Count -eq 17 -and @($c2.Updated).Count -eq 0) "($(Format-Report $c2))"

# The three shapes, one step each: near-complete says its exceptions, complete says it
# is complete, and a name outside the universe - createdon is real but not updatable -
# keeps its list verbatim, because the odd name out is the finding.
Test-FileContains $sandboxC 'TestPlugins\Plugins\NearlyAllColumns.cs' '(all columns except: notetext, subject)'
Test-FileContains $sandboxC 'TestPlugins\Plugins\NearlyAllColumns.cs' '(all columns except: documentbody)'
Test-FileContains $sandboxC 'TestPlugins\Plugins\NearlyAllColumns.cs' '(all 10 columns, written out)'
Test-FileContains $sandboxC 'TestPlugins\Plugins\NearlyAllColumns.cs' 'createdon, firstname, lastname'

# The quarter rule, judged with the universe present rather than absent: ten of account's
# twenty five stand-in columns is nowhere near "all", so the list stays a list.
Test-FileContains $sandboxC 'TestPlugins\Plugins\WideRegistration.cs' '(all columns except' $false
Test-FileContains $sandboxC 'TestPlugins\Plugins\WideRegistration.cs' 'accountcategorycode, accountnumber'

# A short list stays a list, and no filter at all still reads "(all columns)" rather
# than "written out" - a registration with no list follows the table as it grows.
Test-FileContains $sandboxC 'TestPlugins\Plugins\FilteredUpdate.cs' 'firstname, lastname, emailaddress1'
Test-FileContains $sandboxC 'TestPlugins\Plugins\SimpleCreate.cs' '(all columns)'

# ------------------------------------------------------------------------------ tally
Write-Host ''
if ($script:failures.Count -eq 0) {
    Write-Host "All $script:passed checks passed." -ForegroundColor Green
    exit 0
}

Write-Host "$($script:failures.Count) of $($script:passed + $script:failures.Count) checks failed:" -ForegroundColor Red
$script:failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
exit 1
