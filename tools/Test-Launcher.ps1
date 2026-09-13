<#
.SYNOPSIS
    端到端验证启动器：用一次性夹具仓库跑两个阶段，并验证关闭窗口时进程树被回收。

.DESCRIPTION
    夹具完全位于 deepseek-harness-launcher\test\.run 下，包含：
      remote.git   本地裸仓库，充当远端
      seed         用于提交并推送 tag 的工作副本
      work         启动器的目标仓库（相当于 deepseek-harness）
      stub-*.js    代替真实构建/服务的桩程序，会写入标记文件与 pid 文件

    阶段 A：远端有本地没有的新 tag
            预期：提示并更新到 dsh-v0.1.1（分离头指针）→ pnpm run build → pnpm dsh web
    阶段 B：再次启动，此时已是最新且已构建过
            预期：跳过 pnpm run build，直接 pnpm dsh web

    测试不访问网络，也不会读写 deepseek-harness 源码目录；也不会关闭其它
    已在运行的启动器实例（以 --allow-multiple 并存）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-Launcher.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-Launcher.ps1 -KeepFixture
#>
[CmdletBinding()]
param(
    [int]$TimeoutSeconds = 150,
    [switch]$KeepFixture
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($root)) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = Split-Path -Parent $root
$exe = Join-Path $root 'dist\DeepSeekHarnessLauncher.exe'
$run = Join-Path $root 'test\.run'
$results = New-Object System.Collections.Generic.List[object]

function Write-Text {
    param([string]$Path, [string]$Content)
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function Assert-True {
    param([string]$Name, [bool]$Condition, [string]$Detail = '')
    $results.Add([pscustomobject]@{ Name = $Name; Passed = $Condition; Detail = $Detail })
    if ($Condition) {
        Write-Host ("  [通过] {0}" -f $Name) -ForegroundColor Green
    }
    else {
        Write-Host ("  [失败] {0} {1}" -f $Name, $Detail) -ForegroundColor Red
    }
}

function Invoke-Git {
    param([string[]]$GitArguments)
    # git 的换行符警告走 stderr；在 Stop 偏好下会被当成终止错误，这里局部放开。
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & git @GitArguments 2>&1
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($code -ne 0) {
        throw ("git {0} 失败：{1}" -f ($GitArguments -join ' '), ($output -join ' / '))
    }
    return $output
}

function Get-FreePort {
    $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    return $port
}

function Write-FixtureConfig {
    param([int]$Port, [string]$RemoteUrl, [string]$Path, [string]$OpenTarget = 'browser', [string]$AppCommand = '', [string]$AppWindowTitle = '', [string]$HarnessDir = 'work')
    $ini = @"
# 端到端测试夹具配置（由 tools\Test-Launcher.ps1 生成）
harnessDir = $HarnessDir
remoteName = origin
remoteUrl = $RemoteUrl
tagPrefix = dsh-v
port = $Port
checkUpdates = true
installAfterUpdate = false
buildWhenUpToDate = false
buildWhenCheckFailed = true
gitSslFallback = true
closeStopsService = true
buildCommand = run build
webCommand = dsh web
webExtraArgs =
openTarget = $OpenTarget
appCommand = $AppCommand
closeAppOnExit = true
appWindowTitle = $AppWindowTitle
echoOutput = true
"@
    Write-Text $Path $ini
}

function Start-Launcher {
    param([string]$ConfigPath, [string]$HarnessPath = '')
    $arguments = @('--config', "`"$ConfigPath`"", '--yes', '--allow-multiple')
    if ($HarnessPath) {
        $arguments += @('--harness', "`"$HarnessPath`"")
    }

    return Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
}

function Wait-LauncherReady {
    param([System.Diagnostics.Process]$Process, [string]$LogPath, [string]$StubPidPath, [int]$Seconds)

    $deadline = (Get-Date).AddSeconds($Seconds)
    $state = @{ Running = $false; StubPid = $null; EarlyExit = $false; ExitCode = $null; Seconds = 0; LogText = '' }
    $lastReport = 0
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        if (Test-Path $LogPath) {
            $text = Get-Content -LiteralPath $LogPath -Raw -Encoding UTF8
            if ($text -match 'Web 服务已就绪：') { $state.Running = $true }
            if ($text -match '构建失败|更新到 .* 失败|无法启动') { break }
        }
        if (Test-Path $StubPidPath) { $state.StubPid = (Get-Content -LiteralPath $StubPidPath -Raw).Trim() }
        if ($state.Running -and $state.StubPid) { break }

        $Process.Refresh()
        if ($Process.HasExited) {
            $state.EarlyExit = $true
            $state.ExitCode = $Process.ExitCode
            break
        }

        $elapsed = [int]((Get-Date) - $deadline.AddSeconds(-$Seconds)).TotalSeconds
        if ($elapsed - $lastReport -ge 15) {
            $lastReport = $elapsed
            $tail = if (Test-Path $LogPath) { (Get-Content -LiteralPath $LogPath -Encoding UTF8 -Tail 1) } else { '(无日志)' }
            Write-Host ("  … {0}s；最后一行：{1}" -f $elapsed, $tail)
        }
    }

    $state.Seconds = [int]((Get-Date) - $deadline.AddSeconds(-$Seconds)).TotalSeconds
    $state.LogText = if (Test-Path $LogPath) { Get-Content -LiteralPath $LogPath -Raw -Encoding UTF8 } else { '' }
    return $state
}

