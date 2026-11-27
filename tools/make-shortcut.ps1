$app = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exe = Join-Path $app 'AI팀사무실.exe'
$desktop = [Environment]::GetFolderPath('Desktop')
$lnk = Join-Path $desktop 'AI 팀 사무실.lnk'
$sh = New-Object -ComObject WScript.Shell
$s = $sh.CreateShortcut($lnk)
$s.TargetPath = $exe
$s.WorkingDirectory = $app
$s.IconLocation = "$exe,0"
$s.Description = '클로드·코덱스·제미나이가 함께 일하는 AI 팀 사무실'
$s.Save()
Write-Host "바탕화면 바로가기: $lnk -> $exe"
