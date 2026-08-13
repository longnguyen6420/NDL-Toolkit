# Installer Script for NDL Revit Add-in (Multi-version support)

$baseDir = "D:\NDL"
$net48Dll = "$baseDir\Core\bin\Release\net48\NDLCore.dll"
$net80Dll = "$baseDir\Core\bin\Release\net8.0-windows\NDLCore.dll"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "      CAI DAT ADD-IN NDL CHO REVIT       " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

$appDataAddins = "$env:APPDATA\Autodesk\Revit\Addins"
$programDataAddins = "$env:PROGRAMDATA\Autodesk\Revit\Addins"

$targetFolders = @()

if (Test-Path $appDataAddins) {
    $targetFolders += Get-ChildItem -Path $appDataAddins -Directory | Select-Object -ExpandProperty FullName
}

if (Test-Path $programDataAddins) {
    $targetFolders += Get-ChildItem -Path $programDataAddins -Directory | Select-Object -ExpandProperty FullName
}

$registeredCount = 0

foreach ($folder in $targetFolders) {
    $versionName = Split-Path $folder -Leaf
    [int]$year = 0
    [int]::TryParse($versionName, [ref]$year)

    $targetDll = $net48Dll
    if ($year -ge 2025) {
        $targetDll = $net80Dll
    }

    $manifestContent = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>NDL Tools Loader</Name>
    <Assembly>$targetDll</Assembly>
    <FullClassName>NDL.NDLApp</FullClassName>
    <ClientId>11223344-5566-7788-9900-AABBCCDDEEFF</ClientId>
    <VendorId>NDL</VendorId>
    <VendorDescription>NDL Revit Addin Suite</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

    $targetManifestPath = Join-Path $folder "NDL.addin"
    Set-Content -Path $targetManifestPath -Value $manifestContent -Encoding UTF8 -Force
    Write-Host "[->] Da dang ky NDL (Revit $versionName) vao: $targetManifestPath" -ForegroundColor Yellow
    $registeredCount++
}

Write-Host ""
Write-Host "=========================================" -ForegroundColor Green
Write-Host " THANH CONG! Da dang ky Ribbon NDL cho $registeredCount phien ban Revit." -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green