function Stop-Launcher {
    param([System.Diagnostics.Process]$Process)
    $Process.Refresh()
    if ($Process.HasExited) { return $true }
    $sent = $Process.CloseMainWindow()
    if (-not $sent) { return $false }
    return $Process.WaitForExit(20000)
}

# ---------- 准备 ----------

if (-not (Test-Path $exe)) {
    Write-Host '未找到可执行文件，先执行 build.ps1 …'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw '编译失败' }
}

$otherInstances = @(Get-Process -Name 'DeepSeekHarnessLauncher' -ErrorAction SilentlyContinue)
if ($otherInstances.Count -gt 0) {
    Write-Host ("提示：检测到 {0} 个已在运行的启动器实例，测试与之并存，不会关闭它们。" -f $otherInstances.Count) -ForegroundColor Yellow
}

if (Test-Path $run) { Remove-Item -LiteralPath $run -Recurse -Force }
New-Item -ItemType Directory -Force -Path $run | Out-Null

$remote = Join-Path $run 'remote.git'
$seed = Join-Path $run 'seed'
$work = Join-Path $run 'work'
$configPath = Join-Path $run 'fixture.config.ini'
$logPath = Join-Path $run 'logs\launcher.log'
$stubPidPath = Join-Path $work 'web-stub.pid'
$stampPath = Join-Path $work 'build-stamp.txt'
$recordPath = Join-Path $run 'state\last-build.ini'

# 远端一律用 file:/// URL：Windows 路径里的 "D:" 会被 git 当成远端主机名而绕道 sh.exe。
$remoteUrl = 'file:///' + ($remote -replace '\\', '/')

Write-Host '=== 1. 构建夹具仓库 ==='
Invoke-Git @('init', '--bare', '-b', 'master', $remote) | Out-Null
Invoke-Git @('init', '-b', 'master', $seed) | Out-Null

$packageJson = @'
{
  "name": "dsh-launcher-fixture",
  "private": true,
  "version": "0.1.0",
  "scripts": {
    "build": "node stub-build.js",
    "dsh": "node stub-web.js"
  }
}
'@
Write-Text (Join-Path $seed 'package.json') $packageJson

$stubBuild = @'
const fs = require('node:fs')
const path = require('node:path')
fs.writeFileSync(path.join(__dirname, 'build-stamp.txt'), new Date().toISOString())
console.log('stub build: 完成（夹具构建脚本）')
'@
Write-Text (Join-Path $seed 'stub-build.js') $stubBuild

