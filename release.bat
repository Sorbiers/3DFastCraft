<# :
@echo off
rem Releases 3DFastCraft, asking before every step:
rem   tests, version bump + commit, build, tag + push, GitHub release, website update, deploy.
rem Release notes can be drafted by a local LLM (LM Studio or Ollama, as commit.bat), and the
rem model is unloaded straight after. Optional: COMMIT_MODEL, COMMIT_LLM_URL, RELEASE_SITE.
setlocal
set "RC=1"
cd /d "%~dp0"
where git >nul 2>&1 || (echo git is not on PATH.& goto :end)
where gh >nul 2>&1 || (echo gh, the GitHub CLI, is not on PATH.& goto :end)
set "RELEASE_BAT=%~f0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "iex ([IO.File]::ReadAllText($env:RELEASE_BAT))"
set "RC=%ERRORLEVEL%"
:end
echo %cmdcmdline% | find /i "/c" >nul && pause
exit /b %RC%
#>

[Console]::OutputEncoding = [Text.Encoding]::UTF8

$repo = (Get-Location).Path
$site = Join-Path (Split-Path $repo) '3DFastCraft_site'
if ($env:RELEASE_SITE) { $site = $env:RELEASE_SITE }
$csproj = Join-Path $repo 'src\FastCraft3D\FastCraft3D.csproj'
$exe = Join-Path $repo 'build\3DFastCraft.exe'
$index = Join-Path $site 'web\index.html'
$sitemap = Join-Path $site 'web\sitemap.xml'

function Fail($text) { Write-Host $text -ForegroundColor Red; exit 1 }

function Confirm-Step($title, $detail) {
    Write-Host ''
    Write-Host "== $title" -ForegroundColor Yellow
    if ($detail) { Write-Host "   $detail" -ForegroundColor DarkGray }
    do { $a = (Read-Host '[y]es  [s]kip  [q]uit').Trim().ToLower() }
    until ($a -eq 'y' -or $a -eq 's' -or $a -eq 'q')
    if ($a -eq 'q') { Write-Host 'Stopped.'; exit 1 }
    return ($a -eq 'y')
}

function Ask-YesNo($question) { return ((Read-Host "$question [y/n]").Trim().ToLower() -eq 'y') }

function Wait-AppClosed {
    while (Get-Process 3DFastCraft -ErrorAction SilentlyContinue) {
        Read-Host '3DFastCraft is running - close it, then press Enter' | Out-Null
    }
}

function Read-Text($path) { return [IO.File]::ReadAllText($path) }

# Keeps a byte order mark only where the file already had one.
function Write-Text($path, $text) {
    $bom = $false
    if (Test-Path $path) {
        $bytes = [IO.File]::ReadAllBytes($path)
        $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    }
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding $bom))
}

# The text between a markdown heading and the next heading of the same level.
function Get-Section($text, $heading) {
    $m = [regex]::Match($text, '(?ms)^' + [regex]::Escape($heading) + '.*?$(.*?)(?=^## |\z)')
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return ''
}

# --- local LLM, the same servers as commit.bat ----------------------------------------------
function Find-Llm {
    $bases = @('http://localhost:1234/v1', 'http://localhost:11434/v1')
    if ($env:COMMIT_LLM_URL) { $bases = @($env:COMMIT_LLM_URL.TrimEnd('/')) }
    foreach ($b in $bases) {
        try {
            $models = Invoke-RestMethod "$b/models" -TimeoutSec 3 -UseBasicParsing
            $id = $env:COMMIT_MODEL
            if (-not $id) { $id = @($models.data | Where-Object { $_.id -notmatch 'embed' })[0].id }
            if ($id) { return @{ Base = $b; Model = $id } }
        } catch { }
    }
    return $null
}

