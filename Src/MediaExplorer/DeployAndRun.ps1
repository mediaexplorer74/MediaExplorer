param (
    [switch]$SkipBuild,
    [switch]$SkipRegistration,
    [switch]$SkipLog,
    [string]$Configuration = "Debug",
    [string]$Platform = "x86",
    [int]$TimeoutSec = 0,
    [string]$Url = ""
)

$ProjectPath = "C:\Users\Admin\source\repos\!OpenCode\MediaExplorer\Src\MediaExplorer\MediaExplorer.csproj"
$ProjectDir  = Split-Path $ProjectPath -Parent
$BinDir      = Join-Path $ProjectDir "bin\$Platform\$Configuration"

function Remove-PreviousPackage {
    try {
        $existing = Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue |
                    Where-Object { $_.PackageFamilyName -like "*MediaExplorer*" }
        if ($existing) {
            Write-Host "Removing previous MediaExplorer package(s)..."
            foreach ($pkg in $existing) {
                $null = Remove-AppxPackage -Package $pkg.PackageFullName -ErrorAction SilentlyContinue
                $null = Remove-AppxPackage -Package $pkg.PackageFullName -AllUsers -ErrorAction SilentlyContinue
            }
            Start-Sleep -Seconds 1
        }
    } catch { }
}

# step 1: build
if (-not $SkipBuild) {
    Write-Host "=== Build [$Configuration|$Platform] ==="
    msbuild $ProjectPath /p:Configuration=$Configuration /p:Platform=$Platform /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
}

# step 2: find the .appx / .appxbundle
Write-Host "=== Locate package ==="
$appx = Get-ChildItem -Path $ProjectDir -Filter "*_$Platform`_$Configuration.appx" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $appx) { $appx = Get-ChildItem -Path $ProjectDir -Filter "*.appx" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1 }
if (-not $appx) { $appx = Get-ChildItem -Path $ProjectDir -Filter "*.appxbundle" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1 }
if (-not $appx) { throw "No .appx or .appxbundle found" }
Write-Host "Package: $($appx.FullName)"

# step 3: unpack to temp folder
Write-Host "=== Unpack ==="
$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "MediaExplorerDeploy_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
New-Item -ItemType Directory -Path $TempRoot -Force | Out-Null

if ($appx.Extension -eq '.appxbundle') {
    $bundleDir = Join-Path $TempRoot "bundle"
    Expand-Archive -Path $appx.FullName -DestinationPath $bundleDir -Force
    $innerAppx = Get-ChildItem -Path $bundleDir -Filter "*.appx" -Recurse | Select-Object -First 1
    if (-not $innerAppx) { throw "No .appx inside the bundle." }
    $appxPath = $innerAppx.FullName
} else {
    $appxPath = $appx.FullName
}

$Unpacked = Join-Path $TempRoot "appx"
New-Item -ItemType Directory -Path $Unpacked -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($appxPath, $Unpacked)

$sig = Join-Path $Unpacked "AppxSignature.p7x"
if (Test-Path $sig) { Remove-Item $sig -Force }

Write-Host "Unpacked to: $Unpacked"

# step 4: (optional) register
if (-not $SkipRegistration) {
    Write-Host "=== Register ==="
    Remove-PreviousPackage
    $manifest = Join-Path $Unpacked "AppxManifest.xml"
    if (Test-Path $manifest) {
        try {
            Add-AppxPackage -Register $manifest -ForceApplicationShutdown -ForceUpdateFromAnyVersion -ErrorAction Stop
            Write-Host "Registration OK"
        } catch {
            Write-Host "Registration failed: $($_.Exception.Message). Will run exe directly."
            $SkipRegistration = $true
        }
    } else {
        Write-Host "No AppxManifest.xml found; running exe directly."
        $SkipRegistration = $true
    }
}

# step 5: launch app
Write-Host "=== Launch ==="
if ($SkipRegistration) {
    $exe = Join-Path $BinDir "MediaExplorer.exe"
    if (-not (Test-Path $exe)) { throw "Executable not found: $exe" }
    Write-Host "Running: $exe (no registration)"
} else {
    $manifest = Join-Path $Unpacked "AppxManifest.xml"
    $xml = [xml](Get-Content $manifest -Raw)
    $id = $xml.Package.Identity.Name
    $aumid = "$id!App"
    Write-Host "Running (AUMID): $aumid"
}

if ($TimeoutSec -gt 0) {
    Write-Host "Auto-terminate after $TimeoutSec seconds..."
}

# Pass startup URL via environment variable (inherited by child process)
if ($Url) {
    $env:MEDIAEXPLORER_STARTUP_URL = $Url
    Write-Host "Startup URL: $Url"
}

if ($SkipRegistration) {
    $proc = Start-Process -FilePath $exe -PassThru
} else {
    $pkgExe = Get-ChildItem -Path $Unpacked -Filter "*.exe" -Recurse | Select-Object -First 1
    if ($pkgExe) {
        Write-Host "Package exe: $($pkgExe.FullName)"
        $proc = Start-Process -FilePath $pkgExe.FullName -PassThru
    } else {
        Write-Host "No exe found in unpacked folder"
        $proc = $null
    }
}

if ($TimeoutSec -gt 0) {
    Write-Host "Waiting $TimeoutSec seconds..."
    Start-Sleep -Seconds $TimeoutSec
    if ($proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
        Write-Host "App terminated after timeout."
    }
} else {
    Write-Host "Press ENTER to stop the app."
    [void][System.Console]::ReadLine()
    if ($proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
        Write-Host "App terminated."
    }
}

# step 6: dump log
if (-not $SkipLog) {
    # New logger writes to ApplicationData.Current.LocalFolder\Logger.txt
    # That is %LOCALAPPDATA%\Packages\MediaExplorerV0p55_...\LocalState\Logger.txt
    # For unregistered runs, it falls back to %TEMP%\Logger.txt
    $pkg = Get-AppxPackage -Name "MediaExplorerV0p55" -ErrorAction SilentlyContinue
    if ($pkg) {
        $logDir = Join-Path $pkg.InstallLocation "..\LocalState"
        $logDir = (Resolve-Path $logDir -ErrorAction SilentlyContinue).Path
        if (-not $logDir) { $logDir = Join-Path $env:LOCALAPPDATA "Packages\$($pkg.PackageFamilyName)\LocalState" }
    } else {
        $logDir = $env:TEMP
    }
    $logPath = Join-Path $logDir "Logger.txt"
    Write-Host "=== Logger.txt ($logPath) ==="
    if (Test-Path $logPath) {
        Get-Content -Path $logPath -Raw
    } else {
        Write-Host "(file not found)"
    }
    Write-Host "=== End of log ==="
}
