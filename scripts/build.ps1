. (Join-Path $PSScriptRoot 'common.ps1')
Invoke-DotNet restore $ProjectFile --configfile (Join-Path $ProjectRoot 'NuGet.Config')
Invoke-DotNet build $ProjectFile -c Release --no-restore