function Ask-Llm($llm, $system, $user) {
    $body = @{
        model = $llm.Model
        temperature = 0.3
        stream = $false
        messages = @(@{ role = 'system'; content = $system }, @{ role = 'user'; content = $user })
    } | ConvertTo-Json -Depth 5

    # Decoded by hand: Windows PowerShell reads a reply without a charset as Latin-1.
    $resp = Invoke-WebRequest "$($llm.Base)/chat/completions" -Method Post -UseBasicParsing -TimeoutSec 900 `
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    $reply = [Text.Encoding]::UTF8.GetString($resp.RawContentStream.ToArray()) | ConvertFrom-Json
    $text = [string]$reply.choices[0].message.content
    $text = $text -replace '(?s)<think>.*?</think>', ''
    $text = $text -replace '(?m)^```\w*\s*$', ''
    return $text.Trim()
}

function Eject-Llm($llm) {
    $root = $llm.Base -replace '/v1$', ''
    $ollama = $false
    try { Invoke-RestMethod "$root/api/version" -TimeoutSec 3 -UseBasicParsing | Out-Null; $ollama = $true } catch { }

    if ($ollama) {
        $body = @{ model = $llm.Model; keep_alive = 0 } | ConvertTo-Json
        try { Invoke-RestMethod "$root/api/generate" -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 30 -UseBasicParsing | Out-Null } catch { }
        return
    }

    $lms = Get-Command lms -ErrorAction SilentlyContinue
    $lmsPath = if ($lms) { $lms.Source } else { Join-Path $env:USERPROFILE '.lmstudio\bin\lms.exe' }
    if (Test-Path $lmsPath) {
        & $lmsPath unload $llm.Model 2>&1 | Out-Null
    } else {
        try { Invoke-RestMethod "$root/api/v1/models/unload" -Method Post -ContentType 'application/json' -Body (@{ instance_id = $llm.Model } | ConvertTo-Json) -TimeoutSec 30 -UseBasicParsing | Out-Null }
        catch { Write-Host "Could not unload $($llm.Model) - eject it in LM Studio." -ForegroundColor Yellow }
    }
}

# --- where things stand -----------------------------------------------------------------------
$current = [regex]::Match((Read-Text $csproj), '<Version>([^<]+)</Version>').Groups[1].Value
$tags = @(git tag --list 'v*' --sort=-v:refname)
$lastTag = if ($tags.Count -gt 0) { $tags[0] } else { '' }

$suggest = $current
if ($lastTag -eq "v$current") {
    $p = $current.Split('.')
    $suggest = "$($p[0]).$($p[1]).$([int]$p[2] + 1)"
}

Write-Host ''
Write-Host "Version in the project: $current    Last tag: $lastTag"
if ($lastTag) {
    Write-Host "Commits since ${lastTag}:"
    git log --oneline "$lastTag..HEAD"
}
$dirty = @(git status --porcelain)
if ($dirty.Count -gt 0) {
    Write-Host 'Uncommitted changes - not in the release unless committed first:' -ForegroundColor Yellow
    $dirty | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
}

$version = (Read-Host "Version to release [$suggest]").Trim()
if (-not $version) { $version = $suggest }
if ($version -notmatch '^\d+\.\d+\.\d+$') { Fail "Not a version: $version" }
$tag = "v$version"
$prevTag = @($tags | Where-Object { $_ -ne $tag })[0]
$range = if ($prevTag) { "$prevTag..HEAD" } else { 'HEAD~20..HEAD' }
$notesPath = Join-Path $repo "build\release-notes-$tag.md"

# --- 1. tests ---------------------------------------------------------------------------------
if (Confirm-Step 'Run the tests' 'dotnet test') {
    Wait-AppClosed
    dotnet test --nologo
    if ($LASTEXITCODE -ne 0) { Fail 'Tests failed.' }
}

# --- 2. version -------------------------------------------------------------------------------
if ($version -ne $current) {
    if (Confirm-Step "Set the version to $version and commit it" 'src\FastCraft3D\FastCraft3D.csproj') {
        Write-Text $csproj ((Read-Text $csproj) -replace '<Version>[^<]+</Version>', "<Version>$version</Version>")
        git commit -m "3DFastCraft $version" -- $csproj
        if ($LASTEXITCODE -ne 0) { Fail 'Commit failed.' }
    }
}

