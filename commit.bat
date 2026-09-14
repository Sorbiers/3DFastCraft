<# :
@echo off
rem Writes a commit message with a local LLM and commits. Never pushes.
rem Commits the repository of the folder it is run from, wherever the .bat itself lives.
rem Works with LM Studio (port 1234) or Ollama (port 11434), whichever is running, and
rem unloads the model when done.
rem Optional: set COMMIT_MODEL, COMMIT_LLM_URL (e.g. http://localhost:1234/v1), COMMIT_MAX_CHARS.
setlocal
set "RC=1"
where git >nul 2>&1 || (echo git is not on PATH.& goto :end)
git rev-parse --is-inside-work-tree >nul 2>&1 || (echo %CD% is not a git repository.& goto :end)
set "COMMIT_BAT=%~f0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "iex ([IO.File]::ReadAllText($env:COMMIT_BAT))"
set "RC=%ERRORLEVEL%"
:end
echo %cmdcmdline% | find /i "/c" >nul && pause
exit /b %RC%
#>

[Console]::OutputEncoding = [Text.Encoding]::UTF8
$maxChars = 12000
if ($env:COMMIT_MAX_CHARS) { $maxChars = [int]$env:COMMIT_MAX_CHARS }

$model = $null
$base = $null

# Unloads the model so it does not sit in memory after the commit.
function Eject {
    if (-not $model -or -not $base) { return }
    $root = $base -replace '/v1$', ''
    $ollama = $false
    try { Invoke-RestMethod "$root/api/version" -TimeoutSec 3 -UseBasicParsing | Out-Null; $ollama = $true } catch { }

    if ($ollama) {
        $body = @{ model = $model; keep_alive = 0 } | ConvertTo-Json
        try { Invoke-RestMethod "$root/api/generate" -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 30 -UseBasicParsing | Out-Null } catch { }
        return
    }

    $lms = Get-Command lms -ErrorAction SilentlyContinue
    $lmsPath = if ($lms) { $lms.Source } else { Join-Path $env:USERPROFILE '.lmstudio\bin\lms.exe' }
    if (Test-Path $lmsPath) {
        & $lmsPath unload $model 2>&1 | Out-Null
    } else {
        try { Invoke-RestMethod "$root/api/v1/models/unload" -Method Post -ContentType 'application/json' -Body (@{ instance_id = $model } | ConvertTo-Json) -TimeoutSec 30 -UseBasicParsing | Out-Null }
        catch { Write-Host "Could not unload $model - eject it in LM Studio." -ForegroundColor Yellow }
    }
}

function Finish([int]$code) { Eject; exit $code }
function Fail($text) { Write-Host $text -ForegroundColor Red; Finish 1 }

# Stage everything only when nothing is staged, so a hand-picked stage is respected.
$autoStaged = $false
git diff --cached --quiet
if ($LASTEXITCODE -eq 0) {
    git add -A
    $autoStaged = $true
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) { Write-Host 'Nothing to commit.'; exit 0 }
}

function Undo { if ($autoStaged) { git reset -q } }

# Find a running server: LM Studio first, then Ollama. Both speak the OpenAI API.
$bases = @('http://localhost:1234/v1', 'http://localhost:11434/v1')
if ($env:COMMIT_LLM_URL) { $bases = @($env:COMMIT_LLM_URL.TrimEnd('/')) }
$models = $null
foreach ($b in $bases) {
    try { $models = Invoke-RestMethod "$b/models" -TimeoutSec 3 -UseBasicParsing; $base = $b; break } catch { }
}
if (-not $base) { Undo; Fail 'No local LLM found. Start the LM Studio server or Ollama, or set COMMIT_LLM_URL.' }

$found = $env:COMMIT_MODEL
if (-not $found) { $found = @($models.data | Where-Object { $_.id -notmatch 'embed' })[0].id }
if (-not $found) { Undo; Fail "No model available at $base. Load or pull one, or set COMMIT_MODEL." }
$model = $found

$diff = (git diff --cached --no-color --no-ext-diff) -join "`n"
if ($diff.Length -gt $maxChars) { $diff = $diff.Substring(0, $maxChars) + "`n[diff truncated]" }
$stat = (git diff --cached --stat --no-color) -join "`n"
$recent = (git log -10 --format=%s 2>$null) -join "`n"
$project = Split-Path -Leaf (Get-Location)

function New-Message([double]$temperature) {
    $body = @{
        model = $model
        temperature = $temperature
        stream = $false
        messages = @(
            @{ role = 'system'; content = 'You write git commit messages. Reply with the commit message only: a subject line of at most 72 characters, then a blank line and a short body of 1-4 lines saying what changed and why. Match the style of the recent subjects if any are given, otherwise use the imperative mood. No markdown, no code fences, no quotes, no preamble.' },
            @{ role = 'user'; content = "Project: $project`n`nRecent commit subjects:`n$recent`n`nChanged files:`n$stat`n`nDiff:`n$diff" }
        )
    } | ConvertTo-Json -Depth 5

    # Decoded by hand: Windows PowerShell reads a reply without a charset as Latin-1.
    $resp = Invoke-WebRequest "$base/chat/completions" -Method Post -UseBasicParsing -TimeoutSec 600 `
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    $reply = [Text.Encoding]::UTF8.GetString($resp.RawContentStream.ToArray()) | ConvertFrom-Json

    $text = [string]$reply.choices[0].message.content
    $text = $text -replace '(?s)<think>.*?</think>', ''
    $text = $text -replace '(?m)^```\w*\s*$', ''
    return $text.Trim().Trim('"').Trim()
}

Write-Host ''
git diff --cached --stat --no-color
Write-Host ''
Write-Host "Model: $model ($base)" -ForegroundColor DarkGray

$temperature = 0.2
while ($true) {
    Write-Host 'Writing the message...' -ForegroundColor DarkGray
    try { $msg = New-Message $temperature } catch { Undo; Fail "The model call failed: $($_.Exception.Message)" }

    Write-Host ''
    Write-Host $msg -ForegroundColor Cyan
    Write-Host ''

    do { $choice = (Read-Host '[c]ommit  [e]dit  [r]egenerate  [q]uit').Trim().ToLower() }
    until ($choice -eq 'c' -or $choice -eq 'e' -or $choice -eq 'r' -or $choice -eq 'q')

    if ($choice -eq 'r') { $temperature = 0.7; continue }
    if ($choice -eq 'q') { Undo; Write-Host 'Nothing committed.'; Finish 0 }

    $file = [IO.Path]::GetTempFileName()
    [IO.File]::WriteAllText($file, $msg)
    if ($choice -eq 'e') { Start-Process notepad.exe -ArgumentList "`"$file`"" -Wait }
    git commit -F $file
    $code = $LASTEXITCODE
    Remove-Item $file -ErrorAction SilentlyContinue
    if ($code -ne 0) { Undo }
    Finish $code
}
