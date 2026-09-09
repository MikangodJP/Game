param(
    [string]$Dotnet,
    [switch]$Verify
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Dotnet) {
    $localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
    $Dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
}
$Dotnet = (Get-Command $Dotnet -ErrorAction Stop).Source
$godot = Join-Path $projectRoot '.tools\godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe'
if (-not (Test-Path -LiteralPath $godot)) {
    throw 'Install Godot 4.6.3 .NET in .tools/godot as described in probes/phase1a/README.md.'
}
$env:DOTNET_ROOT = Split-Path $Dotnet
$env:DOTNET_HOST_PATH = $Dotnet
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.tools\nuget-cache'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$visualRoot = Join-Path $PSScriptRoot 'Visual'
$artifacts = Join-Path $PSScriptRoot 'artifacts\visual'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

if ($Verify) {
    & (Join-Path $PSScriptRoot 'verify.ps1') -Dotnet $Dotnet -Configuration Debug
    & (Join-Path $PSScriptRoot 'verify.ps1') -Dotnet $Dotnet -Configuration Release
    $modelTests = Join-Path $PSScriptRoot 'VisualTests\Phase1A.VisualTests.csproj'
    & $Dotnet restore $modelTests --configfile (Join-Path $PSScriptRoot 'NuGet.Config') -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Visual model test restore failed.' }
    & $Dotnet build $modelTests --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Visual model test build failed.' }
    & $Dotnet (Join-Path $PSScriptRoot 'VisualTests\bin\Debug\net8.0\Phase1A.VisualTests.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Visual model tests failed.' }
}

$visualProject = Join-Path $visualRoot 'Visual.csproj'
& $Dotnet restore $visualProject --configfile (Join-Path $visualRoot 'NuGet.Config') -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Godot C# restore failed.' }
& $Dotnet build $visualProject --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Godot C# build failed.' }
& $godot --headless --path $visualRoot --import --log-file (Join-Path $artifacts 'import.log')
if ($LASTEXITCODE -ne 0) { throw 'Godot import failed.' }

if ($Verify) {
    # Rendering QA needs a graphical desktop; headless Godot uses a dummy renderer.
    & $godot --path $visualRoot --log-file (Join-Path $artifacts 'engine-qa.log') -- --qa $artifacts
    if ($LASTEXITCODE -ne 0) { throw 'Godot visual/input QA failed.' }
    & $godot --path $visualRoot --log-file (Join-Path $artifacts 'field-engine-qa.log') -- --field-qa $artifacts
    if ($LASTEXITCODE -ne 0) { throw 'Godot field/battle loop QA failed.' }
} else {
    & $godot --path $visualRoot --log-file (Join-Path $artifacts 'engine.log')
    if ($LASTEXITCODE -ne 0) { throw 'Godot visual harness failed.' }
}
