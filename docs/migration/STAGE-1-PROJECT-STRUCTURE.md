# Stage 1: Project Structure & Build System

## Overview

| Attribute | Value |
|-----------|-------|
| **Estimated Effort** | 2-3 days |
| **Risk Level** | Low |
| **Dependencies** | None (first stage) |
| **Blocking For** | All subsequent stages |

## Objective

Convert the project from a Windows-only WPF application to a macOS-only Avalonia application by updating all build configurations, NuGet packages, and project structure.

## Success Criteria

- [ ] Project builds with `dotnet build` on macOS
- [ ] No Windows-specific framework references remain
- [ ] All new Avalonia packages resolve correctly
- [ ] Basic console output runs on macOS
- [ ] Solution structure is clean and organized

---

## Detailed Tasks

### 1.1 Update TerminalHost.csproj

**File:** `src/TerminalHost/TerminalHost/TerminalHost.csproj`

#### 1.1.1 Change Target Framework

**Current (lines 4-5):**
```xml
<OutputType>WinExe</OutputType>
<TargetFramework>net8.0-windows</TargetFramework>
```

**New:**
```xml
<OutputType>WinExe</OutputType>
<TargetFramework>net8.0</TargetFramework>
```

#### 1.1.2 Remove Windows-Specific Properties

**DELETE these lines (8-13):**
```xml
<UseWPF>true</UseWPF>
<UseWindowsForms>true</UseWindowsForms>
<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
```

#### 1.1.3 Update Runtime Identifier

**Current (line 19):**
```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

**New:**
```xml
<RuntimeIdentifiers>osx-arm64;osx-x64</RuntimeIdentifiers>
```

#### 1.1.4 Remove Windows Icon Reference

**DELETE (line 12):**
```xml
<ApplicationIcon>Resources\app.ico</ApplicationIcon>
```

#### 1.1.5 Update NuGet Packages

**REMOVE these PackageReferences (lines 30-37):**
```xml
<PackageReference Include="EasyWindowsTerminalControl" Version="1.0.36" ExcludeAssets="symbols" />
<PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" Version="2.0.0-rc5.4" />
<PackageReference Include="Microsoft.Web.WebView2" Version="1.0.3650.58" />
```

**ADD these PackageReferences:**
```xml
<!-- Avalonia UI Framework -->
<PackageReference Include="Avalonia" Version="11.2.1" />
<PackageReference Include="Avalonia.Desktop" Version="11.2.1" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.1" />
<PackageReference Include="Avalonia.Fonts.Inter" Version="11.2.1" />

<!-- Development tools -->
<PackageReference Include="Avalonia.Diagnostics" Version="11.2.1" Condition="'$(Configuration)' == 'Debug'" />

<!-- Charts for Avalonia -->
<PackageReference Include="LiveChartsCore.SkiaSharpView.Avalonia" Version="2.0.0-rc5.4" />

<!-- Terminal PTY support -->
<PackageReference Include="Pty.Net" Version="0.5.96" />
```

#### 1.1.6 Remove ConPTY Native Library Copy

**DELETE entire ItemGroup (lines 84-90):**
```xml
<ItemGroup>
  <None Include="$(NuGetPackageRoot)ci.microsoft.windows.console.conpty\1.22.250314001\runtimes\win10-x64\native\conpty.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Link>conpty.dll</Link>
  </None>
</ItemGroup>
```

#### 1.1.7 Remove WPF Type Aliases

**DELETE entire ItemGroup with Using elements (lines 41-82):**
```xml
<ItemGroup>
  <Using Include="System.Windows.Input.KeyEventArgs" Alias="KeyEventArgs" />
  <!-- ... all 24 Using elements ... -->
  <Using Include="System.Windows.Media.FontFamily" Alias="FontFamily" />
