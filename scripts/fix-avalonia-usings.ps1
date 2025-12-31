$avaloniaRoot = 'P:\ConHoster\src\TerminalHost.Avalonia'

# Get all .cs files excluding VtNetCore, obj, and bin
$files = Get-ChildItem -Path $avaloniaRoot -Include '*.cs' -Recurse | Where-Object {
    $_.FullName -notmatch 'VtNetCore' -and
    $_.FullName -notmatch 'obj\\' -and
    $_.FullName -notmatch 'bin\\'
}

$count = 0
foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw

    # Skip if already has the using
    if ($content -match 'using TerminalHost\.Core\.Interfaces;') {
        continue
    }

    # Check if file uses any interface that would come from Core
    $needsUsing = $content -match 'I(FileSystem|DialogService|ConfigurationService|ClipboardService|StatisticsService|ToastService|ProcessService|GitStatusService|ProfileRegistry|DispatcherService|LinkDetectionService|GitProcessRunner|FolderPickerService|FilePickerService|FileEditService|GitHubService|GitPrService|GitWorktreeService|SearchService|SingleInstanceService|TimerService|TestRunnerService|TimelineService|TaskService|SystemInfoService|ScreenService|AiAssistantService|ClaudeCommandService|ProjectDetectionService|RunUrlDetectionService|MarkdownService|PtyService|TerminalControl|GitIgnoreService|HookEventQueue|DiffParserService|PanelableViewModel|TabViewModel)[^a-zA-Z]'

    if ($needsUsing) {
        # Add using after namespace TerminalHost.Services or TerminalHost.Domain
        if ($content -match '(using TerminalHost\.(Services|Domain);)') {
            $content = $content -replace '(using TerminalHost\.(Services|Domain);)', "using TerminalHost.Core.Interfaces;`r`n`$1"
        } elseif ($content -match '(using System[^;]*;)') {
            $content = $content -replace '(using System[^;]*;)', "using TerminalHost.Core.Interfaces;`r`n`$1"
        }

        Set-Content -Path $file.FullName -Value $content -NoNewline
        Write-Host "Fixed: $($file.Name)"
        $count++
    }
}
Write-Host "Total files fixed: $count"