$stubWeb = @'
const fs = require('node:fs')
const path = require('node:path')
const args = process.argv.slice(2)
let port = 3199
const index = args.indexOf('--port')
if (index >= 0 && args[index + 1]) { port = Number(args[index + 1]) }
fs.writeFileSync(path.join(__dirname, 'web-stub.pid'), String(process.pid))
console.log('dsh web: opening the default browser; pass --no-open to disable')
console.log('dsh web: 夹具服务已就绪 http://127.0.0.1:' + port + '/?token=stub-token-123456')
setTimeout(function () { process.exit(0) }, 600000)
'@
Write-Text (Join-Path $seed 'stub-web.js') $stubWeb

# 界面应用桩：记录收到的命令行（验证 {url} 注入的是带 token 的地址），
# 并打开一个真实的窗口（验证启动器退出时会关掉应用窗口）。
$stubOpen = @'
const fs = require('node:fs')
const path = require('node:path')
const { spawn } = require('node:child_process')
fs.writeFileSync(path.join(__dirname, 'open-target.txt'), process.argv.slice(2).join(' '))
// stub-window.exe 放在被测仓库之外，避免往仓库里塞可执行文件。
const child = spawn(path.join(__dirname, '..', 'stub-window.exe'), ['DSH-Launcher-Test 窗口'], {
  detached: true,
  stdio: 'ignore',
})
fs.writeFileSync(path.join(__dirname, 'open-window.pid'), String(child.pid))
child.unref()
'@
Write-Text (Join-Path $seed 'stub-open.js') $stubOpen
Write-Text (Join-Path $seed '.gitignore') "build-stamp.txt`nweb-stub.pid`nopen-target.txt`nopen-window.pid`n"

Invoke-Git @('-C', $seed, 'add', '-A') | Out-Null
Invoke-Git @('-C', $seed, '-c', 'user.name=Launcher Test', '-c', 'user.email=test@example.invalid', 'commit', '-m', 'fixture v1') | Out-Null
Invoke-Git @('-C', $seed, 'tag', 'dsh-v0.1.0') | Out-Null
Invoke-Git @('-C', $seed, 'remote', 'add', 'origin', $remoteUrl) | Out-Null
Invoke-Git @('-C', $seed, 'push', 'origin', 'master', '--tags') | Out-Null

# work 只拿到 v0.1.0，因此远端随后的 v0.1.1 对它是“新 tag”
Invoke-Git @('clone', $remoteUrl, $work) | Out-Null
Invoke-Git @('-C', $seed, 'commit', '--allow-empty', '-m', 'fixture v2') | Out-Null
Invoke-Git @('-C', $seed, 'tag', 'dsh-v0.1.1') | Out-Null
Invoke-Git @('-C', $seed, 'push', 'origin', 'master', '--tags') | Out-Null

$beforeTag = (Invoke-Git @('-C', $work, 'describe', '--tags', '--abbrev=0')).Trim()
Write-Host ("  work 当前 tag：{0}；远端已有：dsh-v0.1.1" -f $beforeTag)

# 真实窗口桩：一个只显示窗口的 WinForms 程序，标题由参数给出，用于验证退出时关闭应用窗口。
$stubWindowCode = @'
using System;
using System.Windows.Forms;
internal static class StubWindow
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Form form = new Form();
        form.Text = args.Length > 0 ? args[0] : "stub-window";
        form.Width = 420;
        form.Height = 200;
        Application.Run(form);
    }
}
'@
$stubWindowSourcePath = Join-Path $run 'stub-window.cs'
$stubWindowExe = Join-Path $run 'stub-window.exe'
Write-Text $stubWindowSourcePath $stubWindowCode
$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw '未找到 csc.exe，无法编译窗口桩程序' }
& $csc /nologo /target:winexe /codepage:65001 "/out:$stubWindowExe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $stubWindowSourcePath | Out-Null
if ($LASTEXITCODE -ne 0) { throw "窗口桩程序编译失败（$LASTEXITCODE）" }
Write-Host ("  窗口桩程序：{0}" -f $stubWindowExe)

# ---------- 阶段 A：发现新 tag ----------

