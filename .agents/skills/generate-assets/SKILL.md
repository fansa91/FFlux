---
name: generate-assets
description: >-
  Procedures for generating vector fonts and Lottie animation visual sources from assets.
  Use when vector font definitions, font glyphs, or animation JSON files are modified.
---

# Asset Generation Runbook

Use this runbook when updating icon glyphs, font files, or Lottie animation assets for FFlux.

---

## 1. Prerequisites

Ensure local dotnet tools are restored:
```pwsh
dotnet tool restore
```

---

## 2. Generate Font Assets

When font definitions in `assets/fonts/` are modified or new glyph icons are added:

1. **Run the Font Generation Script**:
   ```pwsh
   pwsh ./scripts/Generate-Fonts.ps1 -Publish
   ```
2. **Generated Output Location**:
   - `FFlux/Assets/Fonts/ScreenboxFluentIcons.ttf`
   - `FFlux/Assets/Fonts/ScreenboxMDL2Assets.ttf`
3. **Verification**:
   - Verify generated `.ttf` files under `FFlux/Assets/Fonts/`.
   - Ensure the new glyphs match the character codes referenced in XAML or code (e.g., `GlyphConverter.cs`, `Icon.xaml`).

---

## 3. Generate Lottie Animation Classes

When animation JSON files in `assets/animations/` are added or modified:

1. **Run the Lottie Generation Script**:
   ```pwsh
   pwsh ./scripts/ConvertFrom-Lottie.ps1
   ```
   This script runs `LottieGen` with parameters:
   - `-GenerateColorBindings`
   - `-GenerateDependencyObject`
   - `-Language CSharp`
   - `-MinimumUapVersion 8`
   - `-Namespace FFlux.Animations`
   - `-Public`
   - `-WinUIVersion 2.8`
   - Target output: `FFlux/Assets/Animations/`
2. **Generated Output Location**:
   - `FFlux/Assets/Animations/*.cs` (e.g., `AnimatedPlayingVisualSource.cs`)
3. **Verification**:
   - Build `FFlux` using MSBuild from Visual Studio 2026 to ensure generated C# classes compile cleanly:
     ```pwsh
     msbuild FFlux/FFlux.csproj /p:Configuration=Debug /p:Platform=x64 /t:Build /m
     ```
