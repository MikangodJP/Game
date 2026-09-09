param(
    [string]$Dotnet,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Dotnet) {
    $localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
    $Dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
}
$Dotnet = (Get-Command $Dotnet -ErrorAction Stop).Source
$env:DOTNET_HOST_PATH = $Dotnet
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.tools\nuget-cache'
$testProject = Join-Path $PSScriptRoot 'Tests\Phase1A.Tests.csproj'
& $Dotnet restore $testProject --configfile (Join-Path $PSScriptRoot 'NuGet.Config') -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $Dotnet build $testProject --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $Dotnet (Join-Path $PSScriptRoot "Tests\bin\$Configuration\net8.0\Phase1A.Tests.dll")
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