$portA = Get-FreePort
Write-FixtureConfig -Port $portA -RemoteUrl $remoteUrl -Path $configPath -OpenTarget 'browser' -AppCommand ''

Write-Host '=== 2. 阶段 A：无人值守启动（预期：更新到 dsh-v0.1.1 并构建后拉起服务）==='
$processA = Start-Launcher -ConfigPath $configPath
$resultA = Wait-LauncherReady -Process $processA -LogPath $logPath -StubPidPath $stubPidPath -Seconds $TimeoutSeconds

Write-Host '--- 阶段 A 断言 ---'
Assert-True '[A] 启动器未被意外关闭' (-not $resultA.EarlyExit) ("exit=" + $resultA.ExitCode)
Assert-True '[A] 日志出现“发现可用更新”' ($resultA.LogText -match '发现可用更新')
Assert-True '[A] 已更新到 dsh-v0.1.1' ($resultA.LogText -match '已更新到 dsh-v0.1.1')
Assert-True '[A] 执行了 pnpm run build' ($resultA.LogText -match '构建判定：执行 pnpm run build')
Assert-True '[A] 构建完成' ($resultA.LogText -match '构建完成')
Assert-True '[A] 服务已就绪并解析出带 token 地址' ($resultA.LogText -match ([regex]::Escape("http://127.0.0.1:$portA/?token=stub-token-123456")))

$afterTag = (Invoke-Git @('-C', $work, 'describe', '--tags', '--abbrev=0')).Trim()
$headBranch = (Invoke-Git @('-C', $work, 'rev-parse', '--abbrev-ref', 'HEAD')).Trim()
Assert-True '[A] work 已检出到 dsh-v0.1.1' ($afterTag -eq 'dsh-v0.1.1') ("实际：$afterTag")
Assert-True '[A] 检出为分离头指针（按 tag 检出）' ($headBranch -eq 'HEAD') ("实际：$headBranch")
Assert-True '[A] 构建标记文件已生成' (Test-Path $stampPath)
Assert-True '[A] 更新前状态已记录' (Test-Path (Join-Path $run 'state\pre-update-state.txt'))
Assert-True '[A] 构建成功已记录（用于下次跳过）' (Test-Path $recordPath)
$recordText = Get-Content -LiteralPath $recordPath -Raw -Encoding UTF8
$headCommit = (Invoke-Git @('-C', $work, 'rev-parse', 'HEAD')).Trim()
Assert-True '[A] 构建记录指向检出后的提交' ($recordText -match $headCommit) ("head=$headCommit record=$($recordText -replace "`r`n", ' | ')")

$stubAliveA = $null -ne (Get-Process -Id ([int]$resultA.StubPid) -ErrorAction SilentlyContinue)
Assert-True '[A] 夹具服务进程在运行' $stubAliveA ("pid=" + $resultA.StubPid)

Write-Host '--- 阶段 A 收尾：关闭窗口并回收进程树 ---'
$closedA = Stop-Launcher -Process $processA
Assert-True '[A] 关闭窗口后启动器退出' $closedA
if (-not $closedA) { $processA.Kill() }
Start-Sleep -Seconds 2
Assert-True '[A] 夹具服务被一并结束（taskkill /T）' ($null -eq (Get-Process -Id ([int]$resultA.StubPid) -ErrorAction SilentlyContinue))

# ---------- 阶段 B：已是最新，跳过构建 ----------

$stampBefore = (Get-Item $stampPath).LastWriteTimeUtc
Remove-Item -LiteralPath $stubPidPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
$portB = Get-FreePort
# 阶段 B 同时验证「界面打开方式 = app」：{url} 必须注入带 token 的地址，且 web 命令要带 --no-open。
$openTargetMarker = Join-Path $work 'open-target.txt'
$openWindowPid = Join-Path $work 'open-window.pid'
Remove-Item -LiteralPath $openTargetMarker -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $openWindowPid -Force -ErrorAction SilentlyContinue
$nodePath = (Get-Command node).Source
$openCommand = '"{0}" "{1}" {{url}}' -f $nodePath, (Join-Path $work 'stub-open.js')
Write-FixtureConfig -Port $portB -RemoteUrl $remoteUrl -Path $configPath -OpenTarget 'app' -AppCommand $openCommand -AppWindowTitle 'DSH-Launcher-Test'