</ItemGroup>
```

#### 1.1.8 Update Resource Reference

**Current (line 26):**
```xml
<Resource Include="Resources\app.ico" />
```

**New:**
```xml
<AvaloniaResource Include="Resources\**\*" />
```

#### 1.1.9 Final TerminalHost.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>TerminalHost</RootNamespace>
    <AssemblyName>host</AssemblyName>
    <NoWarn>$(NoWarn);NU1701</NoWarn>

    <!-- macOS publish settings -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifiers>osx-arm64;osx-x64</RuntimeIdentifiers>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <CopyOutputSymbolsToPublishDirectory>false</CopyOutputSymbolsToPublishDirectory>
  </PropertyGroup>

  <ItemGroup>
    <AvaloniaResource Include="Resources\**\*" />
  </ItemGroup>

  <ItemGroup>
    <!-- Avalonia UI Framework -->
    <PackageReference Include="Avalonia" Version="11.2.1" />
    <PackageReference Include="Avalonia.Desktop" Version="11.2.1" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.1" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.2.1" />
    <PackageReference Include="Avalonia.Diagnostics" Version="11.2.1" Condition="'$(Configuration)' == 'Debug'" />

    <!-- Existing cross-platform packages -->
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    <PackageReference Include="Markdig" Version="0.44.0" />
    <PackageReference Include="Markdig.SyntaxHighlighting" Version="1.1.7" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.1" />
    <PackageReference Include="System.Text.Json" Version="10.0.1" />

    <!-- Avalonia-compatible charts -->
    <PackageReference Include="LiveChartsCore.SkiaSharpView.Avalonia" Version="2.0.0-rc5.4" />

    <!-- Terminal PTY support -->
    <PackageReference Include="Pty.Net" Version="0.5.96" />
  </ItemGroup>

</Project>
```

---

### 1.2 Remove WPF ThemeInfo from AssemblyInfo.cs (Gap Fix)

**File:** `src/TerminalHost/TerminalHost/AssemblyInfo.cs`

This file contains WPF-specific attributes that must be removed.

**DELETE these lines:**
```csharp
using System.Windows;

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
```

The `[ThemeInfo]` attribute is WPF-specific and tells WPF where to look for theme resources. Avalonia doesn't use this mechanism - themes are configured in `App.axaml` instead.

**After cleanup, the file should only contain:**
```csharp
// Any remaining assembly-level attributes that are platform-agnostic
// Or the file can be deleted entirely if empty
```

---

### 1.3 Create Global Usings File

**CREATE:** `src/TerminalHost/TerminalHost/GlobalUsings.cs`

```csharp
// Avalonia core
global using Avalonia;
global using Avalonia.Controls;
global using Avalonia.Controls.Primitives;
global using Avalonia.Input;
global using Avalonia.Interactivity;
global using Avalonia.Layout;
global using Avalonia.Media;
global using Avalonia.Threading;

// Avalonia type aliases (matching previous WPF aliases)
global using KeyEventArgs = Avalonia.Input.KeyEventArgs;
global using MouseEventArgs = Avalonia.Input.PointerEventArgs;
global using TextBox = Avalonia.Controls.TextBox;
global using Button = Avalonia.Controls.Button;
global using Orientation = Avalonia.Layout.Orientation;
global using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
global using Application = Avalonia.Application;
global using Point = Avalonia.Point;
global using UserControl = Avalonia.Controls.UserControl;
global using Color = Avalonia.Media.Color;
global using Colors = Avalonia.Media.Colors;
global using Brush = Avalonia.Media.IBrush;
global using SolidColorBrush = Avalonia.Media.SolidColorBrush;
global using FontFamily = Avalonia.Media.FontFamily;

// Standard .NET
global using System;
global using System.Collections.Generic;
global using System.Collections.ObjectModel;
global using System.ComponentModel;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
```

---

### 1.4 Solution/Project Structure Alignment (Gap Fix)

**Observation:** The repository contains existing `src/TerminalHost.Avalonia` and `src/TerminalHost.Platform.macOS` folders, but the solution currently references the WPF project.

**Decision Required:** Choose one of these approaches:

**Option A: Migrate In-Place (Recommended)**
- Convert existing `src/TerminalHost/TerminalHost` project directly to Avalonia
- Removes need to merge code between projects
- Preserves git history
- Remove or repurpose the existing Avalonia/Platform folders

**Option B: Use Existing Avalonia Project**
- Move code to the existing `src/TerminalHost.Avalonia` project
- Cleaner separation but requires more file reorganization
- May lose some git history context

