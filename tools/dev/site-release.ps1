# Points the website at a new release: version everywhere on the home page, the exe's SHA-256 and
# size, the release-notes link, and the home page's date in the sitemap. The "New in" list is
# writing, not bookkeeping, so it is left to be edited by hand - the script says where.
#
#   tools\dev\site-release.ps1 -Version 4.5.4
#   tools\dev\site-release.ps1 -Version 4.5.4 -Deploy     and run deploy.bat afterwards
#
# Only when the site update was asked for; deploying publishes it.
param([Parameter(Mandatory)] [string]$Version, [switch]$Deploy, [string]$Site = "F:\3DFastCraft_site")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exe = Get-Item (Join-Path $root "build\3DFastCraft.exe")
$sha = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
$mb = [math]::Round($exe.Length / 1MB)

$page = Join-Path $Site "web\index.html"
$html = [IO.File]::ReadAllText($page)
$old = [regex]::Match($html, '"softwareVersion":\s*"([^"]+)"').Groups[1].Value
if (-not $old) { Write-Output "Cannot find softwareVersion in $page"; exit 1 }

# The old version only where it stands alone, so 4.5.1 is not caught by 4.5.10 or the reverse.
# "New in" headings are left: the list under them is edited by hand.
$html = [regex]::Replace($html, "(?<![\d.])" + [regex]::Escape($old) + "(?![\d.])(?!</h3>)", $Version)
$html = [regex]::Replace($html, "(<span>SHA-256</span><code>)[0-9a-f]{64}(</code>)", "`${1}$sha`${2}")
$html = [regex]::Replace($html, "\d+ MB(?= \S+ v" + [regex]::Escape($Version) + ")", "$mb MB")
[IO.File]::WriteAllText($page, $html, (New-Object Text.UTF8Encoding $false))

$map = Join-Path $Site "web\sitemap.xml"
$xml = [IO.File]::ReadAllText($map)
$xml = [regex]::Replace($xml, "(<loc>https://3dfastcraft.com/</loc><lastmod>)[^<]*", "`${1}" + (Get-Date -Format "yyyy-MM-dd"), 1)
[IO.File]::WriteAllText($map, $xml, (New-Object Text.UTF8Encoding $false))

Write-Output "Site moved from $old to $Version (sha $($sha.Substring(0,8))..., $mb MB)."
Write-Output "Edit by hand: the 'New in' list in $page (search for '<h3>New in')."

if ($Deploy) { & (Join-Path $Site "deploy.bat") | Select-Object -Last 6 }
