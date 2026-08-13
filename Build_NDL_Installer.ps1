# Master Build and Installer Packager Script for NDL Revit Tools Suite
# Run this script whenever a new tool is added to D:\NDL!

$baseDir = "D:\NDL"
Set-Location $baseDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   NDL AUTOMATED BUILD AND SETUP PACKAGER SYSTEM          " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Compile all tool projects in D:\NDL
$subDirs = Get-ChildItem -Path $baseDir -Directory
$toolsCompiled = @()

foreach ($dir in $subDirs) {
    if ($dir.Name.StartsWith(".") -or $dir.Name -eq "NDL_Installer") {
        continue
    }

    $csproj = Get-ChildItem -Path $dir.FullName -Filter "*.csproj" -Recurse | Select-Object -First 1
    if ($csproj) {
        Write-Host "[BUILD] Compiling Tool: '$($dir.Name)'..." -ForegroundColor Yellow
        dotnet build $csproj.FullName -c Release --nologo
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  -> [OK] Success!" -ForegroundColor Green
            if ($dir.Name -ne "Core") {
                $toolsCompiled += $dir.Name
            }
        } else {
            Write-Host "  -> [FAIL] Build Failed!" -ForegroundColor Red
        }
    }
}

# 2. Compile NDL_Setup.exe Installer GUI
Write-Host ""
Write-Host "[BUILD] Creating latest NDL_Setup.exe..." -ForegroundColor Yellow
$installerCsproj = "$baseDir\NDL_Installer\NDL_Installer.csproj"

if (Test-Path $installerCsproj) {
    dotnet build $installerCsproj -c Release --nologo
    if ($LASTEXITCODE -eq 0) {
        $setupSource = "$baseDir\NDL_Installer\bin\Release\net48\NDL_Setup.exe"
        $setupDest = "$baseDir\NDL_Setup.exe"
        Copy-Item -Path $setupSource -Destination $setupDest -Force
        Write-Host "  -> [OK] Updated NDL_Setup.exe at: $setupDest" -ForegroundColor Green
    }
}

# 3. Create 1-file Portable SFX Executable for Sharing (NDL_Revit_Tools_Installer.exe)
Write-Host ""
Write-Host "[PACK] Creating 1-file Standalone Executable: NDL_Revit_Tools_Installer.exe..." -ForegroundColor Yellow
$winrarRar = "C:\Program Files\WinRAR\rar.exe"
$sfxConfig = "$baseDir\sfx_config.txt"
$sfxOutputFile = "$baseDir\NDL_Revit_Tools_Installer.exe"

if (Test-Path $winrarRar) {
    & $winrarRar a -sfx -z"$sfxConfig" -r -x"*.git*" -x"*obj*" "$sfxOutputFile" "$baseDir\*"
    if (Test-Path $sfxOutputFile) {
        Write-Host "  -> [OK] Successfully created 1-file Installer: $sfxOutputFile" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "   SUMMARY: NDL TOOLS BUILD & PACKAGING COMPLETE" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host " - Total Tools Loaded and Compiled: $($toolsCompiled.Count)" -ForegroundColor White
foreach ($t in $toolsCompiled) {
    Write-Host "   - $t" -ForegroundColor Gray
}
Write-Host " - Internal Installer: D:\NDL\NDL_Setup.exe" -ForegroundColor Yellow
Write-Host " - 1-FILE EXE TO SHARE: D:\NDL\NDL_Revit_Tools_Installer.exe" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
