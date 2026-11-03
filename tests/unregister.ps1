<#
.SYNOPSIS
    Removes everything register.ps1 put in the environment.

.DESCRIPTION
    Deletes all three fixture solutions, companion first. Because they are managed,
    deleting them takes the steps, images, plugin types and the assemblies themselves with
    them - which an unmanaged solution would not: its components stay behind in the
    Default solution.

    Then the assembly that was registered without a solution, which has nothing to delete
    but its records: images, steps, plugin types and the assembly, in that order, because
    none of them will go while something still points at it.

    The publishers the solutions were imported under are left alone. They own nothing once
    the solutions are gone, and pac has no command for deleting one.

    Neither a solution nor a record that is not there is an error; this is safe to run at
    any time, including twice.
#>
[CmdletBinding()]
param([string] $Environment)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'unmanaged.ps1')

$manifest = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'registrations.psd1')
$envArgs = if ($Environment) { @('--environment', $Environment) } else { @() }

# Companion first: its steps run against plugin types the other solutions own.
$names = @($manifest.DisabledSolution.Name) + @($manifest.Solutions.Name)

foreach ($name in $names) {
    Write-Host "Deleting $name..."
    $output = & pac @(@('solution', 'delete', '--solution-name', $name) + $envArgs) 2>&1 |
        ForEach-Object { "$_" }

    if ($LASTEXITCODE -eq 0) {
        continue
    }

    if ($output -match 'not found|does not exist|Cannot find') {
        Write-Host "  not installed, nothing to remove."
        continue
    }

    throw "Deleting $name failed:`r`n$($output -join "`r`n")"
}

Unregister-Unmanaged -Manifest $manifest -Environment $Environment

# Asked of the environment rather than assumed. pac exits clean for a solution that is not
# there, so a fixture installed under a name this manifest no longer uses - the solutions were
# PluginDocumenterE2E* once - sails through the loop above and stays. Every fixture id starts
# the same way, so whatever is left can be found without knowing what it was installed as.
$left = @(Get-DataverseRecords 'pluginassemblies?$select=name,pluginassemblyid' | ForEach-Object { $_ } |
    Where-Object { $_.pluginassemblyid -like '9a5b3c10-*' })
if ($left.Count -gt 0) {
    throw "Still registered, under a solution this manifest does not name: $(($left.name) -join ', ')"
}

Write-Host ''
Write-Host 'Unregistered. Every fixture assembly and all of their steps are gone.'

# Explicit, because a solution that was not installed leaves pac's non zero exit code
# behind and this script would otherwise inherit it after saying everything went fine.
exit 0
