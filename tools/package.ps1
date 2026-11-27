# 배포용 압축: 다운로드\AI팀사무실-v버전.zip  (exe + README.txt + 규칙\팀규칙.md)
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path $PSScriptRoot -Parent
$app = Split-Path $root -Parent
$ver = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$zip = Join-Path (Join-Path $env:USERPROFILE 'Downloads') "AI팀사무실-v$ver.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# 한글 파일 이름이 깨지지 않게 UTF-8 이름으로 압축. 팀 규칙은 이 PC에서 고친 것 말고 원본을 넣는다
$files = @(
  @((Join-Path $app 'AI팀사무실.exe'), 'AI팀사무실/AI팀사무실.exe'),
  @((Join-Path $app 'README.txt'), 'AI팀사무실/README.txt'),
  @((Join-Path $root 'rules\팀규칙.md'), 'AI팀사무실/규칙/팀규칙.md')
)
$archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create', [System.Text.Encoding]::UTF8)
foreach ($f in $files) { [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f[0], $f[1], 'Optimal') | Out-Null }
$archive.Dispose()
$check = [System.IO.Compression.ZipFile]::Open($zip, 'Read', [System.Text.Encoding]::UTF8)
$check.Entries | ForEach-Object { '{0}  ({1:N0} bytes)' -f $_.FullName, $_.Length }
$check.Dispose()
Write-Host ("압축 완료: {0}  ({1:N0} bytes)" -f $zip, (Get-Item $zip).Length)
