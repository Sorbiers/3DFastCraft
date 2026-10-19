# Build, and optionally run some tests, printing only what matters: errors, failures and the totals.
#
# The full output of dotnet build/test is hundreds of lines of restore chatter and analyser
# warnings, and every line of it is read. This keeps a quick check to a few lines.
#
#   tools\dev\check.ps1                     build only
#   tools\dev\check.ps1 FaceRebuild         build, then the tests whose names contain FaceRebuild
#   tools\dev\check.ps1 "Mirror|Gizmo"      several, separated by |
#   tools\dev\check.ps1 -All                the whole suite (about five minutes)
#
# Never stops a running 3DFastCraft: it only says so if one is locking the build output.
param([string]$Filter = "", [switch]$All)

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tests = Join-Path $root "tests\FastCraft3D.Tests"

$running = Get-Process 3DFastCraft -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*\bin\*" }
if ($running) { Write-Output "3DFastCraft is running from $($running[0].Path) - close it, or the build cannot replace it."; exit 1 }

if (-not $Filter -and -not $All) {
    $out = dotnet build $tests --nologo 2>&1
    $errors = $out | Select-String -Pattern " error " | ForEach-Object { $_.Line.Trim() } | Sort-Object -Unique
    if ($errors) { $errors; exit 1 }
    Write-Output "Build succeeded."
    exit 0
}

$arguments = @("test", $tests, "--nologo")
if (-not $All) {
    $parts = $Filter.Split("|") | ForEach-Object { "FullyQualifiedName~$_" }
    $arguments += @("--filter", ($parts -join "|"))
}

$out = & dotnet @arguments 2>&1
$out | Select-String -Pattern " error |\[FAIL\]|Assert\.|Expected:|Actual:|^\s+at FastCraft3D\.Tests|Passed!|Failed!|No test matches" |
    Where-Object { $_.Line -notmatch ": warning " } | ForEach-Object { $_.Line.TrimEnd() } | Select-Object -First 60
if ($LASTEXITCODE -ne 0) { exit 1 }
