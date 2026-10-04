$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path $PSScriptRoot -Parent
$ProjectFile = Join-Path $ProjectRoot 'SyncPlayer.csproj'
$env:DOTNET_CLI_HOME = Join-Path $ProjectRoot '.build-profile'
$env:NUGET_PACKAGES = Join-Path $ProjectRoot '.build-profile\packages'
function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
