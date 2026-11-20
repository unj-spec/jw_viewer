# 소설 뷰어 안드로이드 앱(APK) 빌드 — Gradle 없이 SDK 도구로 직접 만든다.
# 결과: NovelViewer\소설뷰어.apk
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$sdk   = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { "$env:LOCALAPPDATA\Android\Sdk" }
$bt    = "$sdk\build-tools\35.0.0"
$jar   = "$sdk\platforms\android-35\android.jar"
$out   = 'build'
$apk   = Join-Path (Split-Path $PSScriptRoot) '소설뷰어.apk'
# 버전은 저장소 맨 위 VERSION 파일 하나로 맞춘다(PC·모바일 같이). 1.2.3 → versionCode 10203
$ver   = (Get-Content (Join-Path (Split-Path $PSScriptRoot) 'VERSION') -Raw).Trim()
$p = $ver.Split('.'); $code = [int]$p[0] * 10000 + [int]$p[1] * 100 + [int]$p[2]

function Run($exe) {
  & $exe @args
  if ($LASTEXITCODE -ne 0) { throw "실패: $exe $args" }
}

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force "$out\gen", "$out\classes", "$out\dex", "$out\assets" | Out-Null

Write-Host '1/5 리소스 컴파일'
# 뷰어 화면(NovelViewer\index.html)을 앱 안에 넣는다. 화면을 고치면 앱을 다시 빌드해야 한다.
Copy-Item assets\* "$out\assets"
Copy-Item ..\index.html "$out\assets\index.html"
Run "$bt\aapt2.exe" compile --dir res -o "$out\res.zip"
Run "$bt\aapt2.exe" link -I $jar --manifest AndroidManifest.xml -A "$out\assets" `
  --min-sdk-version 26 --target-sdk-version 34 --version-code $code --version-name $ver `
  --java "$out\gen" -o "$out\unsigned.apk" "$out\res.zip"

Write-Host '2/5 자바 컴파일'
$src = @(Get-ChildItem -Recurse src, "$out\gen" -Filter *.java | ForEach-Object FullName)
Run javac -encoding UTF-8 -source 8 -target 8 -nowarn '-Xlint:-options' -bootclasspath "$jar;$bt\core-lambda-stubs.jar" -d "$out\classes" @src

Write-Host '3/5 DEX 변환'
$classes = @(Get-ChildItem -Recurse "$out\classes" -Filter *.class | ForEach-Object FullName)
Run "$bt\d8.bat" --release --min-api 26 --lib $jar --output "$out\dex" @classes

Write-Host '4/5 APK 묶기'
Push-Location "$out\dex"
Run "$bt\aapt.exe" add ..\unsigned.apk classes.dex | Out-Null
Pop-Location
Run "$bt\zipalign.exe" -f 4 "$out\unsigned.apk" "$out\aligned.apk"

Write-Host '5/5 서명'
# 직접 설치용 서명 키. 처음 한 번 만들고 계속 같은 키를 써야 앱을 덮어서 업데이트할 수 있다.
if (-not (Test-Path 'release.keystore')) {
  Run keytool -genkeypair -keystore release.keystore -storepass novelviewer -keypass novelviewer `
    -alias novelviewer -keyalg RSA -keysize 2048 -validity 10000 -dname 'CN=Novel Viewer'
}
Run "$bt\apksigner.bat" sign --ks release.keystore --ks-pass pass:novelviewer --key-pass pass:novelviewer `
  --v4-signing-enabled false --out $apk "$out\aligned.apk"

Write-Host "`n완료: $apk"
