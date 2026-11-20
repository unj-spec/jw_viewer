# 폰에서 볼 수 있게 할 PC 폴더를 고른다. 고른 폴더는 folders.txt 에 한 줄로 추가된다.
Add-Type -AssemblyName System.Windows.Forms
$file = Join-Path $PSScriptRoot 'folders.txt'

$dlg = New-Object System.Windows.Forms.FolderBrowserDialog
$dlg.Description = '폰에서 볼 소설 폴더를 고르세요'
$dlg.ShowNewFolderButton = $false
if ($dlg.ShowDialog() -ne 'OK') { exit }

$dir = $dlg.SelectedPath
$list = @()
if (Test-Path $file) { $list = Get-Content $file -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_ } }
if ($list -contains $dir) {
  [System.Windows.Forms.MessageBox]::Show("이미 추가된 폴더입니다.`n$dir", '소설 뷰어') | Out-Null
  exit
}
$list += $dir
Set-Content -Path $file -Value $list -Encoding UTF8
[System.Windows.Forms.MessageBox]::Show("추가했습니다.`n$dir`n`n서버(start.bat)를 다시 켤 필요 없이 앱에서 바로 보입니다.", '소설 뷰어') | Out-Null
