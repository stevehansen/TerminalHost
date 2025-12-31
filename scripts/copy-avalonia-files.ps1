# Phase 3: Copy Avalonia files from macos_only branch to TerminalHost.Avalonia
# This script extracts files without switching branches

param(
    [switch]$DryRun,
    [switch]$VtNetCoreOnly,
    [switch]$ViewsOnly,
    [switch]$ControlsOnly
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceBranch = "macos_only"
$sourceBase = "src/TerminalHost/TerminalHost"
$targetBase = "src/TerminalHost.Avalonia"

# Files/directories to skip (already in Core or macOS projects)
$skipPatterns = @(
    "*/Services/Mac*.cs",           # Already in TerminalHost.macOS
    "*/Resources/pty_helper.py",    # Already in TerminalHost.macOS
    "*.wpf.bak"                     # Backup files
)

# Domain files that should stay in Core (skip copying)
$coreDomainFiles = @(
    "CommandLineArgs.cs",
    "HookEvent.cs",
    "SessionState.cs"
    # Add more as needed
)

function Should-Skip($path) {
    foreach ($pattern in $skipPatterns) {
        if ($path -like $pattern) { return $true }
    }
    return $false
}

function Get-TargetPath($relativePath) {
    return Join-Path (Join-Path $repoRoot $targetBase) $relativePath
}

function Copy-FileFromBranch($sourcePath, $targetPath) {
    if ($DryRun) {
        Write-Host "[DRY RUN] Would copy: $sourcePath -> $targetPath"
        return
    }

    $targetDir = Split-Path -Parent $targetPath
    if (-not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    # Use git show to extract file content
    $content = git -C $repoRoot show "${sourceBranch}:${sourcePath}" 2>$null
    if ($LASTEXITCODE -eq 0 -and $content) {
        # Write with UTF-8 encoding without BOM
        [System.IO.File]::WriteAllText($targetPath, ($content -join "`n"), [System.Text.UTF8Encoding]::new($false))
        Write-Host "Copied: $sourcePath"
    } else {
        Write-Warning "Failed to extract: $sourcePath"
    }
}

function Get-FilesFromBranch($pathFilter) {
    $files = git -C $repoRoot ls-tree -r --name-only $sourceBranch -- "$sourceBase/$pathFilter" 2>$null
    return $files | Where-Object { $_ -and -not (Should-Skip $_) }
}

# Main execution
Write-Host "=== Phase 3: Copy Avalonia Files ===" -ForegroundColor Cyan
Write-Host "Source: $sourceBranch branch"
Write-Host "Target: $targetBase"
if ($DryRun) { Write-Host "[DRY RUN MODE]" -ForegroundColor Yellow }
Write-Host ""

$totalCopied = 0

# 1. VtNetCore (terminal emulation)
if (-not $ViewsOnly -and -not $ControlsOnly) {
    Write-Host "--- VtNetCore Files ---" -ForegroundColor Green
    $vtFiles = Get-FilesFromBranch "VtNetCore"
    foreach ($file in $vtFiles) {
        $relativePath = $file -replace "^$([regex]::Escape($sourceBase))/", ""
        $targetPath = Get-TargetPath $relativePath
        Copy-FileFromBranch $file $targetPath
        $totalCopied++
    }
    Write-Host "VtNetCore: $($vtFiles.Count) files" -ForegroundColor Gray
}

# 2. Controls (MacTerminalControl, DiffViewer, etc.)
if (-not $VtNetCoreOnly -and -not $ViewsOnly) {
    Write-Host "`n--- Controls ---" -ForegroundColor Green
    $controlFiles = Get-FilesFromBranch "Controls"
    foreach ($file in $controlFiles) {
        $relativePath = $file -replace "^$([regex]::Escape($sourceBase))/", ""
        $targetPath = Get-TargetPath $relativePath
        Copy-FileFromBranch $file $targetPath
        $totalCopied++
    }
    Write-Host "Controls: $($controlFiles.Count) files" -ForegroundColor Gray
}

# 3. Views (.axaml files and code-behind)
if (-not $VtNetCoreOnly -and -not $ControlsOnly) {
    Write-Host "`n--- Views ---" -ForegroundColor Green
    $viewFiles = Get-FilesFromBranch "Views"
    foreach ($file in $viewFiles) {
        $relativePath = $file -replace "^$([regex]::Escape($sourceBase))/", ""
        $targetPath = Get-TargetPath $relativePath
        Copy-FileFromBranch $file $targetPath
        $totalCopied++
    }
    Write-Host "Views: $($viewFiles.Count) files" -ForegroundColor Gray
}

# 4. App.axaml and related
if (-not $VtNetCoreOnly -and -not $ViewsOnly -and -not $ControlsOnly) {
    Write-Host "`n--- App Files ---" -ForegroundColor Green
    $appFiles = @(
        "$sourceBase/App.axaml",
        "$sourceBase/App.axaml.cs",
        "$sourceBase/AssemblyInfo.cs"
    )
    foreach ($file in $appFiles) {
        if (git -C $repoRoot ls-tree --name-only $sourceBranch -- $file 2>$null) {
            $relativePath = $file -replace "^$([regex]::Escape($sourceBase))/", ""
            $targetPath = Get-TargetPath $relativePath
            Copy-FileFromBranch $file $targetPath
            $totalCopied++
        }
    }

    # MainWindow
    $mainWindowFiles = @(
        "$sourceBase/MainWindow.axaml",
        "$sourceBase/MainWindow.axaml.cs"
    )
    foreach ($file in $mainWindowFiles) {
        if (git -C $repoRoot ls-tree --name-only $sourceBranch -- $file 2>$null) {
            $relativePath = $file -replace "^$([regex]::Escape($sourceBase))/", ""
            $targetPath = Get-TargetPath $relativePath
            Copy-FileFromBranch $file $targetPath
            $totalCopied++
        }
    }
}

# 5. Converters
if (-not $VtNetCoreOnly) {
    Write-Host "`n--- Converters ---" -ForegroundColor Green
    $converterFile = "$sourceBase/Converters/Converters.cs"
    if (git -C $repoRoot ls-tree --name-only $sourceBranch -- $converterFile 2>$null) {
        $targetPath = Get-TargetPath "Converters/Converters.cs"
        Copy-FileFromBranch $converterFile $targetPath
        $totalCopied++
    }
}

# 6. Assets (fonts, icons)
if (-not $VtNetCoreOnly -and -not $ViewsOnly -and -not $ControlsOnly) {
    Write-Host "`n--- Assets ---" -ForegroundColor Green
    $assetFiles = Get-FilesFromBranch "Assets"
    foreach ($file in $assetFiles) {
        $relativePath = $file -replace "^$([regex]::Escape($sourceBase))/", ""
        $targetPath = Get-TargetPath $relativePath

        # For binary files, use a different approach
        if ($file -match "\.(ttf|png|ico|jpg|jpeg|gif)$") {
            if (-not $DryRun) {
                $targetDir = Split-Path -Parent $targetPath
                if (-not (Test-Path $targetDir)) {
                    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
                }
                git -C $repoRoot show "${sourceBranch}:${file}" 2>$null | Set-Content -Path $targetPath -Encoding Byte
                Write-Host "Copied (binary): $file"
            } else {
                Write-Host "[DRY RUN] Would copy (binary): $file -> $targetPath"
            }
        } else {
            Copy-FileFromBranch $file $targetPath
        }
        $totalCopied++
    }
}

# 7. Resources folder
if (-not $VtNetCoreOnly -and -not $ViewsOnly -and -not $ControlsOnly) {
    Write-Host "`n--- Resources ---" -ForegroundColor Green
    $resourceFiles = Get-FilesFromBranch "Resources"
    foreach ($file in $resourceFiles) {
        if (Should-Skip $file) { continue }
        $relativePath = $file -replace "^$([regex]::Escape($sourceBase))/", ""
        $targetPath = Get-TargetPath $relativePath
        Copy-FileFromBranch $file $targetPath
        $totalCopied++
    }
}

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "Total files processed: $totalCopied"
if ($DryRun) {
    Write-Host "Run without -DryRun to actually copy files" -ForegroundColor Yellow
}
