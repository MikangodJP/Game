param(
    [string]$Dotnet,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$Dotnet = Resolve-Dotnet8 -RequestedPath $Dotnet
Initialize-DotnetEnvironment -Dotnet $Dotnet -ProjectRoot $projectRoot
$testProject = Join-Path $PSScriptRoot 'Tests' 'Phase1A.Tests.csproj'
& $Dotnet restore $testProject --configfile (Join-Path $PSScriptRoot 'NuGet.Config') -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $Dotnet build $testProject --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $Dotnet (Join-Path $PSScriptRoot 'Tests' 'bin' $Configuration 'net8.0' 'Phase1A.Tests.dll')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
