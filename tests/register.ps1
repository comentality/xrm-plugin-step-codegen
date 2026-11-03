<#
.SYNOPSIS
    Registers the whole test matrix in a Dataverse environment.

.DESCRIPTION
    Builds the fixture solutions and imports them:

      PluginStepCodegenE2E          publisher Comentality:
                                   Microsoft.Contoso.Crm.Extensions.Plugins,
                                   its plugin type and its step, imported with
                                   --activate-plugins
      PluginStepCodegenE2EContoso   publisher Contoso: the three Contoso assemblies and
                                   their enabled steps, also with --activate-plugins
      PluginStepCodegenE2EDisabled  the steps that are meant to stay switched off,
                                   imported without it

    All three are managed, so unregister.ps1 can take them away again completely. The
    companion goes last because its steps run against plugin types the others install.

    Then the two assemblies that are in no solution at all, written record by record over
    the Web API the way the plugin registration tool writes one into a development
    environment. That is the shape the tool is really used against - it is what the
    assembly list shows before any switch is touched - and the only one the solution route
    cannot produce; see unmanaged.ps1.

    Safe to run repeatedly: importing the same solution again updates it in place, and the
    unmanaged records are written at ids of the fixture's own choosing.

.EXAMPLE
    .\register.ps1
    Registers against the active organization of the current pac auth profile.

.EXAMPLE
    .\register.ps1 -Environment https://contoso.crm.dynamics.com
#>
[CmdletBinding()]
param(
    [string] $Environment,
    [switch] $SkipAssemblyBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
. (Join-Path $root 'unmanaged.ps1')

$manifest = Import-PowerShellDataFile (Join-Path $root 'registrations.psd1')
$envArgs = if ($Environment) { @('--environment', $Environment) } else { @() }

$zips = & (Join-Path $root 'build.ps1') -Environment $Environment -SkipAssemblyBuild:$SkipAssemblyBuild

function Import-Fixture {
    param([string] $Path, [switch] $Activate)

    $arguments = @('solution', 'import', '--path', $Path) + $envArgs
    if ($Activate) { $arguments += '--activate-plugins' }

    Write-Host "Importing $(Split-Path $Path -Leaf)$(if ($Activate) { ' (activating steps)' })..."
    $output = & pac @arguments 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        throw "Import of $Path failed:`r`n$($output -join "`r`n")"
    }
}

foreach ($zip in $zips.Main) {
    Import-Fixture -Path $zip -Activate
}

if ($zips.Companion) {
    Import-Fixture -Path $zips.Companion
}

Register-Unmanaged -Manifest $manifest -Built $zips.Built -Environment $Environment

Write-Host ''
Write-Host 'Registered. Point the Plugin Step Codegen at the source folder:'
Write-Host "  $(Join-Path $root 'src')"
Write-Host 'Run verify.ps1 to confirm the environment matches registrations.psd1.'
