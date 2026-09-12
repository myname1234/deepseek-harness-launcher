<#
.SYNOPSIS
    移除 DeepSeek Harness 启动器的快捷方式。

.DESCRIPTION
    删除桌面（以及可选开始菜单）中的“DeepSeek Harness.lnk”。
    默认保留编译产物、配置、日志，便于再次安装。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File uninstall.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File uninstall.ps1 -StartMenu -RemoveBuildOutput
#>
[CmdletBinding()]
param(
    [switch]$StartMenu,
    [switch]$RemoveBuildOutput,
    [string]$ShortcutName = 'DeepSeek Harness'
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($root)) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }

$shell = New-Object -ComObject WScript.Shell
$targets = New-Object System.Collections.Generic.List[string]

$desktop = $shell.SpecialFolders.Item('Desktop')
if (-not $desktop) { $desktop = [Environment]::GetFolderPath('Desktop') }
$targets.Add((Join-Path $desktop "$ShortcutName.lnk"))

if ($StartMenu) {
    $programs = $shell.SpecialFolders.Item('Programs')
    if (-not $programs) { $programs = [Environment]::GetFolderPath('Programs') }
    $targets.Add((Join-Path $programs "$ShortcutName.lnk"))
}

foreach ($target in $targets) {
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Force
        Write-Host ("已删除：{0}" -f $target)
    }
    else {
        Write-Host ("不存在，跳过：{0}" -f $target)
    }
}

if ($RemoveBuildOutput) {
    $dist = Join-Path $root 'dist'
    if (Test-Path $dist) {
        Remove-Item -LiteralPath $dist -Recurse -Force
        Write-Host ("已删除编译产物：{0}" -f $dist)
    }
}

Write-Host '卸载完成。'