**Action Items:**
1. Decide on approach before starting migration
2. Update `TerminalHost.sln` to reference the target project definitively
3. Remove or archive unused project folders to avoid confusion
4. Ensure only one entry point exists post-migration

**If choosing Option A (recommended):**
```bash
# Remove placeholder projects if not needed
rm -rf src/TerminalHost.Avalonia
rm -rf src/TerminalHost.Platform.macOS
```

---

### 1.5 Update Test Projects

#### 1.5.1 Unit Test Project

**File:** `tests/TerminalHost.Tests/TerminalHost.Tests.csproj`

**Change (line 4):**
```xml
<!-- Current -->
<TargetFramework>net8.0-windows</TargetFramework>

<!-- New -->
<TargetFramework>net8.0</TargetFramework>
```

#### 1.5.2 Delete UI Test Project

**DELETE entire directory:** `tests/TerminalHost.UITests/`

FlaUI is Windows-only (uses UI Automation API). A new macOS test project will be created in Stage 8.

---

### 1.6 Create macOS Application Bundle Files

#### 1.6.1 Info.plist

**CREATE:** `src/TerminalHost/TerminalHost/Info.plist`

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>TerminalHost</string>

    <key>CFBundleDisplayName</key>
    <string>Terminal Host</string>

    <key>CFBundleIdentifier</key>
    <string>com.terminalhost.app</string>

    <key>CFBundleVersion</key>
    <string>1.0.0</string>

    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>

    <key>CFBundleExecutable</key>
    <string>host</string>

    <key>CFBundlePackageType</key>
    <string>APPL</string>

    <key>CFBundleIconFile</key>
    <string>app.icns</string>

    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>

    <key>NSHighResolutionCapable</key>
    <true/>

    <key>NSHumanReadableCopyright</key>
    <string>Copyright © 2024</string>

    <key>LSApplicationCategoryType</key>
    <string>public.app-category.developer-tools</string>

    <key>NSAppleEventsUsageDescription</key>
    <string>TerminalHost needs to run terminal commands.</string>

    <key>NSDocumentsFolderUsageDescription</key>
    <string>TerminalHost needs access to your documents for project management.</string>
</dict>
</plist>
```

#### 1.6.2 Entitlements.plist

**CREATE:** `src/TerminalHost/TerminalHost/Entitlements.plist`

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <!-- Allow spawning child processes (for terminal) -->
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key>
    <true/>

    <!-- Allow JIT compilation (may be needed for some libraries) -->
    <key>com.apple.security.cs.allow-jit</key>
    <true/>

    <!-- Network access for git operations -->
    <key>com.apple.security.network.client</key>
    <true/>

    <!-- File access -->
    <key>com.apple.security.files.user-selected.read-write</key>
    <true/>

    <!-- Inherit from parent for child processes -->
    <key>com.apple.security.inherit</key>
    <true/>
</dict>
</plist>
```

---

### 1.7 Update Resources

#### 1.7.1 Delete Windows Icon

**DELETE:** `src/TerminalHost/TerminalHost/Resources/app.ico`

#### 1.7.2 Create macOS Icon

**CREATE:** `src/TerminalHost/TerminalHost/Resources/app.icns`

