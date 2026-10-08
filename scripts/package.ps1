param([string]$Version = '2.0.1')
. (Join-Path $PSScriptRoot 'common.ps1')
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][A-Za-z0-9.]+)?$') { throw 'Use a semantic version, for example 1.0.0' }
$output = Join-Path $ProjectRoot "artifacts\SyncPlayer-$Version-win-x64"
$archive = "$output.zip"
Invoke-DotNet publish $ProjectFile -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:EnableCompressionInSingleFile=true' "-p:Version=$Version" '-p:RestoreSources=https://api.nuget.org/v3/index.json' -o $output
$allowed = @('SyncPlayer.exe', 'README.md', 'README.zh-Hans.md', 'README.zh-Hant.md', 'LICENSE', 'NOTICE.md')
$files = @(Get-ChildItem -LiteralPath $output -File)
if ($files.Count -ne $allowed.Count -or ($files.Name | Where-Object { $_ -notin $allowed })) { throw 'Unexpected release files; refusing to package' }
Compress-Archive -LiteralPath $files.FullName -DestinationPath $archive -Force
Write-Output $archive

$videoNames = @('COinciDAnce.mp4', 'COinciDAnce - Copy.mp4')
$videoOutput = "$output-with-videos"
[void][IO.Directory]::CreateDirectory($videoOutput)
foreach ($file in $files) { Copy-Item -LiteralPath $file.FullName -Destination $videoOutput -Force }
$videoFolder = Join-Path $videoOutput 'test-video'
[void][IO.Directory]::CreateDirectory($videoFolder)
foreach ($name in $videoNames) {
    $source = Join-Path $ProjectRoot ('test-video\' + $name)
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing reference video: $name" }
    Copy-Item -LiteralPath $source -Destination $videoFolder -Force
}
$videoFiles = @(Get-ChildItem -LiteralPath $videoFolder -File)
if ($videoFiles.Count -ne 2 -or ($videoFiles.Name | Where-Object { $_ -notin $videoNames })) { throw 'Unexpected reference videos' }
$videoArchive = "$videoOutput.zip"
Compress-Archive -LiteralPath @($files.FullName + $videoFolder) -DestinationPath $videoArchive -Force
Write-Output $videoArchive
