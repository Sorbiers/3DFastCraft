# Cut a release in one go, once the notes are written. Only ever run when a release was asked for.
#
#   tools\dev\release.ps1 -Version 4.5.4 -Notes build\release-notes-v4.5.4.md
#   tools\dev\release.ps1 -Version 4.5.4 -Notes ... -SkipTests     when the suite was run already
#
# Steps, stopping at the first that fails: set <Version> in the csproj, run the suite, build.bat,
# print the exe's size and SHA-256 (the notes and the site want them), commit "3DFastCraft x.y.z",
# tag, push the branch and the tag, fast-forward main and push it, publish the GitHub release.
# The notes file must already carry the SHA: run with -Prepare first to get it, edit the notes,
# then run again without it.
param(
    [Parameter(Mandatory)] [string]$Version,
    [string]$Notes = "",
    [switch]$Prepare,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

function Step($what) { Write-Output "-- $what" }
function Check($what) { if ($LASTEXITCODE -ne 0) { Write-Output "FAILED: $what"; exit 1 } }

if (Get-Process 3DFastCraft -ErrorAction SilentlyContinue) { Write-Output "3DFastCraft is running - close it first (build.bat cannot replace the exe)."; exit 1 }

$csproj = "src\FastCraft3D\FastCraft3D.csproj"
$text = [IO.File]::ReadAllText($csproj)
$updated = [regex]::Replace($text, "<Version>[^<]*</Version>", "<Version>$Version</Version>")
if ($updated -ne $text) { [IO.File]::WriteAllText($csproj, $updated); Step "version set to $Version" }

if ($Prepare) {
    if (-not $SkipTests) { Step "tests"; & "$PSScriptRoot\check.ps1" -All; Check "tests" }
    Step "build"; & (Join-Path $root "build.bat") | Select-Object -Last 3; Check "build"
    $exe = Get-Item build\3DFastCraft.exe
    Write-Output ("size {0} bytes ({1} MB)" -f $exe.Length, [math]::Round($exe.Length / 1MB))
    Write-Output ("sha256 " + (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower())
    Write-Output "Now write the notes, then run again with -Notes and without -Prepare."
    exit 0
}

if (-not $Notes -or -not (Test-Path $Notes)) { Write-Output "Notes file not found: '$Notes'"; exit 1 }
$sha = (Get-FileHash build\3DFastCraft.exe -Algorithm SHA256).Hash.ToLower()
if (-not (Select-String -Path $Notes -SimpleMatch $sha -Quiet)) { Write-Output "The notes do not carry the built exe's SHA-256 ($sha) - rebuild with -Prepare or fix the notes."; exit 1 }

$branch = (git rev-parse --abbrev-ref HEAD).Trim()
Step "commit and tag on $branch"
git add $csproj README.md; Check "git add"
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) { git commit -q -m "3DFastCraft $Version"; Check "commit" }
git tag -a "v$Version" -m "3DFastCraft $Version"; Check "tag"

Step "push"
git push -q origin $branch; Check "push $branch"
git checkout -q main; Check "checkout main"
git merge --ff-only -q $branch; Check "fast-forward main"
git push -q origin main; Check "push main"
git push -q origin "v$Version"; Check "push tag"
git checkout -q $branch

Step "GitHub release"
gh release create "v$Version" build\3DFastCraft.exe --title "3DFastCraft $Version" --notes-file $Notes; Check "gh release"
gh release view "v$Version" --json assets --jq '.assets[] | "\(.name) \(.size)"'
