# 실행 중인 프로그램을 끄고 → 빌드 → 다시 켠다
Stop-Process -Name 'AI팀사무실','AITeamOffice' -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800
& "$PSScriptRoot\..\build.ps1"
$app = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Start-Process (Join-Path $app 'AI팀사무실.exe') -WorkingDirectory $app
