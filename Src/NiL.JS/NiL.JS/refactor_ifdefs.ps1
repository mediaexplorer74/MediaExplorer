<#
.SYNOPSIS
  Remove dead preprocessor guards from NiL.JS source files.
  NiL.JS now targets ONLY netstandard1.4, so platform guards for
  NETCORE, PORTABLE, NET40, NET35, WRC, NET461, etc. are all dead.

  Symbol table for netstandard1.4:
    Defined:   NETSTANDARD1_4
    Undefined: NETCORE, PORTABLE, NET40, NET35, WRC, NET461, NET48,
               NETSTANDARD1_3, NET40_OR_GREATER, JIT, CALLSTACKTOSTRING,
               DEV, GIVENAMEFUNCTION, TYPE_SAFE
#>

param(
    [string]$RootDir = ".",
    [switch]$WhatIf
)

# ── Known symbols ─────────────────────────────────────────────────────
$definedSymbols   = @("NETSTANDARD1_4")
$undefinedSymbols = @(
    "NETCORE", "PORTABLE", "NET40", "NET35", "WRC", "NET461", "NET48",
    "NETSTANDARD1_3", "NET40_OR_GREATER", "JIT", "CALLSTACKTOSTRING",
    "DEV", "GIVENAMEFUNCTION", "TYPE_SAFE"
)