Write-Host '=== 3. 阶段 B：再次启动（预期：跳过 pnpm run build，直接拉起服务并打开界面应用）==='
$processB = Start-Launcher -ConfigPath $configPath
$resultB = Wait-LauncherReady -Process $processB -LogPath $logPath -StubPidPath $stubPidPath -Seconds $TimeoutSeconds

Write-Host '--- 阶段 B 断言 ---'
Assert-True '[B] 启动器未被意外关闭' (-not $resultB.EarlyExit) ("exit=" + $resultB.ExitCode)
Assert-True '[B] 判定为已是最新' ($resultB.LogText -match '已是最新')
Assert-True '[B] 跳过 pnpm run build' ($resultB.LogText -match '构建判定：跳过 pnpm run build')
Assert-True '[B] 未出现“执行 pnpm run build”' (-not ($resultB.LogText -match '构建判定：执行 pnpm run build'))
Assert-True '[B] 服务已就绪并解析出带 token 地址' ($resultB.LogText -match ([regex]::Escape("http://127.0.0.1:$portB/?token=stub-token-123456")))
Assert-True '[B] web 命令带上 --no-open（由启动器负责开界面）' ($resultB.LogText -match 'pnpm dsh web --port \d+ --no-open')
Assert-True '[B] 日志记录了界面应用启动' ($resultB.LogText -match '已启动界面应用')

$openTarget = ''
$markerDeadline = (Get-Date).AddSeconds(15)
while ((Get-Date) -lt $markerDeadline -and $openTarget.Length -eq 0) {
    if (Test-Path $openTargetMarker) { $openTarget = (Get-Content -LiteralPath $openTargetMarker -Raw -Encoding UTF8).Trim() }
    if ($openTarget.Length -eq 0) { Start-Sleep -Milliseconds 500 }
}
Assert-True '[B] 界面应用被启动' ($openTarget.Length -gt 0) ("marker=" + $openTarget)
Assert-True '[B] 界面应用收到的是带 token 的地址' ($openTarget -match ([regex]::Escape("http://127.0.0.1:$portB/?token=stub-token-123456"))) ("收到：" + $openTarget)

# 等界面窗口出现并被启动器记录（后台轮询，最长约 20 秒）。
$windowPidB = $null
$windowDeadline = (Get-Date).AddSeconds(25)
while ((Get-Date) -lt $windowDeadline) {
    if (Test-Path $openWindowPid) { $windowPidB = (Get-Content -LiteralPath $openWindowPid -Raw).Trim() }
    $nowLog = if (Test-Path $logPath) { Get-Content -LiteralPath $logPath -Raw -Encoding UTF8 } else { '' }
    if ($windowPidB -and $nowLog -match '已记录界面应用窗口') { break }
    Start-Sleep -Milliseconds 500
}
$logBeforeClose = if (Test-Path $logPath) { Get-Content -LiteralPath $logPath -Raw -Encoding UTF8 } else { '' }
$windowAlive = $false
if ($windowPidB) {
    $windowAlive = $null -ne (Get-Process -Id ([int]$windowPidB) -ErrorAction SilentlyContinue)
}
Assert-True '[B] 界面应用窗口进程已启动' $windowAlive ("pid=" + $windowPidB)
Assert-True '[B] 启动器识别出界面应用窗口' ($logBeforeClose -match '已记录界面应用窗口')

$stampAfter = (Get-Item $stampPath).LastWriteTimeUtc
Assert-True '[B] 构建脚本确实没有再次执行（构建标记未变）' ($stampBefore -eq $stampAfter) ("before=$stampBefore after=$stampAfter")

$stubAliveB = $null -ne (Get-Process -Id ([int]$resultB.StubPid) -ErrorAction SilentlyContinue)
Assert-True '[B] 夹具服务进程在运行' $stubAliveB ("pid=" + $resultB.StubPid)
Assert-True '[B] 两次启动使用了不同的服务进程' ($resultA.StubPid -ne $resultB.StubPid) ("A=" + $resultA.StubPid + " B=" + $resultB.StubPid)

