param([switch]$Video, [switch]$Screenshots)
. (Join-Path $PSScriptRoot 'common.ps1')
Set-Location $ProjectRoot
$output = Join-Path $ProjectRoot 'artifacts\tests'
Invoke-DotNet restore $ProjectFile --configfile (Join-Path $ProjectRoot 'NuGet.Config')
Invoke-DotNet build $ProjectFile -c Release --no-restore '-p:EnableDiagnostics=true' -o $output
$executable = Join-Path $output 'SyncPlayer.exe'
$cases = [ordered]@{ '--self-test'='self-test-result.txt'; '--network-test'='network-test-result.txt'; '--lan-test'='lan-test-result.txt'; '--language-test'='language-test-result.txt' }
if ($Video) {
    $cases['--video-test']='video-test-result.txt'
    $cases['--controls-test']='video-test-result.txt'
    $cases['--boundary-test']='boundary-test-result.txt'
    $cases['--offset-test']='offset-test-result.txt'
}
[void][IO.Directory]::CreateDirectory((Join-Path $ProjectRoot 'diagnostics'))
foreach ($entry in $cases.GetEnumerator()) {
    if (Test-Path -LiteralPath $entry.Value) { Remove-Item -LiteralPath $entry.Value }
    $process = Start-Process -FilePath $executable -ArgumentList $entry.Key -PassThru
    if (-not $process.WaitForExit(180000)) { Stop-Process -Id $process.Id; throw "Test timed out: $($entry.Key)" }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $entry.Value)) { throw "Test did not complete: $($entry.Key)" }
    $report = Get-Content -LiteralPath $entry.Value -Raw
    Move-Item -LiteralPath $entry.Value -Destination (Join-Path $ProjectRoot ('diagnostics\' + $entry.Key.TrimStart('-') + '.txt')) -Force
    if ($report -match '(?m)^ERROR|\bFAIL\b') { throw $report }
    Write-Output "PASS $($entry.Key)"
}
if ($Screenshots) {
    foreach ($language in @('en','zh-Hans','zh-Hant')) {
        Start-Process -FilePath $executable -ArgumentList '--ui-check',"--language=$language",'--ui-peers' -Wait
        foreach ($file in @('ui-0.png','ui-1.png','ui-settings.png')) { Copy-Item -LiteralPath $file -Destination (Join-Path $ProjectRoot "diagnostics\$language-$file") }
    }
}
