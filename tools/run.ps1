# 프로그램만 다시 켠다 (빌드 없이)
$app = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Start-Process (Join-Path $app 'AI팀사무실.exe') -WorkingDirectory $app
