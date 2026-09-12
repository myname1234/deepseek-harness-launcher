<#
.SYNOPSIS
    安装 DeepSeek Harness 启动器：编译可执行文件并创建桌面快捷方式。

.DESCRIPTION
    默认行为：
      1. 若 dist\DeepSeekHarnessLauncher.exe 不存在（或指定 -Rebuild）则先执行 build.ps1；
      2. 在桌面创建“DeepSeek Harness.lnk”，指向该可执行文件并带上图标。
    使用 -StartMenu 时同时在开始菜单创建快捷方式。
    本脚本只写启动器目录与快捷方式，不改动 deepseek-harness 源码目录。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File install.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File install.ps1 -StartMenu -Rebuild
#>
[CmdletBinding()]
param(
    [switch]$Rebuild,
    [switch]$StartMenu,
    [string]$ShortcutName = 'DeepSeek Harness'
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($root)) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
$exe = Join-Path $root 'dist\DeepSeekHarnessLauncher.exe'
$icon = Join-Path $root 'assets\dsh-launcher.ico'
$config = Join-Path $root 'launcher.config.ini'

if ($Rebuild -or -not (Test-Path $exe)) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）" }
}

if (-not (Test-Path $exe)) { throw "未找到可执行文件：$exe" }
if (-not (Test-Path $config)) { Write-Warning "未找到配置文件 $config，启动器将使用内置默认值。" }

$shell = New-Object -ComObject WScript.Shell
$created = New-Object System.Collections.Generic.List[string]

function New-LauncherShortcut {
    param([string]$LinkPath)

    $existing = Get-Item -LiteralPath $LinkPath -ErrorAction SilentlyContinue
    if ($existing) { Remove-Item -LiteralPath $LinkPath -Force }

    $shortcut = $shell.CreateShortcut($LinkPath)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $root
    $shortcut.Description = '启动 DeepSeek Harness（检查 tag、构建并拉起 Web 服务）'
    if (Test-Path $icon) { $shortcut.IconLocation = "$icon,0" }
    $shortcut.WindowStyle = 1
    $shortcut.Save()
}

$desktop = $shell.SpecialFolders.Item('Desktop')
if (-not $desktop) { $desktop = [Environment]::GetFolderPath('Desktop') }
$desktopLink = Join-Path $desktop "$ShortcutName.lnk"
New-LauncherShortcut -LinkPath $desktopLink
$created.Add($desktopLink)

if ($StartMenu) {
    $programs = $shell.SpecialFolders.Item('Programs')
    if (-not $programs) { $programs = [Environment]::GetFolderPath('Programs') }
    $startLink = Join-Path $programs "$ShortcutName.lnk"
    New-LauncherShortcut -LinkPath $startLink
    $created.Add($startLink)
}

Write-Host ''
Write-Host 'DeepSeek Harness 启动器安装完成：'
Write-Host ("  可执行文件：{0}" -f $exe)
Write-Host ("  配置文件  ：{0}" -f $config)
foreach ($link in $created) {
    Write-Host ("  快捷方式  ：{0}" -f $link)
}
Write-Host ''
Write-Host '双击桌面图标即可启动；启动器会自动检查远端 tag、按需更新、构建并拉起 Web 服务。'
Write-Host '如需卸载快捷方式：powershell -ExecutionPolicy Bypass -File uninstall.ps1'
