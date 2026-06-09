# DeployAndRun.ps1 - Build, unpack, register and run the MediaExplorer app, then show logs.

param (
    [string]$Configuration = "Release",
    [string]$Platform = "x86"
)

function Get-ProjectPath {
    # Use $PSScriptRoot (the folder of this script) to locate the .csproj two levels up.
    $projDir = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    return Join-Path $projDir "MediaExplorer.csproj"
}

function Build-Project {
    $proj = Get-ProjectPath
    Write-Host "Building $proj [$Configuration|$Platform]..."
    msbuild $proj /p:Configuration=$Configuration /p:Platform=$Platform /verbosity:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed."
    }
}

function Find-Package {
    $outDir = Join-Path (Split-Path (Get-ProjectPath)) "bin\$Platform\$Configuration"
    $appx = Get-ChildItem -Path $outDir -Filter *.appx -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($appx) { return $appx.FullName }
    $bundle = Get-ChildItem -Path $outDir -Filter *.appxbundle -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($bundle) { return $bundle.FullName }
    throw "No .appx or .appxbundle found in $outDir."
}

function Unpack-Package {
    param (
        [string]$PackagePath,
        [string]$TempDir
    )
    Write-Host "Unpacking $PackagePath → $TempDir"
    if ($PackagePath -like "*.appxbundle") {
        # first extract the .appx from the bundle
        $bundleExtract = Join-Path $TempDir "bundle_extracted"
        mkdir $bundleExtract -Force | Out-Null
        Expand-Archive -Path $PackagePath -DestinationPath $bundleExtract -Force
        $appxPath = Get-ChildItem -Path $bundleExtract -Filter *.appx -Recurse | Select-Object -First 1
        if (-not $appxPath) { throw "Appx not found inside bundle." }
        $PackagePath = $appxPath.FullName
    }
    # now unpack the .appx
    $appxExtract = Join-Path $TempDir "appx_extracted"
    mkdir $appxExtract -Force | Out-Null
    # makeappx works if installed; fallback to zip extraction
    try {
        makeappx unpack /p $PackagePath /d $appxExtract
    } catch { 
        Expand-Archive -Path $PackagePath -DestinationPath $appxExtract -Force
    }
    # remove signature if present
    $sig = Join-Path $appxExtract "AppxSignature.p7x"
    if (Test-Path $sig) { Remove-Item $sig -Force }
    return $appxExtract
}

function Register-App {
    param ([string]$UnpackedFolder)
    $manifest = Join-Path $UnpackedFolder "AppxManifest.xml"
    if (-not (Test-Path $manifest)) { throw "Manifest not found: $manifest" }
    Write-Host "Registering app ..."
    powershell -NoProfile -Command "add-appxpackage -Register `"$manifest`""
}

function Run-App {
    param ([string]$UnpackedFolder)
    $exe = Get-ChildItem -Path $UnpackedFolder -Filter *.exe -Recurse | Select-Object -First 1
    if (-not $exe) { throw "Executable not found in unpacked folder." }
    Write-Host "Launching $($exe.FullName)..."
    $proc = Start-Process -FilePath $exe.FullName -PassThru
    Write-Host "Press ENTER to stop the app."
    [void][System.Console]::ReadLine()
    if (-not $proc.HasExited) {
        Write-Host "Stopping process ..."
        Stop-Process -Id $proc.Id -Force
    }
}

function Show-Log {
    $logPath = Join-Path $env:UserProfile "Pictures\MediaExplorer\Logger.txt"
    Write-Host "`n--- Content of Logger.txt (`$logPath`) ---`n"
    if (Test-Path $logPath) {
        Get-Content -Path $logPath -Raw
    } else {
        Write-Host "Log file not found."
    }
    Write-Host "`n--- End of log ---`n"
}

# ---- Main flow ----
$projPath = Get-ProjectPath
# Build the MediaExplorer project
Build-Project

# Locate the generated .appx or .appxbundle
$packagePath = Find-Package

# Create a temporary directory for unpacking the package
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "MediaExplorerDeploy_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

# Unpack the package (handles both .appx and .appxbundle)
$unpackedFolder = Unpack-Package -PackagePath $packagePath -TempDir $tempRoot

# Register the unpacked app with the system
Register-App -UnpackedFolder $unpackedFolder

# Launch the app, wait for user to press ENTER, then stop it
Run-App -UnpackedFolder $unpackedFolder

# Show the DevTools log (Logger.txt)
Show-Log
