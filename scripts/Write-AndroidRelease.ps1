param(
    [Parameter(Mandatory)][string]$Apk,
    [Parameter(Mandatory)][string]$AndroidSdk,
    [Parameter(Mandatory)][string]$Destination
)
$ErrorActionPreference = 'Stop'
$aapt = Get-ChildItem -LiteralPath (Join-Path $AndroidSdk 'build-tools') -Filter 'aapt.exe' -Recurse |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $aapt) { throw 'Android aapt is unavailable.' }
$badging = & $aapt.FullName dump badging $Apk
if ($LASTEXITCODE -ne 0) { throw 'Cannot read APK version.' }
$package = $badging | Where-Object { $_ -match '^package:' } | Select-Object -First 1
if ($package -notmatch "name='([^']+)' versionCode='([0-9]+)' versionName='([^']+)'") {
    throw 'Invalid APK version metadata.'
}
if ($Matches[1] -ne 'com.nemesxv.hamsterhub') { throw 'Unexpected APK package.' }
[ordered]@{
    packageName = $Matches[1]
    versionCode = [long]$Matches[2]
    versionName = $Matches[3]
    sizeBytes = (Get-Item -LiteralPath $Apk).Length
    sha256 = (Get-FileHash -LiteralPath $Apk -Algorithm SHA256).Hash
} | ConvertTo-Json | Set-Content -LiteralPath $Destination -Encoding ascii
