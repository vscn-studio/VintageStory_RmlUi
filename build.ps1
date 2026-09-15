<#
Copyright (c) 2026 VSCN-Studio
SPDX-License-Identifier: MIT
#>

param(
    [string]$GameDirectory = (Join-Path $PSScriptRoot '../Vintagestory'),
    [string]$RmlUiSource = (Join-Path $PSScriptRoot '../RmlUi'),
    [string]$GameNativeDirectory,
    [string]$BundleNative,
    [switch]$NativeOnly,
    [switch]$PrepareOnly,
    [switch]$PackageOnly,
    [switch]$SkipTests,
    [switch]$HeadlessTests
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$pythonNames = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) { @('python', 'python3') } else { @('python3', 'python') }
$pythonCommand = $null
foreach ($pythonName in $pythonNames) {
    $candidate = Get-Command $pythonName -ErrorAction SilentlyContinue
    if ($candidate -and $candidate.Source -notlike '*\Microsoft\WindowsApps\*') { $pythonCommand = $candidate; break }
}
if (-not $pythonCommand) { throw 'Install Python 3.10+ and put it on PATH (Windows Store execution aliases are not a Python installation).' }
$buildArguments = @((Join-Path $PSScriptRoot 'build.py'), '--game-directory', $GameDirectory, '--rmlui-source', $RmlUiSource)
if ($GameNativeDirectory) { $buildArguments += @('--game-native-directory', $GameNativeDirectory) }
if ($BundleNative) { $buildArguments += @('--bundle-native', $BundleNative) }
if ($NativeOnly) { $buildArguments += '--native-only' }
if ($PrepareOnly) { $buildArguments += '--prepare-only' }
if ($PackageOnly) { $buildArguments += '--package-only' }
if ($SkipTests) { $buildArguments += '--skip-tests' }
if ($HeadlessTests) { $buildArguments += '--headless-tests' }
& $pythonCommand.Source @buildArguments
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
