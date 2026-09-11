param([Parameter(Mandatory = $true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& dotnet build (Join-Path $PSScriptRoot 'src\TSKHook.UI\TSKHook.UI.csproj') -c Release --nologo "-p:GamePath=$GamePath"
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
