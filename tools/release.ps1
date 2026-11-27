# 새 버전 내보내기 (버전 올리기 → 업데이트 내역 자동 작성 → 빌드 → git 커밋·태그 → 압축)
#
#   powershell -ExecutionPolicy Bypass -File .\tools\release.ps1            # 0.0.5 → 0.0.6
#   powershell -ExecutionPolicy Bypass -File .\tools\release.ps1 -Bump minor  # 0.0.5 → 0.1.0
#   powershell -ExecutionPolicy Bypass -File .\tools\release.ps1 -Keep      # 지금 VERSION 그대로 내보내기
#   ... -Publish  # GitHub(soin1120/ai-team-office)에 올리기 → 사용자 화면의 [업데이트]에 빨간불
#
# 업데이트 내역(CHANGELOG.md)은 지난 버전 태그 이후의 커밋 제목으로 자동으로 채운다.
# 이미 그 버전 제목(## vX.Y.Z)을 손으로 써 두었으면 그대로 둔다.
param([ValidateSet('patch', 'minor', 'major')][string]$Bump = 'patch', [switch]$Keep, [switch]$NoPackage, [switch]$Publish)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$utf8 = New-Object Text.UTF8Encoding $false
# git이 내보내는 한글(UTF-8)이 깨지지 않게
[Console]::OutputEncoding = [Text.Encoding]::UTF8; $OutputEncoding = [Text.Encoding]::UTF8

function Invoke-Git { $ErrorActionPreference = 'Continue'; $out = & git.exe -c core.safecrlf=false @args 2>&1; if ($LASTEXITCODE -ne 0) { throw "git $args 실패: $out" }; $out }

# 1) 지금 버전과 지난 태그
$current = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$lastTag = $null
try { $lastTag = (& git describe --tags --abbrev=0 2>$null); if ($LASTEXITCODE -ne 0) { $lastTag = $null } } catch { }
if ($Keep -or -not $lastTag) { $new = $current }
else {
  $p = $current.Split('.') | ForEach-Object { [int]$_ }
  switch ($Bump) { 'major' { $p = @(($p[0] + 1), 0, 0) } 'minor' { $p = @($p[0], ($p[1] + 1), 0) } default { $p = @($p[0], $p[1], ($p[2] + 1)) } }  # 괄호 필수: 쉼표가 +보다 먼저 묶임
  $new = $p -join '.'
}
if ((& git tag --list "v$new")) { throw "v$new 태그가 이미 있어요. -Bump로 올리거나 VERSION을 고쳐 주세요." }

# 2) 아직 커밋 안 한 변경이 있으면 먼저 커밋 (바뀐 파일 목록을 제목에)
$dirty = @(& git status --porcelain)
if ($dirty.Count -gt 0) {
  $names = $dirty | ForEach-Object { ($_.Substring(3) -split '/')[-1] } | Select-Object -Unique -First 6
  Invoke-Git add -A | Out-Null
  Invoke-Git commit -q -m ("작업: " + ($names -join ', ') + $(if ($dirty.Count -gt 6) { " 외 $($dirty.Count - 6)개" } else { '' })) | Out-Null
}

# 3) 업데이트 내역 자동 작성
$log = Join-Path $root 'CHANGELOG.md'
$text = if (Test-Path $log) { [IO.File]::ReadAllText($log) } else { "# 업데이트 내역`n" }
if ($text -notmatch "(?m)^## v$([regex]::Escape($new))\b") {
  $range = if ($lastTag) { "$lastTag..HEAD" } else { 'HEAD' }
  $subjects = @(& git log $range --pretty=format:%s --no-merges) | Where-Object { $_ -and $_ -notmatch '^release:' }
  if ($subjects.Count -eq 0) { $subjects = @('자잘한 수정') }
  $section = "## v$new — $(Get-Date -Format 'yyyy-MM-dd')`n" + (($subjects | ForEach-Object { "- $_" }) -join "`n") + "`n`n"
  $i = $text.IndexOf("`n## ")
  $text = if ($i -ge 0) { $text.Substring(0, $i + 1) + $section + $text.Substring($i + 1) } else { $text.TrimEnd() + "`n`n" + $section }
  [IO.File]::WriteAllText($log, $text, $utf8)
}
[IO.File]::WriteAllText((Join-Path $root 'VERSION'), "$new`n", $utf8)

# 4) 커밋·태그 (빌드보다 먼저 해야 exe에 커밋 번호가 깨끗하게 들어감)
Invoke-Git add -A | Out-Null
Invoke-Git commit -q --allow-empty -m "release: v$new" | Out-Null
Invoke-Git tag -a "v$new" -m "v$new" | Out-Null

# 5) 빌드 (실행 중이면 끄고 → 빌드 → 다시 켬) · 압축
Stop-Process -Name 'AI팀사무실', 'AITeamOffice' -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800
& (Join-Path $root 'build.ps1')
if (-not $NoPackage) { & (Join-Path $PSScriptRoot 'package.ps1') }
# 6) GitHub에 올리기 (-Publish): 사용자들의 [업데이트] 버튼이 이걸 보고 빨간불이 된다
if ($Publish) {
  $repo = 'soin1120/ai-team-office'
  $zip = Join-Path (Join-Path $env:USERPROFILE 'Downloads') "AI팀사무실-v$new.zip"
  $all = [IO.File]::ReadAllText($log)
  $m = [regex]::Match($all, "(?ms)^## v$([regex]::Escape($new))\b[^\n]*\n(.*?)(?=^## |\z)")
  $notesFile = Join-Path $env:TEMP "aiteam-notes-$new.md"
  [IO.File]::WriteAllText($notesFile, $(if ($m.Success) { $m.Groups[1].Value.Trim() } else { "v$new" }), $utf8)
  & git.exe push origin main; & git.exe push origin "v$new"
  & gh release create "v$new" $zip --repo $repo --title "v$new" --notes-file $notesFile
  if ($LASTEXITCODE -eq 0) { Write-Host "GitHub 릴리스 v$new 올림: https://github.com/$repo/releases/tag/v$new" } else { Write-Host "GitHub에 올리지 못했어요 (gh 로그인 계정·저장소를 확인해 주세요)" }
}
$app = Split-Path $root -Parent
Start-Process (Join-Path $app 'AI팀사무실.exe') -WorkingDirectory $app
Write-Host ""
Write-Host "v$new 내보내기 완료 (태그 v$new)"
