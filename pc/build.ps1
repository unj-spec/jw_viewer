# 소설 뷰어 PC 버전 빌드 — 윈도우 기본 .NET Framework 의 C# 컴파일러로 exe 를 만들고, 설치 파일까지 만든다.
# 결과: NovelViewer\소설뷰어_PC_설치.exe
# 필요한 것: Inno Setup 6 (https://jrsoftware.org/isdl.php). 그 밖에 받을 것은 없다.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$out  = 'build'
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
          "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 이 없습니다. https://jrsoftware.org/isdl.php 에서 설치하세요.' }

function Run($exe) {
  & $exe @args
  if ($LASTEXITCODE -ne 0) { throw "실패: $exe $args" }
}

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null

Write-Host '1/3 아이콘'
# 파란 바탕에 펼친 책. 256px PNG 를 담은 ico 로 저장한다.
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$bg = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 56
$bg.AddArc(0, 0, $r, $r, 180, 90); $bg.AddArc(256 - $r, 0, $r, $r, 270, 90)
$bg.AddArc(256 - $r, 256 - $r, $r, $r, 0, 90); $bg.AddArc(0, 256 - $r, $r, $r, 90, 90); $bg.CloseFigure()
$g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(59, 111, 212))), $bg)
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
foreach ($side in -1, 1) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $x0 = 128 + $side * 4; $x1 = 128 + $side * 78
  $p.AddBezier($x0, 80, $x0 + $side * 20, 64, $x1 - $side * 30, 58, $x1, 72)
  $p.AddLine($x1, 72, $x1, 192)
  $p.AddBezier($x1, 192, $x1 - $side * 30, 178, $x0 + $side * 20, 184, $x0, 200)
  $p.CloseFigure()
  $g.FillPath($white, $p)
}
$g.Dispose()
$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $ms.ToArray()
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]1)              # 헤더: 아이콘 1개
$w.Write([Byte]0); $w.Write([Byte]0); $w.Write([Byte]0); $w.Write([Byte]0) # 256x256
$w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$png.Length); $w.Write([UInt32]22)
$w.Write($png); $w.Flush()
[System.IO.File]::WriteAllBytes("$PSScriptRoot\$out\icon.ico", $ico.ToArray())

Write-Host '2/3 프로그램 컴파일'
# 버전은 저장소 맨 위 VERSION 파일 하나로 맞춘다(PC·모바일 같이).
$ver = (Get-Content (Join-Path (Split-Path $PSScriptRoot) 'VERSION') -Raw).Trim()
[IO.File]::WriteAllText("$PSScriptRoot\$out\Version.cs",
  "[assembly: System.Reflection.AssemblyVersion(`"$ver.0`")]`nnamespace NovelViewer { static class AppInfo { public const string Version = `"$ver`"; } }`n")
$src = @(Get-ChildItem src -Filter *.cs | ForEach-Object FullName) + "$PSScriptRoot\$out\Version.cs"
# 인자는 따옴표로 감싼다(PowerShell 이 "-이름:값" 을 둘로 나누지 않게).
Run $csc '-nologo' '-target:winexe' '-optimize' '-codepage:65001' "-win32icon:$out\icon.ico" "-out:$out\NovelViewer.exe" `
  '-r:System.Web.Extensions.dll' '-r:System.Security.dll' '-r:System.Windows.Forms.dll' '-r:System.Drawing.dll' '-r:Microsoft.VisualBasic.dll' @src

Write-Host '3/3 설치 파일'
Run $iscc /Q "/DAppVer=$ver" installer.iss

Write-Host "`n완료: $(Join-Path (Split-Path $PSScriptRoot) '소설뷰어_PC_설치.exe')"
