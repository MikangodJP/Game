param(
    [string]$Dotnet,
    [string]$Godot,
    [switch]$Verify
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$Dotnet = Resolve-Dotnet8 -RequestedPath $Dotnet
Initialize-DotnetEnvironment -Dotnet $Dotnet -ProjectRoot $projectRoot

if (-not $Godot) {
    $godotCandidates = [System.Collections.Generic.List[string]]::new()
    if ($IsWindows) {
        $godotCandidates.Add((Join-Path $projectRoot '.tools' 'godot' 'Godot_v4.6.3-stable_mono_win64' 'Godot_v4.6.3-stable_mono_win64_console.exe'))
    } elseif ($IsMacOS) {
        $systemRoot = [IO.Path]::GetPathRoot($projectRoot)
        $godotCandidates.Add((Join-Path $projectRoot '.tools' 'godot' 'Godot_mono.app' 'Contents' 'MacOS' 'Godot'))
        $godotCandidates.Add((Join-Path $systemRoot 'Applications' 'Godot_mono.app' 'Contents' 'MacOS' 'Godot'))
    } else {
        $godotCandidates.Add((Join-Path $projectRoot '.tools' 'godot' 'Godot'))
    }

    foreach ($commandName in @('godot-mono', 'godot4', 'godot')) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue
        if ($command) {
            $godotCandidates.Add($command.Source)
        }
    }

    foreach ($candidate in $godotCandidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $Godot = $candidate
            break
        }
    }
}

if (-not $Godot) {
    throw 'Godot 4.6.3 .NET was not found. Install it as described in probes/phase1a/README.md or pass -Godot <path>.'
}
$Godot = (Get-Command $Godot -ErrorAction Stop).Source
$godotVersion = & $Godot --version
if ($LASTEXITCODE -ne 0 -or $godotVersion -notmatch '^4\.6\.3\..*mono') {
    throw "Godot 4.6.3 .NET is required; '$Godot' reported '$godotVersion'."
}

$visualRoot = Join-Path $PSScriptRoot 'Visual'
$artifacts = Join-Path $PSScriptRoot 'artifacts' 'visual'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

if ($Verify) {
    & (Join-Path $PSScriptRoot 'verify.ps1') -Dotnet $Dotnet -Configuration Debug
    & (Join-Path $PSScriptRoot 'verify.ps1') -Dotnet $Dotnet -Configuration Release
    $modelTests = Join-Path $PSScriptRoot 'VisualTests' 'Phase1A.VisualTests.csproj'
    & $Dotnet restore $modelTests --configfile (Join-Path $PSScriptRoot 'NuGet.Config') -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Visual model test restore failed.' }
    & $Dotnet build $modelTests --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Visual model test build failed.' }
    & $Dotnet (Join-Path $PSScriptRoot 'VisualTests' 'bin' 'Debug' 'net8.0' 'Phase1A.VisualTests.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Visual model tests failed.' }
}

$visualProject = Join-Path $visualRoot 'Visual.csproj'
& $Dotnet restore $visualProject --configfile (Join-Path $visualRoot 'NuGet.Config') -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Godot C# restore failed.' }
& $Dotnet build $visualProject --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Godot C# build failed.' }
& $Godot --headless --path $visualRoot --import --log-file (Join-Path $artifacts 'import.log')
if ($LASTEXITCODE -ne 0) { throw 'Godot import failed.' }

if ($Verify) {
    # Rendering QA needs a graphical desktop; headless Godot uses a dummy renderer.
    & $Godot --path $visualRoot --log-file (Join-Path $artifacts 'engine-qa.log') -- --qa $artifacts
    if ($LASTEXITCODE -ne 0) { throw 'Godot visual/input QA failed.' }
    & $Godot --path $visualRoot --log-file (Join-Path $artifacts 'field-engine-qa.log') -- --field-qa $artifacts
    if ($LASTEXITCODE -ne 0) { throw 'Godot field/battle loop QA failed.' }
    & $Godot --path $visualRoot --log-file (Join-Path $artifacts 'menu-engine-qa.log') -- --menu-qa $artifacts
    if ($LASTEXITCODE -ne 0) { throw 'Godot field menu QA failed.' }
    foreach ($report in @('qa.txt', 'field-qa.txt', 'menu-qa.txt')) {
        if ((Get-Content (Join-Path $artifacts $report) -Tail 1) -ne 'PASS ALL') {
            throw "$report did not finish with PASS ALL."
        }
    }
} else {
    & $Godot --path $visualRoot --log-file (Join-Path $artifacts 'engine.log')
    if ($LASTEXITCODE -ne 0) { throw 'Godot visual harness failed.' }
}
