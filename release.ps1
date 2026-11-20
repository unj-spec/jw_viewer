# 새 버전 내보내기: VERSION 파일의 버전으로 PC 설치 파일과 APK 를 빌드하고 GitHub 릴리스로 올린다.
# PC·폰 앱은 켤 때 이 릴리스를 보고 자동 업데이트한다.
# 쓰는 법: powershell -ExecutionPolicy Bypass -File release.ps1 "바뀐 점 한 줄"
# 필요한 것: gh (GitHub CLI, 로그인), git, 각 빌드에 필요한 도구(README 참고)
param([string]$Notes = '')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Run($exe) {
  & $exe @args
  if ($LASTEXITCODE -ne 0) { throw "실패: $exe $args" }
}

$ver = (Get-Content VERSION -Raw).Trim()
$tag = "v$ver"
if (git status --porcelain) { throw '커밋하지 않은 변경이 있습니다. 먼저 커밋하세요.' }
gh release view $tag 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) { throw "$tag 릴리스가 이미 있습니다. VERSION 의 숫자를 올리세요." }

Write-Host "== $tag 빌드 =="
Run powershell -NoProfile -ExecutionPolicy Bypass -File pc\build.ps1
Run powershell -NoProfile -ExecutionPolicy Bypass -File android\build.ps1

# 앱이 찾는 파일 이름(영문)으로 복사해서 올린다.
New-Item -ItemType Directory -Force dist | Out-Null
Copy-Item -LiteralPath '소설뷰어_PC_설치.exe' dist\jw_viewer_pc_setup.exe -Force
Copy-Item -LiteralPath '소설뷰어.apk' dist\jw_viewer.apk -Force

Write-Host "== $tag 올리기 =="
Run git push
if (-not $Notes) { $Notes = "소설 뷰어 $ver" }
Run gh release create $tag dist\jw_viewer_pc_setup.exe dist\jw_viewer.apk --title "소설 뷰어 $ver" --notes $Notes
Write-Host "`n완료: https://github.com/unj-spec/jw_viewer/releases/tag/$tag"
