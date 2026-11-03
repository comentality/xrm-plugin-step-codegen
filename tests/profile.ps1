# A profiled step has to read exactly as it did before anybody profiled it.
#
# Start Profiling in the plugin registration tool renames the step "<name> (Profiled)", switches
# it off, and moves its images onto a stand-in step registered against the profiler's own plugin
# type. Read at face value that is a disabled step with a strange name and no images - a
# description of somebody's debugging session, not of the plugin.
#
#   .\profile.ps1                  # against the active organization of the pac auth profile
#   .\profile.ps1 -Environment <url>
#   .\profile.ps1 -NoBuild         # reuse the last build
#
# Needs the fixture registered (register.ps1). It profiles every enabled fixture step with the
# registration tool's own library - the same calls the button makes, out of the package the
# registration tool ships in - reads the fixture through RegistrationQuery before, during and
# after, and requires the three readings to be identical down to every line both emitters write.
# The first run installs the profiler solution, which is left installed afterwards; profiling
# itself is always stopped again, including after a run that died half way.
#
# The readings are left in tests\.profile for a diff, and the exit code is the number of findings.

param(
    [string]$Environment,
    [string]$OutputDir,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot 'dataverse.ps1')

$project = Join-Path $PSScriptRoot "PluginStepCodegen.ProfileHarness\PluginStepCodegen.ProfileHarness.csproj"
$exe     = Join-Path $PSScriptRoot "PluginStepCodegen.ProfileHarness\bin\Debug\net48\PluginStepCodegen.ProfileHarness.exe"
if (-not $OutputDir) { $OutputDir = Join-Path $PSScriptRoot ".profile" }

if (-not $NoBuild) {
    dotnet build $project -c Debug -v q --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# The harness is handed the token pac already holds rather than signing in on its own, the same
# way unmanaged.ps1 writes records: there is still nothing to authenticate beyond pac auth.
$url = Connect-Dataverse -Environment $Environment
$env:PSCG_DATAVERSE_URL = $url
$env:PSCG_DATAVERSE_TOKEN = Get-DataverseToken
try {
    & $exe $OutputDir
    $code = $LASTEXITCODE
} finally {
    Remove-Item Env:PSCG_DATAVERSE_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:PSCG_DATAVERSE_URL -ErrorAction SilentlyContinue
}

exit $code