Write-Host '--- 阶段 B 收尾：关闭启动器，界面应用窗口应一并关闭 ---'
$closedB = Stop-Launcher -Process $processB
Assert-True '[B] 关闭窗口后启动器退出' $closedB
if (-not $closedB) { $processB.Kill() }
Start-Sleep -Seconds 3
Assert-True '[B] 夹具服务被一并结束（taskkill /T）' ($null -eq (Get-Process -Id ([int]$resultB.StubPid) -ErrorAction SilentlyContinue))
$windowGone = $true
if ($windowPidB) {
    $windowGone = $null -eq (Get-Process -Id ([int]$windowPidB) -ErrorAction SilentlyContinue)
}
Assert-True '[B] 启动器退出后界面应用窗口被关闭' $windowGone ("pid=" + $windowPidB)
$logAfterClose = if (Test-Path $logPath) { Get-Content -LiteralPath $logPath -Raw -Encoding UTF8 } else { '' }
Assert-True '[B] 日志记录了关闭界面应用窗口' ($logAfterClose -match '已请求关闭界面应用窗口')

# ---------- 阶段 C：用 --harness 指定另一个源码目录 ----------

Write-Host '=== 4. 阶段 C：配置里的目录无效，用 --harness 指向夹具仓库 ==='
Remove-Item -LiteralPath $stubPidPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
$portC = Get-FreePort
# 故意写一个不存在的 harnessDir：不传 --harness 时启动器应当失败。
Write-FixtureConfig -Port $portC -RemoteUrl $remoteUrl -Path $configPath -OpenTarget 'browser' -AppCommand '' -HarnessDir '..\does-not-exist'
$processC = Start-Launcher -ConfigPath $configPath -HarnessPath $work
$resultC = Wait-LauncherReady -Process $processC -LogPath $logPath -StubPidPath $stubPidPath -Seconds $TimeoutSeconds

Write-Host '--- 阶段 C 断言 ---'
Assert-True '[C] 启动器未被意外关闭' (-not $resultC.EarlyExit) ("exit=" + $resultC.ExitCode)
Assert-True '[C] 日志确认使用了 --harness 指定的目录' ($resultC.LogText -match ([regex]::Escape('--harness 指定源码目录：' + $work)))
Assert-True '[C] 实际使用的源码目录是夹具仓库' ($resultC.LogText -match ([regex]::Escape('源码目录：' + $work)))
Assert-True '[C] 配置里的无效目录被忽略（未报目录不存在）' (-not ($resultC.LogText -match '源码目录不存在'))
Assert-True '[C] 服务已在夹具仓库上就绪' ($resultC.LogText -match ([regex]::Escape("http://127.0.0.1:$portC/?token=stub-token-123456")))

Write-Host '--- 阶段 C 收尾 ---'
$closedC = Stop-Launcher -Process $processC
Assert-True '[C] 关闭窗口后启动器退出' $closedC
if (-not $closedC) { $processC.Kill() }
Start-Sleep -Seconds 2
Assert-True '[C] 夹具服务被一并结束' ($null -eq (Get-Process -Id ([int]$resultC.StubPid) -ErrorAction SilentlyContinue))

if (-not $KeepFixture) {
    Remove-Item -LiteralPath $run -Recurse -Force -ErrorAction SilentlyContinue
}

# ---------- 汇总 ----------

$failed = @($results | Where-Object { -not $_.Passed })
Write-Host ''
Write-Host ("=== 结果：{0}/{1} 项通过 ===" -f ($results.Count - $failed.Count), $results.Count)
if ($failed.Count -gt 0) {
    foreach ($item in $failed) { Write-Host ("  失败：{0} {1}" -f $item.Name, $item.Detail) -ForegroundColor Red }
    exit 1
}

Write-Host '端到端验证通过。' -ForegroundColor Green
exit 0
