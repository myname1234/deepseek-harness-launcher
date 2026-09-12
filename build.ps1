<#
.SYNOPSIS
    编译 DeepSeek Harness 启动器。

.DESCRIPTION
    使用 .NET Framework 自带的 csc.exe（C# 5）把 src\*.cs 编译成
    dist\DeepSeekHarnessLauncher.exe，并嵌入 assets\dsh-launcher.ico 作为程序图标。
    图标缺失时先调用 tools\New-LauncherIcon.ps1 生成。
    本脚本只读写 deepseek-harness-launcher 目录，不接触源码仓库。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build.ps1
#>
[CmdletBinding()]
param(
    [switch]$RebuildIcon
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($root)) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
$srcDir = Join-Path $root 'src'
$distDir = Join-Path $root 'dist'
$assetsDir = Join-Path $root 'assets'
$iconPath = Join-Path $assetsDir 'dsh-launcher.ico'
$outExe = Join-Path $distDir 'DeepSeekHarnessLauncher.exe'

function Find-Csc {
    $candidates = New-Object System.Collections.Generic.List[string]
    $candidates.Add((Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'))
    $candidates.Add((Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'))
    foreach ($base in @("$env:ProgramFiles\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio")) {
        if (Test-Path $base) {
            Get-ChildItem -Path $base -Filter 'csc.exe' -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\\Roslyn\\' } |
                Sort-Object FullName -Descending |
                ForEach-Object { $candidates.Add($_.FullName) }
        }
    }
    foreach ($path in $candidates) {
        if ($path -and (Test-Path $path)) { return $path }
    }
    return $null
}

$csc = Find-Csc
if (-not $csc) {
    throw '未找到 C# 编译器 csc.exe。请安装 .NET Framework 4.x（Windows 10/11 默认自带），或安装 Visual Studio 的 Roslyn 编译器。'
}

if ($RebuildIcon -or -not (Test-Path $iconPath)) {
    Write-Host '生成图标 assets\dsh-launcher.ico …'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\New-LauncherIcon.ps1')
    if ($LASTEXITCODE -ne 0) { throw "图标生成失败（退出码 $LASTEXITCODE）" }
}

if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Force -Path $distDir | Out-Null }

$sources = @(Get-ChildItem -Path $srcDir -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "src 目录下没有 C# 源文件：$srcDir" }

$compilerArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/codepage:65001'
    '/langversion:5'
    '/warn:4'
    "/out:$outExe"
    "/win32icon:$iconPath"
    '/reference:System.dll'
    '/reference:System.Core.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
) + $sources

Write-Host ("编译器：{0}" -f $csc)
Write-Host ("源文件：{0} 个" -f $sources.Count)

& $csc @compilerArgs
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    throw "编译失败（csc 退出码 $exitCode）"
}

$info = Get-Item $outExe
Write-Host ("编译完成：{0}（{1:N0} 字节）" -f $info.FullName, $info.Length)