# --- 3. build ---------------------------------------------------------------------------------
if (Confirm-Step 'Build the exe' 'build.bat') {
    Wait-AppClosed
    cmd /c build.bat
    if ($LASTEXITCODE -ne 0) { Fail 'Build failed.' }
}

# --- 4. tag and push --------------------------------------------------------------------------
$branch = (git rev-parse --abbrev-ref HEAD)
if (Confirm-Step "Tag $tag and push $branch with the tag" "git push origin $branch; git push origin $tag") {
    git rev-parse -q --verify "refs/tags/$tag" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        git tag -a $tag -m "3DFastCraft $version"
        if ($LASTEXITCODE -ne 0) { Fail 'Tagging failed.' }
    } else {
        Write-Host "$tag already exists - pushing it as it is." -ForegroundColor DarkGray
    }
    git push origin $branch
    if ($LASTEXITCODE -ne 0) { Fail 'Push failed.' }
    git push origin $tag
    if ($LASTEXITCODE -ne 0) { Fail 'Pushing the tag failed.' }
}

# --- 5. GitHub release ------------------------------------------------------------------------
if (Confirm-Step "Write the notes and publish the GitHub release $tag" 'the notes open in Notepad, and publishing is asked again') {
    if (-not (Test-Path $exe)) { Fail "No exe at $exe - build it first." }

    if (Test-Path $notesPath) {
        Write-Host "Using the notes already drafted: $notesPath" -ForegroundColor DarkGray
    } else {
        $prevBody = ''
        if ($prevTag) { $prevBody = (gh release view $prevTag --json body --jq .body) -join "`n" }
        $prevVersion = if ($prevTag) { $prevTag.TrimStart('v') } else { '' }

        $draft = (git log --no-merges --format='- %s' $range) -join "`n"
        $llm = Find-Llm
        if ($llm -and (Ask-YesNo "Draft the 'New in' section with $($llm.Model)?")) {
            Write-Host 'Writing the notes...' -ForegroundColor DarkGray
            $system = 'You write release notes for a Windows 3D modelling app. From the commit messages, write the body of a "New in" section in GitHub markdown: one ### heading for each thing a user would go looking for, each followed by a short plain-English paragraph on what it does and why it helps. Group related commits. Leave out refactoring, tests, docs and version bumps. No top-level heading, no preamble. Match the tone of the example.'
            $example = Get-Section $prevBody "## New in $prevVersion"
            $commits = (git log --no-merges --format='- %s%n%b' $range) -join "`n"
            try { $draft = Ask-Llm $llm $system "Example from the last release:`n$example`n`nCommits:`n$commits" }
            catch { Write-Host "The model call failed: $($_.Exception.Message) - using the commit list." -ForegroundColor Yellow }
            finally { Eject-Llm $llm }
        }

        # Everything but the new section carries over from the last release: epigraph, download,
        # known limits and licence.
        $iDownload = $prevBody.IndexOf('## Download')
        $iNew = $prevBody.IndexOf('## New in')
        $iLimits = $prevBody.IndexOf('## Known limits')
        if ($iDownload -gt 0 -and $iNew -gt $iDownload -and $iLimits -gt $iNew) {
            $epigraph = ($prevBody -split "`n")[0].Trim()
            $download = $prevBody.Substring($iDownload, $iNew - $iDownload).Replace("v$prevVersion", $tag).TrimEnd()
            $tail = $prevBody.Substring($iLimits).TrimEnd()
            $notes = "$epigraph`n`nTODO: one line on what this release brings.`n`n$download`n`n## New in $version`n`n$draft`n`n$tail`n"
        } else {
            $notes = "## New in $version`n`n$draft`n"
        }
        [IO.File]::WriteAllText($notesPath, $notes, (New-Object Text.UTF8Encoding $false))
    }

    Start-Process notepad.exe -ArgumentList "`"$notesPath`"" -Wait
    if ((Read-Text $notesPath) -match 'TODO') { Write-Host 'The notes still say TODO.' -ForegroundColor Yellow }

    if (Confirm-Step "Publish $tag on GitHub" "build\3DFastCraft.exe with $notesPath") {
        gh release create $tag $exe --title "3DFastCraft $version" --notes-file $notesPath
        if ($LASTEXITCODE -ne 0) { Fail 'Publishing the release failed.' }
        gh release view $tag --json url --jq .url
    }
}

# --- 6. website -------------------------------------------------------------------------------
if (Confirm-Step "Update the website to $version" 'version, download links, size, SHA-256, New in list, sitemap date') {
    if (-not (Test-Path $index)) { Fail "No website at $index - set RELEASE_SITE." }
    if (-not (Test-Path $exe)) { Fail "No exe at $exe - build it first." }

    $html = Read-Text $index
    $nl = if ($html.Contains("`r`n")) { "`r`n" } else { "`n" }
    $old = [regex]::Match($html, '"softwareVersion": "([^"]+)"').Groups[1].Value
    if (-not $old) { Fail 'No softwareVersion in index.html to go by.' }

    $sha = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
    $mb = [math]::Round((Get-Item $exe).Length / 1MB)

    $new = [regex]::Replace($html, '(?<![\d.])' + [regex]::Escape($old) + '(?![\d.])', $version)
    $new = [regex]::Replace($new, '(<span>SHA-256</span><code>)[0-9a-f]{64}(</code>)', '${1}' + $sha + '${2}')
    $new = [regex]::Replace($new, '\d+ MB(?= \S+ v' + [regex]::Escape($version) + ')', "$mb MB")

    if (Test-Path $notesPath) {
        $section = Get-Section (Read-Text $notesPath) "## New in $version"
        $items = @([regex]::Matches($section, '(?m)^###\s+(.+?)\s*$') | ForEach-Object { $_.Groups[1].Value })
        if ($items.Count -eq 0) { $items = @([regex]::Matches($section, '(?m)^[-*]\s+(.+?)\s*$') | ForEach-Object { $_.Groups[1].Value }) }

        $list = [regex]::Match($new, '(?s)<h3>New in [^<]*</h3>\s*<ul class="ticks">(.*?)\s*</ul>')
        if ($items.Count -gt 0 -and $list.Success -and (Ask-YesNo "Replace the site's New in list with the $($items.Count) items from the release notes?")) {
            $lis = ($items | ForEach-Object { '        <li>' + [Net.WebUtility]::HtmlEncode(($_ -replace '[*`]', '')) + '</li>' }) -join $nl
            $g = $list.Groups[1]
            $new = $new.Substring(0, $g.Index) + $nl + $lis + $new.Substring($g.Index + $g.Length)
        }
    }

    Write-Host ''
    Compare-Object ($html -split "`r?`n") ($new -split "`r?`n") | ForEach-Object {
        if ($_.SideIndicator -eq '=>') { Write-Host "+ $($_.InputObject.Trim())" -ForegroundColor Green }
        else { Write-Host "- $($_.InputObject.Trim())" -ForegroundColor Red }
    }

    if (Confirm-Step 'Save these changes' "$index and the sitemap date") {
        Write-Text $index $new
        if (Test-Path $sitemap) {
            $today = Get-Date -Format 'yyyy-MM-dd'
            Write-Text $sitemap ((Read-Text $sitemap) -replace '<lastmod>[^<]+</lastmod>', "<lastmod>$today</lastmod>")
        }
        if (Ask-YesNo 'Open index.html in Notepad to check it?') {
            Start-Process notepad.exe -ArgumentList "`"$index`"" -Wait
        }
    }
}

# --- 7. deploy --------------------------------------------------------------------------------
if (Confirm-Step 'Deploy the website' (Join-Path $site 'deploy.bat')) {
    Push-Location $site
    cmd /c deploy.bat
    $code = $LASTEXITCODE
    Pop-Location
    if ($code -ne 0) { Fail 'Deploy failed.' }
}

Write-Host ''
Write-Host "Done: https://github.com/Sorbiers/3DFastCraft/releases/tag/$tag" -ForegroundColor Green
exit 0
