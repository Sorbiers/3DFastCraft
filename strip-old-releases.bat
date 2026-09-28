<# :
@echo off
rem Removes the downloadable files from every GitHub release except the Latest one, to stop the
rem repository carrying gigabytes of old installers. The releases themselves - their notes and
rem tags - stay. Lists what it would remove and asks before removing anything.
setlocal
set "RC=1"
cd /d "%~dp0"
where gh >nul 2>&1 || (echo gh, the GitHub CLI, is not on PATH.& goto :end)
set "STRIP_BAT=%~f0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "iex ([IO.File]::ReadAllText($env:STRIP_BAT))"
set "RC=%ERRORLEVEL%"
:end
echo %cmdcmdline% | find /i "/c" >nul && pause
exit /b %RC%
#>

[Console]::OutputEncoding = [Text.Encoding]::UTF8

# The release GitHub marks Latest: gh release view with no tag shows that one.
$latest = (gh release view --json tagName --jq .tagName).Trim()
if ($LASTEXITCODE -ne 0 -or -not $latest) { Write-Host 'Could not tell which release is Latest.' -ForegroundColor Red; exit 1 }
Write-Host "Keeping the files on the Latest release: $latest" -ForegroundColor Green

$tags = @(gh release list --limit 200 --json tagName --jq '.[].tagName')
$doomed = @()
foreach ($tag in $tags) {
    if ($tag -eq $latest) { continue }
    # Parsed here rather than by --jq: PowerShell 5.1 strips the double quotes out of an argument
    # to a native program, so a jq string literal such as "|" arrives as a bare | and jq fails.
    $assets = (gh release view $tag --json assets | Out-String | ConvertFrom-Json).assets
    foreach ($a in $assets) {
        $doomed += [pscustomobject]@{ Tag = $tag; Name = $a.name; MB = [math]::Round([double]$a.size / 1MB, 1) }
    }
}

if ($doomed.Count -eq 0) { Write-Host 'No other release has any files. Nothing to do.'; exit 0 }

Write-Host ''
$doomed | Format-Table -AutoSize | Out-String | Write-Host
Write-Host ("{0} files, {1:0.#} GB in all." -f $doomed.Count, (($doomed | Measure-Object MB -Sum).Sum / 1024))
Write-Host 'Removed files cannot be got back. The releases, their notes and their tags stay.' -ForegroundColor Yellow

if ((Read-Host 'Remove them? Type yes to go on').Trim().ToLower() -ne 'yes') { Write-Host 'Nothing was removed.'; exit 1 }

$failed = 0
foreach ($d in $doomed) {
    gh release delete-asset $d.Tag $d.Name -y
    if ($LASTEXITCODE -ne 0) { $failed++; Write-Host "Could not remove $($d.Name) from $($d.Tag)" -ForegroundColor Red }
    else { Write-Host "Removed $($d.Name) from $($d.Tag)" }
}

if ($failed -gt 0) { Write-Host "$failed could not be removed." -ForegroundColor Red; exit 1 }
Write-Host 'Done.' -ForegroundColor Green
exit 0
