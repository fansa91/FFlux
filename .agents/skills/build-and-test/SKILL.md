---
name: build-and-test
description: >-
  Procedures for restoring packages, compiling FFlux and FFlux.Core,
  running automated unit tests, and verifying XAML formatting. Use when the user
  asks to build projects, run tests, or verify code changes.
---

# Build and Test Runbook

Follow these structured procedures when restoring, compiling, testing, or validating formatting in the FFlux solution.

---

## 1. Prerequisites & Package Restore

Ensure local dotnet tools and NuGet packages are restored before building.

> [!IMPORTANT]
> **Always use MSBuild from Visual Studio 2026 (version 18)** for compiling and restoring projects in this repository. **NEVER use `dotnet build`**, as UWP and CsWinRT tooling require Visual Studio 2026 MSBuild.

### Locate Visual Studio 2026 MSBuild
You can locate MSBuild using `vswhere` or standard installation path:
```pwsh
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -version "[18.0,19.0)" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe
if (-not $msbuild) { $msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" }
```

### Restore Dotnet Tools (XAML Styler & LottieGen)
```pwsh
dotnet tool restore
```

### Restore NuGet Packages
For the full solution:
```pwsh
& $msbuild FFlux.slnx -t:restore -p:Platform=x64 -p:Configuration=Debug
```

Or restore the main UWP app project individually:
```pwsh
& $msbuild FFlux/FFlux.csproj /p:Configuration=Debug /p:Platform=x64 /t:Restore /m
```

---

## 2. Compilation / Build

Target the system platform for local development. For example, to build the main UWP app in Debug mode on the `x64` platform:
```pwsh
& $msbuild FFlux/FFlux.csproj /p:Configuration=Debug /p:Platform=x64 /t:Build /m
```

Or build `FFlux.Core`:
```pwsh
& $msbuild FFlux.Core/FFlux.Core.csproj /p:Configuration=Debug /p:Platform=x64 /t:Build /m
```

---

## 3. Running Automated Tests

### Run Full Test Suite
```pwsh
dotnet run --project FFlux.Core.Tests/FFlux.Core.Tests.csproj
```

### Testing Rules & Constraints
- **Pure Logic Tests**: Use `[TestMethod]` in `FFlux.Core.Tests`.
- **XAML-Dependent Tests**: **NEVER** use plain test runners for tests that instantiate XAML types. Use the `UWP Unit Test App` runner with `[UITestMethod]`.
- **No Boilerplate Comments**: Do not emit `// Arrange`, `// Act`, `// Assert` comments.
- **Naming Style**: Mirror existing test method naming in nearby test classes.

---

## 4. XAML Formatting & Lint Verification

Verify all XAML files adhere to XAML Styler rules:

```pwsh
pwsh ./scripts/lint-xaml.ps1
```

To format specific modified XAML files manually:
```pwsh
dotnet tool run xstyler -f FFlux/Pages/PlayerPage.xaml
```
