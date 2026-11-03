# Screenshots the tool's UI at a few window sizes, without XrmToolBox and without a connection.
#
# The layout is built in code and most of it only misbehaves at a size you did not try, so this
# hosts PluginStepCodegenControl in a bare form, fills it with sample rows and grabs a PNG per size.
# Shots land in tests\.ui and are overwritten each run.
#
#   .\ui.ps1                          # default sizes
#   .\ui.ps1 -Size 1600x1000          # one size
#   .\ui.ps1 -Size 1280x900,820x620   # several
#   .\ui.ps1 -NoBuild                 # reuse the last build
#   .\ui.ps1 -Docs                    # regenerate the two screenshots in the README
#
# It drives the real control, so it screenshots real bugs: a splitter that throws while being set
# up shows here as a failed run rather than as a broken tab in XrmToolBox.
#
# The windows open off screen and never take the keyboard, so a run does not interrupt whoever is
# at the machine. Set PSCG_HARNESS_ONSCREEN=1 to watch one being taken.
#
# -Docs writes assets\ui-attributes.png and assets\ui-comment.png, which README.md and docs\ point
# at. They are the same control and the same sample data as every other shot; only the window
# title, the source folder shown in the box and the output mode differ, so a change to the layout
# turns up in the README by re-running this rather than by somebody remembering to.

param(
    [string[]]$Size = @("1280x900", "820x620"),
    [string]$OutputDir,
    [switch]$NoBuild,
    [switch]$Docs
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "PluginStepCodegen.UiHarness\PluginStepCodegen.UiHarness.csproj"
$exe     = Join-Path $PSScriptRoot "PluginStepCodegen.UiHarness\bin\Debug\net48\PluginStepCodegen.UiHarness.exe"
if (-not $OutputDir) {
    $OutputDir = if ($Docs) { Join-Path (Split-Path $PSScriptRoot -Parent) "assets" } else { Join-Path $PSScriptRoot ".ui" }
}

# A path that reads like somebody's plugin project rather than like this machine. It has to exist,
# because the control greys its buttons out and says so for a folder that does not - which is the
# right render, and the wrong screenshot. When created here it is created empty, and the harness
# then seeds it with sample source files for the scan column; it is removed again below.
$docsFolder = "C:\src\Contoso.Crm"

if (-not $NoBuild) {
    dotnet build $project -c Debug -v q --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Each shot is width, height, output path, and then what makes it different from the last one.
$shots = if ($Docs) {
    @(
        @{ Size = "1280x860"; Name = "ui-attributes.png"; Mode = "attributes"; Folder = $docsFolder; Title = "Plugin Step Codegen" }
        @{ Size = "1280x860"; Name = "ui-comment.png";    Mode = "comment";    Folder = $docsFolder; Title = "Plugin Step Codegen" }
    )
} else {
    @($Size | ForEach-Object { @{ Size = $_; Name = $null; Mode = "attributes"; Folder = $null; Title = $null } })
}

$madeDocsFolder = $false
if ($Docs -and -not (Test-Path $docsFolder)) {
    New-Item -ItemType Directory -Force -Path $docsFolder | Out-Null
    $madeDocsFolder = $true
}

$failed = $false
try {
    foreach ($shot in $shots) {
        if ($shot.Size -notmatch '^\s*(\d+)\s*[x×]\s*(\d+)\s*$') { throw "Size must look like 1280x900, got '$($shot.Size)'" }
        $width  = [int]$Matches[1]
        $height = [int]$Matches[2]

        $name = if ($shot.Name) { $shot.Name } else { "$($width)x$($height).png" }
        $path = Join-Path $OutputDir $name
        Remove-Item "$path*" -ErrorAction SilentlyContinue

        # Quoted, because a path or a title with a space in it otherwise arrives as two arguments
        # and the window ends up titled "Plugin".
        $arguments = @($width, $height, "`"$path`"", $shot.Mode)
        if ($shot.Folder) { $arguments += "`"$($shot.Folder)`"" }
        if ($shot.Title)  { $arguments += "`"$($shot.Title)`"" }

        # Out of process with a timeout: a layout that deadlocks the message loop should fail the
        # run rather than hang it.
        $p = Start-Process $exe -ArgumentList $arguments -PassThru
        if (-not $p.WaitForExit(30000)) {
            $p.Kill()
            Write-Host "TIMED OUT  $name" -ForegroundColor Red
            $failed = $true
            continue
        }

        if ($p.ExitCode -ne 0) {
            Write-Host "FAILED     $name" -ForegroundColor Red
            if (Test-Path "$path.error.txt") { Get-Content "$path.error.txt" | Select-Object -First 12 }
            $failed = $true
            continue
        }

        Write-Host "$($width)x$($height)  ->  $path"
    }
} finally {
    if ($madeDocsFolder) { Remove-Item $docsFolder -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($failed) { exit 1 }