To create an .icns file:
1. Create a 1024x1024 PNG icon
2. Use `iconutil` or a tool like [Image2Icon](https://img2icnsapp.com/)
3. Place the resulting `app.icns` in Resources/

**Temporary placeholder (use existing icon converted):**
```bash
# On macOS, convert existing icon or create new one
# If you have a PNG, use:
mkdir app.iconset
# Add required sizes: 16, 32, 128, 256, 512, 1024 (and @2x versions)
iconutil -c icns app.iconset -o Resources/app.icns
```

---

### 1.8 Update Solution File

**File:** `TerminalHost.sln`

Remove the UI test project reference:

**DELETE these lines:**
```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TerminalHost.UITests", "tests\TerminalHost.UITests\TerminalHost.UITests.csproj", "{GUID}"
EndProject
```

Also delete any corresponding `GlobalSection` entries for this project.

---

### 1.9 Create Avalonia Entry Point

**CREATE:** `src/TerminalHost/TerminalHost/Program.cs`

```csharp
using Avalonia;
using System;

namespace TerminalHost;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
```

---

### 1.10 Create Stub App.axaml

These are temporary stubs to verify the build works. Full implementation comes in Stage 5.

**CREATE:** `src/TerminalHost/TerminalHost/App.axaml`

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="TerminalHost.App"
             RequestedThemeVariant="Dark">
    <Application.Styles>
        <FluentTheme />
    </Application.Styles>
</Application>
```

**CREATE:** `src/TerminalHost/TerminalHost/App.axaml.cs`

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace TerminalHost;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Temporary: Create a simple window to verify build works
            desktop.MainWindow = new Avalonia.Controls.Window
            {
                Title = "TerminalHost - Build Verification",
                Width = 800,
                Height = 600,
                Content = new Avalonia.Controls.TextBlock
                {
                    Text = "Stage 1 Complete: Build system working!",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    FontSize = 24
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

---

### 1.11 Temporarily Exclude Files from Compilation

Until later stages complete the migration, exclude WPF-specific files:

**Add to TerminalHost.csproj:**
```xml
<ItemGroup>
  <!-- Temporarily exclude WPF files until migrated -->
  <Compile Remove="App.xaml.cs" />
  <Compile Remove="MainWindow.xaml.cs" />
  <Compile Remove="Views\**\*.cs" />
  <Compile Remove="Controls\**\*.cs" />
  <Compile Remove="Domain\TerminalSession.cs" />
  <Compile Remove="Services\DarkModeHelper.cs" />
  <Compile Remove="Services\SystemTrayService.cs" />
  <Compile Remove="Services\TerminalControlFactory.cs" />
  <Compile Remove="Services\DialogService.cs" />
  <Compile Remove="Services\ToastService.cs" />
  <Compile Remove="ViewModels\**\*.cs" />
  <Compile Remove="Converters.cs" />

  <!-- Exclude XAML files (will be replaced with AXAML) -->
  <None Remove="**\*.xaml" />
</ItemGroup>
```

---

## File Change Summary

| Action | File | Notes |
|--------|------|-------|
| **MODIFY** | `TerminalHost.csproj` | Major changes |
| **MODIFY** | `AssemblyInfo.cs` | **NEW** - Remove WPF ThemeInfo |
| **CREATE** | `GlobalUsings.cs` | New file |
| **CREATE** | `Program.cs` | Avalonia entry point |
| **CREATE** | `App.axaml` | Stub Avalonia app |
| **CREATE** | `App.axaml.cs` | Stub Avalonia app |
| **CREATE** | `Info.plist` | macOS bundle metadata |
| **CREATE** | `Entitlements.plist` | macOS permissions |
| **CREATE** | `Resources/app.icns` | macOS icon |
| **DELETE** | `Resources/app.ico` | Windows icon |
| **MODIFY** | `TerminalHost.Tests.csproj` | Change target framework |
| **DELETE** | `tests/TerminalHost.UITests/` | Entire directory |
| **MODIFY** | `TerminalHost.sln` | Remove UI test project |
| **DELETE** | `src/TerminalHost.Avalonia/` | **NEW** - Remove placeholder (if Option A) |
| **DELETE** | `src/TerminalHost.Platform.macOS/` | **NEW** - Remove placeholder (if Option A) |

---

## Verification Steps

### Build Verification
```bash
cd /path/to/TerminalHost
dotnet restore
dotnet build
```

Expected: Build succeeds with no errors (warnings acceptable)

### Run Verification
```bash
dotnet run --project src/TerminalHost/TerminalHost
```

Expected: Window opens showing "Stage 1 Complete: Build system working!"

### Publish Verification
```bash
dotnet publish src/TerminalHost/TerminalHost -c Release -r osx-arm64
```

Expected: Creates output in `bin/Release/net8.0/osx-arm64/publish/`

---

## Rollback Plan

If issues arise:
1. Revert all changes via git: `git checkout .`
2. Restore deleted files from git history
3. Document specific failure for investigation

---

## Next Stage

After completing Stage 1, proceed to **Stage 2: Service Layer Abstractions** which creates platform-agnostic service interfaces.
