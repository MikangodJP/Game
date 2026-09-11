function Resolve-Dotnet8 {
    param([string]$RequestedPath)

    $executableName = if ($IsWindows) { 'dotnet.exe' } else { 'dotnet' }
    $candidates = [System.Collections.Generic.List[string]]::new()

    if ($RequestedPath) {
        $candidates.Add($RequestedPath)
    } else {
        $projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
        $systemRoot = [IO.Path]::GetPathRoot($projectRoot)
        $candidates.Add((Join-Path $projectRoot '.tools' 'dotnet' $executableName))

        $pathCommand = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($pathCommand) {
            $candidates.Add($pathCommand.Source)
        }

        if ($IsMacOS) {
            $candidates.Add((Join-Path $systemRoot 'usr' 'local' 'share' 'dotnet' 'dotnet'))
            $candidates.Add((Join-Path $systemRoot 'opt' 'homebrew' 'bin' 'dotnet'))
        } elseif ($IsLinux) {
            $candidates.Add((Join-Path $systemRoot 'usr' 'share' 'dotnet' 'dotnet'))
            $candidates.Add((Join-Path $systemRoot 'usr' 'local' 'share' 'dotnet' 'dotnet'))
        } elseif ($IsWindows -and $env:ProgramFiles) {
            $candidates.Add((Join-Path $env:ProgramFiles 'dotnet' 'dotnet.exe'))
        }
    }

    $checked = [System.Collections.Generic.List[string]]::new()
    foreach ($candidate in $candidates) {
        if (-not $candidate -or $checked.Contains($candidate)) {
            continue
        }
        $checked.Add($candidate)

        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if (-not $command) {
            continue
        }

        $sdks = & $command.Source --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $sdks -match '^8\.0\.') {
            return $command.Source
        }
    }

    $detail = if ($checked.Count -gt 0) { $checked -join ', ' } else { '(none)' }
    throw "A .NET 8 SDK is required. Checked: $detail. Pass its host path with -Dotnet."
}

function Initialize-DotnetEnvironment {
    param(
        [Parameter(Mandatory)][string]$Dotnet,
        [Parameter(Mandatory)][string]$ProjectRoot
    )

    $env:DOTNET_ROOT = Split-Path $Dotnet
    $env:DOTNET_HOST_PATH = $Dotnet
    $env:DOTNET_CLI_HOME = Join-Path $ProjectRoot '.tools' 'cli-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    $env:NUGET_PACKAGES = Join-Path $ProjectRoot '.tools' 'nuget-packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $ProjectRoot '.tools' 'nuget-cache'
    $env:PATH = [string]::Join([IO.Path]::PathSeparator, @($env:DOTNET_ROOT, $env:PATH))
}
