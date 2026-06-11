param (
    [switch]$Build,
    [switch]$NoLaunch,
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [string]$StartupUrl = ""
)

$ProjectPath = "C:\Users\Admin\source\repos\!OpenCode\MediaExplorer\Src\MediaExplorer\MediaExplorer.csproj"
$ProjectDir  = Split-Path $ProjectPath -Parent

function Get-InstalledPackage {
    param([string]$BinDir)
    Get-AppxPackage -ErrorAction SilentlyContinue |
        Where-Object { $_.InstallLocation -eq $BinDir } |
        Select-Object -First 1
}

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
            Start-Sleep -Seconds 2
        }
    } catch { }
}

# optional build
if ($Build) {
    Write-Host "=== Build [$Configuration|$Platform] ==="
    msbuild $ProjectPath /p:Configuration=$Configuration /p:Platform=$Platform /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
}

# register from build output
$binDir = Join-Path $ProjectDir "bin\$Platform\$Configuration"
$manifest = Join-Path $binDir "AppxManifest.xml"

if (-not (Test-Path $manifest)) {
    throw "AppxManifest.xml not found at $manifest. Run with -Build first or build manually."
}

Write-Host "=== Register ==="
Remove-PreviousPackage
Add-AppxPackage -Register $manifest -ForceApplicationShutdown -ForceUpdateFromAnyVersion -ErrorAction Stop
Write-Host "Registration OK"

# write startup URL
if ($StartupUrl) {
    try {
        $pkg = Get-InstalledPackage -BinDir $binDir
        if ($pkg) {
            $localState = Join-Path $env:LOCALAPPDATA "Packages\$($pkg.PackageFamilyName)\LocalState"
            $startupFile = Join-Path $localState "startup_url.txt"
            New-Item -ItemType Directory -Path $localState -Force -ErrorAction SilentlyContinue | Out-Null
            Set-Content -Path $startupFile -Value $StartupUrl -NoNewline -ErrorAction Stop
            Write-Host "Startup URL written: $StartupUrl"
        }
    } catch {
        Write-Host "Warning: could not write startup URL: $_"
    }
}

# launch by default, skip with -NoLaunch
if (-not $NoLaunch) {
    try {
        $pkg = Get-InstalledPackage -BinDir $binDir
        if ($pkg) {
            Write-Host "Launching $($pkg.Name)..."
            $null = Start-Process "shell:AppsFolder\$($pkg.PackageFamilyName)!App"
        }
    } catch {
        Write-Host "Warning: could not launch: $_"
    }
}

Write-Host "=== Deploy complete ==="