function Evaluate-Condition($raw) {
    $expr = $raw.Trim()
    # Replace known-defined symbols with '1' (safe token, no $ in regex context)
    foreach ($s in $definedSymbols) {
        $expr = [regex]::Replace($expr, "(?<![a-zA-Z_])$s(?![a-zA-Z_])", '1')
    }
    # Replace known-undefined symbols with '0'
    foreach ($s in $undefinedSymbols) {
        $expr = [regex]::Replace($expr, "(?<![a-zA-Z_])$s(?![a-zA-Z_])", '0')
    }
    # If any unknown uppercase symbol remains, treat as KEEP (return $null)
    # Use -cmatch (case-sensitive) so lowercase letters don't match [A-Z_]
    if ($expr -cmatch '\b[A-Z_][A-Z0-9_]+\b') {
        return $null
    }
    # Convert C# preprocessor syntax to PowerShell boolean syntax
    # Use $$ to produce literal $ in regex replacement ($$ → $)
    $psExpr = $expr `
        -replace '\|\|', ' -or ' `
        -replace '&&', ' -and ' `
        -replace '!', '-not ' `
        -replace '(?<!\w)1(?!\w)', '$$true' `
        -replace '(?<!\w)0(?!\w)', '$$false'
    try {
        return [bool](Invoke-Expression $psExpr -ErrorAction Stop)
    } catch {
        return $null
    }
}

# Classify: "KEEP", "REMOVE_GUARD", "REMOVE_BLOCK"
function Classify-Condition($cond) {
    $r = Evaluate-Condition $cond
    if ($r -eq $null) { return "KEEP" }
    return $(if ($r) { "REMOVE_GUARD" } else { "REMOVE_BLOCK" })
}

# Same as Classify-Condition but with per-file defines merged into the global list
function Classify-ConditionWithDefines($cond, $perFileDefines) {
    $saved = $definedSymbols
    $script:definedSymbols = $definedSymbols + $perFileDefines
    try {
        return Classify-Condition $cond
    } finally {
        $script:definedSymbols = $saved
    }
}

# ── File processing ──────────────────────────────────────────────────
function Process-File($filePath) {
    Write-Host "  $filePath"
    $lines = Get-Content -Path $filePath

    # Scan for per-file #define directives
    $perFileDefines = New-Object System.Collections.ArrayList
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $t = $lines[$i].TrimStart()
        if ($t -match '^#define\s+([A-Za-z_][A-Za-z0-9_]*)') {
            $perFileDefines.Add($matches[1]) | Out-Null
        }
    }
    # Merge per-file defines into $definedSymbols for this file
    $localDefined = $definedSymbols + $perFileDefines

    $out = New-Object System.Collections.ArrayList
    $stack = New-Object System.Collections.ArrayList  # each: @{ class, inElse }

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $raw = $lines[$i]
        $trimmed = $raw.TrimStart()

        # -- #define line: pass through as-is --
        if ($trimmed -match '^#define\b') {
            $out.Add($raw) | Out-Null
            continue
        }

        # -- #undef line: pass through as-is --
        if ($trimmed -match '^#undef\b') {
            $out.Add($raw) | Out-Null
            continue
        }

        # -- #if line --
        if ($trimmed -match '^#if\s+(.*)$') {
            $cond = $matches[1]
            $cls = Classify-ConditionWithDefines $cond $localDefined
            $stack.Add(@{ class = $cls; inElse = $false }) | Out-Null
            if ($cls -eq "KEEP") { $out.Add($raw) | Out-Null }
            continue
        }

        # -- #elif line --
        if ($trimmed -match '^#elif\s+(.*)$') {
            if ($stack.Count -eq 0) { Write-Warning "  #elif orphan at line $($i+1)"; $out.Add($raw) | Out-Null; continue }
            $top = $stack[$stack.Count-1]
            if ($top.class -eq "KEEP") {
                $out.Add($raw) | Out-Null
            }
            elseif ($top.class -eq "REMOVE_GUARD") {
                # #if was already true; skip this #elif and its code
                $top.inElse = $true
            }
            elseif ($top.class -eq "REMOVE_BLOCK") {
                # #if was false; evaluate this #elif independently
                $cond = $matches[1]
                $cls = Classify-ConditionWithDefines $cond $localDefined
                if ($cls -eq "REMOVE_GUARD") {
                    $top.class = "REMOVE_GUARD"
                    $top.inElse = $false
                }
                elseif ($cls -eq "REMOVE_BLOCK") {
                    # still false; stay in REMOVE_BLOCK, keep looking
                    $top.inElse = $false
                }
                else {
                    # unknown -> KEEP
                    $top.class = "KEEP"
                    $out.Add($raw) | Out-Null
                }
            }
            continue
        }

        # -- #else line --
        if ($trimmed -match '^#else\b') {
            if ($stack.Count -eq 0) { Write-Warning "  #else orphan at line $($i+1)"; $out.Add($raw) | Out-Null; continue }
            $top = $stack[$stack.Count-1]
            $top.inElse = $true
            if ($top.class -eq "KEEP") { $out.Add($raw) | Out-Null }
            continue
        }

        # -- #endif line --
        if ($trimmed -match '^#endif\b') {
            if ($stack.Count -eq 0) { Write-Warning "  #endif orphan at line $($i+1)"; $out.Add($raw) | Out-Null; continue }
            $top = $stack[$stack.Count-1]
            if ($top.class -eq "KEEP") { $out.Add($raw) | Out-Null }
            $stack.RemoveAt($stack.Count-1)
            continue
        }

        # -- regular code line --
        $emit = $true
        for ($j = 0; $j -lt $stack.Count; $j++) {
            $s = $stack[$j]
            if ($s.class -eq "REMOVE_GUARD") {
                if ($s.inElse) { $emit = $false; break }
            }
            elseif ($s.class -eq "REMOVE_BLOCK") {
                if (-not $s.inElse) { $emit = $false; break }
            }
        }
        if ($emit) { $out.Add($raw) | Out-Null }
    }

    if ($WhatIf) {
        Write-Host "    $($lines.Count) → $($out.Count) lines"
        return
    }

    # Write output preserving original line endings
    $content = $out -join "`r`n"
    # Add trailing newline
    if ($content -ne "") { $content += "`r`n" }
    Set-Content -Path $filePath -Value $content -NoNewline
}

# ── Main ──────────────────────────────────────────────────────────────
$files = Get-ChildItem -Path $RootDir -Filter *.cs -Recurse |
    Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' }
Write-Host "Found $($files.Count) .cs files"

foreach ($f in $files) {
    Process-File $f.FullName
}

Write-Host "Done."
