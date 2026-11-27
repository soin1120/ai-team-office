# AI 팀 사무실 빌드: Windows 기본 .NET Framework 컴파일러로 단일 exe를 만든다 (추가 설치 불필요)
param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'obj'   # 중간 산출물(3D 번들·아이콘)만 둔다
# 실제 프로그램 폴더 = 이 '개발' 폴더의 바로 위 (exe·README·규칙 폴더가 바로 들어 있음)
$app = Split-Path $root -Parent
New-Item -ItemType Directory -Force $app | Out-Null
New-Item -ItemType Directory -Force $dist | Out-Null
$bundler = Join-Path $root 'node_modules\.bin\esbuild.cmd'
if (-not (Test-Path $bundler)) { throw '프로젝트 폴더에서 npm ci를 먼저 실행하세요.' }
$officeBundle = Join-Path $dist 'office.bundle.js'
& $bundler (Join-Path $root 'src\office.js') --bundle --minify --log-level=error "--outfile=$officeBundle"
if ($LASTEXITCODE -ne 0) { throw '3D 사무실 빌드 실패' }

# 1) 아이콘 만들기 (256px PNG를 담은 .ico)
Add-Type -AssemblyName System.Drawing
$ico = Join-Path $root 'src\app.ico'
$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 56; $w = $size - 8
$path.AddArc(4, 4, $r, $r, 180, 90); $path.AddArc($w - $r + 4, 4, $r, $r, 270, 90)
$path.AddArc($w - $r + 4, $w - $r + 4, $r, $r, 0, 90); $path.AddArc(4, $w - $r + 4, $r, $r, 90, 90); $path.CloseFigure()
$g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 23, 26, 33))), $path)
$colors = @(@(217,119,87), @(63,182,139), @(91,141,239))
$pos = @(@(128, 82), @(78, 168), @(178, 168))
for ($i = 0; $i -lt 3; $i++) {
  $c = $colors[$i]; $p = $pos[$i]
  $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, $c[0], $c[1], $c[2]))), $p[0] - 38, $p[1] - 38, 76, 76)
}
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 231, 233, 238)), 7
$g.DrawLine($pen, 128, 82, 78, 168); $g.DrawLine($pen, 128, 82, 178, 168); $g.DrawLine($pen, 78, 168, 178, 168)
for ($i = 0; $i -lt 3; $i++) {
  $c = $colors[$i]; $p = $pos[$i]
  $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, $c[0], $c[1], $c[2]))), $p[0] - 30, $p[1] - 30, 60, 60)
}
$g.Dispose()
$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $ms.ToArray()
$fs = [System.IO.File]::Create($ico)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)
$bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0)
$bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$png.Length); $bw.Write([UInt32]22)
$bw.Write($png); $bw.Close()

# 2) 컴파일
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
$out = if ($OutputPath) { [IO.Path]::GetFullPath($OutputPath) } else { Join-Path $app 'AI팀사무실.exe' }
# 버전: VERSION 파일 + git 커밋 번호를 exe에 새긴다 (파일 속성·화면 표시용)
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$commit = ''
try { $commit = (& git -C $root rev-parse --short HEAD 2>$null); if ($LASTEXITCODE -ne 0) { $commit = '' } } catch { $commit = '' }
$dirty = ''
try { if ((& git -C $root status --porcelain 2>$null)) { $dirty = '-수정중' } } catch { }
$verCs = Join-Path $dist 'Version.cs'
$v4 = (($version -split '\.') + @('0','0','0','0'))[0..3] -join '.'
$verText = @"
[assembly: System.Reflection.AssemblyVersion("$v4")]
[assembly: System.Reflection.AssemblyFileVersion("$v4")]
[assembly: System.Reflection.AssemblyInformationalVersion("$version+$commit$dirty")]
namespace AITeam { static class AppInfo { public const string Version = "$version"; public const string Commit = "$commit$dirty"; } }
"@
[IO.File]::WriteAllText($verCs, $verText, (New-Object Text.UTF8Encoding $true))
$src = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) + $verCs
& $csc /nologo /target:winexe /optimize+ /codepage:65001 /utf8output "/out:$out" "/win32icon:$ico" `
  /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
  "/resource:$(Join-Path $root 'src\ui.html'),ui.html" "/resource:$officeBundle,office.js" `
  "/resource:$(Join-Path $root 'CHANGELOG.md'),CHANGELOG.md" "/resource:$(Join-Path $root 'rules\팀규칙.md'),rules_team.md" "/resource:$(Join-Path $root 'rules\이PC메모.md'),rules_pc.md" `
  "/resource:$(Join-Path $root 'mcp\office\hwp_server.py'),mcp_hwp_server.py" "/resource:$(Join-Path $root 'mcp\office\ppt_server.py'),mcp_ppt_server.py" `
  "/resource:$(Join-Path $root 'mcp\office\com_worker.py'),mcp_com_worker.py" "/resource:$(Join-Path $root 'mcp\office\hwp.py'),mcp_hwp.py" @src
if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }
if (-not $OutputPath) {
  # 설명서와 팀 규칙도 프로그램 폴더에 둔다 (팀 규칙은 사용자가 고친 게 있으면 덮어쓰지 않음)
  Copy-Item (Join-Path $root 'docs\README.txt') (Join-Path $app 'README.txt') -Force
  $rulesDir = Join-Path $app '규칙'
  New-Item -ItemType Directory -Force $rulesDir | Out-Null
  $teamRules = Join-Path $rulesDir '팀규칙.md'
  if (-not (Test-Path $teamRules)) { Copy-Item (Join-Path $root 'rules\팀규칙.md') $teamRules }
}
Write-Host "빌드 완료: $out  (v$version $commit$dirty)"
