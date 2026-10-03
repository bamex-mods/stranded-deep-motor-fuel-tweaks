param(
    [Parameter(Mandatory=$true)]
    [string]$GameRoot,

    [switch]$Deploy
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $GameRoot -PathType Container)) {
    throw "Stranded Deep game directory not found: $GameRoot"
}

$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path

$Managed = Join-Path $GameRoot "Stranded_Deep_Data\Managed"
$BepInExCore = Join-Path $GameRoot "BepInEx\core"

$CompilerCandidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)

$Compiler = $CompilerCandidates |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($Compiler)) {
    throw ".NET Framework C# compiler not found."
}

$Source = Join-Path $PSScriptRoot "MotorFuelTweaks.cs"
$ModSettingsClient = Join-Path $PSScriptRoot "SDK\ModSettingsClient.cs"

foreach ($Required in @(
    $Source,
    $ModSettingsClient,
    $Managed,
    $BepInExCore
)) {
    if (-not (Test-Path -LiteralPath $Required)) {
        throw "Required build input missing: $Required"
    }
}

$BuildDir = Join-Path $PSScriptRoot "build"
New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null

$Output = Join-Path $BuildDir "StrandedDeepMotorFuelTweaks.dll"

$References = New-Object System.Collections.Generic.List[string]

foreach ($Required in @(
    (Join-Path $BepInExCore "BepInEx.dll"),
    (Join-Path $Managed "Assembly-CSharp.dll"),
    (Join-Path $Managed "bolt.dll"),
    (Join-Path $Managed "bolt.user.dll"),
    (Join-Path $Managed "Rewired_Core.dll")
)) {
    if (-not (Test-Path -LiteralPath $Required -PathType Leaf)) {
        throw "Required assembly not found: $Required"
    }

    $References.Add($Required)
}

Get-ChildItem -LiteralPath $Managed -Filter "UnityEngine*.dll" -File |
    Sort-Object Name |
    ForEach-Object {
        $References.Add($_.FullName)
    }

$Args = New-Object System.Collections.Generic.List[string]
$Args.Add("/nologo")
$Args.Add("/target:library")
$Args.Add("/optimize+")
$Args.Add("/langversion:5")
$Args.Add("/out:$Output")

foreach ($Reference in $References) {
    $Args.Add("/reference:$Reference")
}

$Args.Add($Source)
$Args.Add($ModSettingsClient)

Write-Host "Compiler:   $Compiler"
Write-Host "Output:     $Output"
Write-Host "References: $($References.Count)"
Write-Host ""

& $Compiler $Args.ToArray()

if ($LASTEXITCODE -ne 0) {
    throw "csc.exe failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $Output -PathType Leaf)) {
    throw "Build reported success but DLL was not created: $Output"
}

Write-Host ""
Write-Host "BUILD OK"
Write-Host $Output

if ($Deploy) {
    $PluginDir = Join-Path $GameRoot "BepInEx\plugins\StrandedDeepMotorFuelTweaks"
    New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null

    Copy-Item `
        -LiteralPath $Output `
        -Destination (Join-Path $PluginDir "StrandedDeepMotorFuelTweaks.dll") `
        -Force

    $RuntimePdb = Join-Path $PluginDir "StrandedDeepMotorFuelTweaks.pdb"

    if (Test-Path -LiteralPath $RuntimePdb -PathType Leaf) {
        Remove-Item -LiteralPath $RuntimePdb -Force
    }

    Write-Host ""
    Write-Host "DEPLOY OK"
    Write-Host $PluginDir
}
