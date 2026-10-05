param([string]$Version = '1.0.1')
. (Join-Path $PSScriptRoot 'common.ps1')
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][A-Za-z0-9.]+)?$') { throw 'Use a semantic version, for example 1.0.0' }
$output = Join-Path $ProjectRoot "artifacts\SyncPlayer-$Version-win-x64"
$archive = "$output.zip"
Invoke-DotNet publish $ProjectFile -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:EnableCompressionInSingleFile=true' "-p:Version=$Version" '-p:RestoreSources=https://api.nuget.org/v3/index.json' -o $output
$allowed = @('SyncPlayer.exe', 'README.md', 'README.zh-Hans.md', 'README.zh-Hant.md')
$files = @(Get-ChildItem -LiteralPath $output -File)
if ($files.Count -ne $allowed.Count -or ($files.Name | Where-Object { $_ -notin $allowed })) { throw 'Unexpected release files; refusing to package' }
Compress-Archive -LiteralPath $files.FullName -DestinationPath $archive -Force
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($archive))" } | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
Write-Output $archive
