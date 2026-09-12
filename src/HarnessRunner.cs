using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

namespace DshLauncher
{
    /// <summary>启动流程所处的阶段。</summary>
    internal enum LauncherState
    {
        Idle,
        Checking,
        WaitingUser,
        Updating,
        Installing,
        Building,
        Starting,
        Running,
        Stopped,
        Failed,
    }

    /// <summary>工作区存在未提交修改时用户的选择。</summary>
    internal enum DirtyChoice
    {
        Cancel,
        SkipUpdate,
        StashAndUpdate,
    }

    /// <summary>
    /// 启动流程：
    /// 环境检查 → 远端 tag 比较 → （可选）提示并更新 → pnpm install → pnpm run build → pnpm dsh web。
    /// 全部步骤在后台线程串行执行，界面线程只负责展示与弹窗。
    /// </summary>
    internal sealed class HarnessRunner
    {
        private static readonly Regex UrlPattern = new Regex(
            @"https?://127\.0\.0\.1:\d+/\?token=[A-Za-z0-9._~\-]+",
            RegexOptions.Compiled);

        private readonly AppConfig _config;
        private readonly Log _log;
        private CancellationTokenSource _cancellation;
        private Thread _worker;
        private string _url;
        private readonly object _appWindowGate = new object();
        private IntPtr _appWindow;
        private string _appWindowTitle;

        internal HarnessRunner(AppConfig config, Log log)
        {
            _config = config;
            _log = log;
        }

        internal event Action<LauncherState, string> StateChanged;

        internal event Action<string> UrlDetected;

        /// <summary>提示用户是否更新到远端最新 tag，返回是否更新。</summary>
        internal Func<UpdateDecision, bool> ConfirmUpdate;

        /// <summary>工作区有未提交修改时的选择。</summary>
        internal Func<string, int, DirtyChoice> ConfirmDirty;

        /// <summary>端口被占用时询问是否改用建议端口。</summary>
        internal Func<int, int, bool> ConfirmPortBusy;

        /// <summary>本次运行是否强制构建（--force-build）。</summary>
        internal bool ForceBuild;

        internal string CurrentUrl
        {
            get { return _url; }
        }

        internal bool IsBusy
        {
            get
            {
                Thread worker = _worker;
                return worker != null && worker.IsAlive;
            }
        }

        /// <summary>在后台线程启动一次完整流程；正在运行时忽略。</summary>
        internal void Start()
        {
            if (IsBusy)
            {
                return;
            }

            _url = null;
            _cancellation = new CancellationTokenSource();
            _worker = new Thread(Pipeline);
            _worker.IsBackground = true;
            _worker.Name = "dsh-launcher-pipeline";
            _worker.Start();
        }

        /// <summary>请求停止：取消令牌会让 ProcessRunner 结束整棵进程树。</summary>
        internal void Stop()
        {
            CancellationTokenSource cancellation = _cancellation;
            if (cancellation != null)
            {
                try
                {
                    cancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // 流程恰好结束并释放了令牌，无需处理。
                }
            }
        }

        /// <summary>等待后台流程结束，供退出前收敛使用。</summary>
        internal bool WaitForExit(int milliseconds)
        {
            Thread worker = _worker;
            if (worker == null)
            {
                return true;
            }

            return worker.Join(milliseconds);
        }

        private CancellationToken Token
        {
            get
            {
                CancellationTokenSource cancellation = _cancellation;
                return cancellation == null ? CancellationToken.None : cancellation.Token;
            }
        }

        private void SetState(LauncherState state, string detail)
        {
            Action<LauncherState, string> handler = StateChanged;
            if (handler != null)
            {
                handler(state, detail);
            }
        }

        private void Pipeline()
        {
            try
            {
                SetState(LauncherState.Checking, "正在检查运行环境…");
                if (!Preflight())
                {
                    SetState(LauncherState.Failed, "环境检查未通过");
                    return;
                }

                GitRepository repository = new GitRepository(_config, _log, Token);
                RepoState state = repository.ReadState();
                ReportRepository(state);

                UpdateDecision decision = null;
                if (_config.CheckUpdates)
                {
                    if (!state.IsRepository)
                    {
                        _log.Warn("目标目录不是 git 仓库，跳过 tag 检查。");
                    }
                    else
                    {
                        SetState(LauncherState.Checking, "正在查询远端 tag…");
                        decision = CheckForUpdates(repository, state);
                        if (decision != null && decision.Available)
                        {
                            bool updated;
                            if (!MaybeUpdate(repository, state, decision, out updated))
                            {
                                return;
                            }

                            if (updated)
                            {
                                // 更新改变了当前提交，必须重新读取：构建记录要写在检出的那个提交上。
                                state = repository.ReadState(false);
                                _log.Info("更新后当前提交：" + ShortCommit(state.HeadCommit)
                                    + "  当前 tag：" + (state.CurrentTag == null ? "(不在 tag 上)" : state.CurrentTag));
                            }
                        }
                    }
                }
                else
                {
                    _log.Info("配置项 checkUpdates = false，跳过 tag 检查。");
                }

                if (Token.IsCancellationRequested)
                {
                    SetState(LauncherState.Stopped, "已取消");
                    return;
                }

                BuildVerdict verdict = DecideBuild(state, decision);
                bool buildSucceeded = false;
                if (verdict.Build)
                {
                    _log.Info("构建判定：执行 pnpm run build（" + verdict.Reason + "）。");
                    if (!Build(state))
                    {
                        return;
                    }

                    buildSucceeded = true;
                }
                else
                {
                    _log.Success("构建判定：跳过 pnpm run build，直接启动 Web 服务（" + verdict.Reason + "）。");
                }

                int port = ResolvePort();
                if (port < 0)
                {
                    SetState(LauncherState.Failed, "未选择可用端口，已取消启动");
                    return;
                }

                RunWeb(port, !buildSucceeded);
            }
            catch (Exception ex)
            {
                _log.Error("启动流程出现未预期错误：" + ex);
                SetState(LauncherState.Failed, "内部错误：" + ex.Message);
            }
        }

        /// <summary>结合仓库状态、更新判定与已有构建产物决定是否构建。</summary>
        private BuildVerdict DecideBuild(RepoState state, UpdateDecision decision)
        {
            bool force = ForceBuild || _config.BuildWhenUpToDate;
            bool artifactsCurrent = false;
            string artifactsDetail = null;
            if (!force && state != null && state.IsRepository && !state.IsDirty
                && (decision == null || !decision.Available))
            {
                artifactsCurrent = BuildState.ArtifactsCurrent(_config, state, out artifactsDetail);
            }

            return BuildPolicy.Decide(
                force,
                state,
                decision,
                _config.BuildWhenCheckFailed,
                artifactsCurrent,
                artifactsDetail);
        }

        /// <summary>检查目录与外部工具；缺失时给出可操作的提示。</summary>
        private bool Preflight()
        {
            bool ok = true;

            if (!System.IO.Directory.Exists(_config.HarnessDir))
            {
                _log.Error("源码目录不存在：" + _config.HarnessDir);
                _log.Error("请修改 " + AppConfig.FileName + " 中的 harnessDir。");
                return false;
            }

            _log.Info("源码目录：" + _config.HarnessDir);

            ok &= RequireTool("git", "需要安装 Git for Windows");
            ok &= RequireTool("node", "需要 Node.js（^22.19 或 >=24）");
            ok &= RequireTool("pnpm", "需要 pnpm（npm i -g pnpm）");

            for (int i = 0; i < _config.Warnings.Count; i++)
            {
                _log.Warn(_config.Warnings[i]);
            }

            return ok;
        }

        private bool RequireTool(string name, string hint)
        {
            List<string> found = ToolLocator.Find(name);
            if (found.Count == 0)
            {
                _log.Error("未找到命令 " + name + "：" + hint);
                return false;
            }

            _log.Info(string.Format(CultureInfo.InvariantCulture, "{0} → {1}", name, string.Join(" | ", found.ToArray())));
            return true;
        }

        private void ReportRepository(RepoState state)
        {
            if (!state.IsRepository)
            {
                return;
            }

            _log.Info("当前分支：" + (state.Branch == null ? "(未知)" : state.Branch)
                + "  提交：" + ShortCommit(state.HeadCommit));
            _log.Info("当前 tag：" + (state.CurrentTag == null ? "(不在 tag 上)" : state.CurrentTag)
                + "  本地版本 tag 数：" + state.LocalTags.Count.ToString(CultureInfo.InvariantCulture));
            if (state.IsDirty)
            {
                _log.Warn("工作区有 " + state.DirtyCount.ToString(CultureInfo.InvariantCulture) + " 项未提交修改。");
            }
        }

        private UpdateDecision CheckForUpdates(GitRepository repository, RepoState state)
        {
            string error;
            List<RemoteTag> remoteTags = repository.ListRemoteTags(out error);
            UpdateDecision decision = UpdatePolicy.Evaluate(state, remoteTags, _config.TagPrefix, error);

            if (!decision.CheckPerformed)
            {
                _log.Warn("无法检查远端 tag：" + (decision.CheckError == null ? decision.Reason : decision.CheckError));
                _log.Warn("将跳过更新检查，直接使用本地代码构建。");
                return decision;
            }

            _log.Info("远端 tag 数：" + decision.RemoteTagCount.ToString(CultureInfo.InvariantCulture)
                + "  远端最新：" + (decision.LatestRemote == null ? "(无)" : decision.LatestRemote.Name)
                + "  本地最新：" + (decision.LatestLocalTag == null ? "(无)" : decision.LatestLocalTag));

            if (decision.Available)
            {
                _log.Warn("发现可用更新：" + decision.Reason);
            }
            else
            {
                _log.Success("已是最新：" + decision.Reason);
            }

            return decision;
        }

        /// <summary>
        /// 提示并执行更新；返回 false 表示流程应当终止（用户取消或更新失败）。
        /// <paramref name="updated"/> 表示是否真的把工作区切换到了目标 tag。
        /// </summary>
        private bool MaybeUpdate(GitRepository repository, RepoState state, UpdateDecision decision, out bool updated)
        {
            updated = false;
            SetState(LauncherState.WaitingUser, "等待确认是否更新到 " + decision.LatestRemote.Name + "…");

            Func<UpdateDecision, bool> confirm = ConfirmUpdate;
            bool update = confirm != null && confirm(decision);
            if (!update)
            {
                _log.Info("已选择不更新，使用当前代码继续。");
                return true;
            }

            if (state.IsDirty)
            {
                Func<string, int, DirtyChoice> dirtyConfirm = ConfirmDirty;
                DirtyChoice choice = dirtyConfirm == null
                    ? DirtyChoice.Cancel
                    : dirtyConfirm(decision.LatestRemote.Name, state.DirtyCount);

                if (choice == DirtyChoice.Cancel)
                {
                    _log.Warn("工作区有未提交修改，已取消启动。");
                    SetState(LauncherState.Stopped, "已取消");
                    return false;
                }

                if (choice == DirtyChoice.SkipUpdate)
                {
                    _log.Warn("已选择保留本地修改并跳过更新。");
                    return true;
                }

                string message = "dsh-launcher 自动暂存 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                if (!repository.StashLocalChanges(message))
                {
                    SetState(LauncherState.Failed, "暂存本地修改失败，已取消更新");
                    return false;
                }

                _log.Success("本地修改已暂存，可用 git stash pop 恢复。");
            }

            repository.SavePreUpdateState(state, decision);

            SetState(LauncherState.Updating, "正在获取并检出 " + decision.LatestRemote.Name + "…");
            if (!repository.FetchAndCheckout(decision.LatestRemote.Name))
            {
                SetState(LauncherState.Failed, "更新到 " + decision.LatestRemote.Name + " 失败");
                return false;
            }

            _log.Success("已更新到 " + decision.LatestRemote.Name + "（分离头指针状态）。");
            updated = true;

            if (_config.InstallAfterUpdate)
            {
                SetState(LauncherState.Installing, "正在安装依赖（pnpm install --frozen-lockfile）…");
                ProcessResult install = RunCommand(_config.InstallCommandLine(), 0, null);
                if (install.Canceled)
                {
                    SetState(LauncherState.Stopped, "已取消");
                    return false;
                }

                if (!install.Started || install.ExitCode != 0)
                {
                    _log.Warn("依赖安装失败或未完成，继续执行构建；如构建失败请手动执行 pnpm install。");
                    LogTail(install, 20);
                }
                else
                {
                    _log.Success("依赖安装完成。");
                }
            }

            return true;
        }

        private bool Build(RepoState state)
        {
            SetState(LauncherState.Building, "正在构建：" + _config.BuildCommandLine());
            ProcessResult build = RunCommand(_config.BuildCommandLine(), 0, null);

            if (build.Canceled)
            {
                SetState(LauncherState.Stopped, "已取消构建");
                return false;
            }

            if (!build.Started)
            {
                _log.Error("构建命令无法启动：" + build.StartError);
                SetState(LauncherState.Failed, "构建命令无法启动");
                return false;
            }

            if (build.ExitCode != 0)
            {
                _log.Error(string.Format(
                    CultureInfo.InvariantCulture,
                    "构建失败，退出码 {0}，耗时 {1:0.0}s。",
                    build.ExitCode,
                    build.Duration.TotalSeconds));
                LogTail(build, 40);
                SetState(LauncherState.Failed, "构建失败（退出码 " + build.ExitCode.ToString(CultureInfo.InvariantCulture) + "）");
                return false;
            }

            _log.Success(string.Format(CultureInfo.InvariantCulture, "构建完成，耗时 {0:0.0}s。", build.Duration.TotalSeconds));
            // 记录本次构建，作为下次启动能否跳过构建的依据。
            BuildState.WriteRecord(_config, state, _config.BuildCommandLine());
            return true;
        }

        private int ResolvePort()
        {
            int configured = _config.Port;
            if (configured <= 0)
            {
                return 0;
            }

            if (!NetUtil.IsPortListening(configured))
            {
                return configured;
            }

            int suggested = NetUtil.FindFreePort(configured + 1);
            _log.Warn("端口 " + configured.ToString(CultureInfo.InvariantCulture) + " 已被占用。");

            Func<int, int, bool> confirm = ConfirmPortBusy;
            if (suggested > 0 && confirm != null && confirm(configured, suggested))
            {
                _log.Info("改用端口 " + suggested.ToString(CultureInfo.InvariantCulture) + "。");
                return suggested;
            }

            return -1;
        }

        /// <summary>
        /// 启动 Web 服务。若本次跳过了构建而 dsh 又报告客户端产物与源码不一致，
        /// 则补做一次完整构建并重试一次，避免“跳过构建”把启动变成失败。
        /// </summary>
        private void RunWeb(int port, bool allowBuildRecovery)
        {
            string commandLine = _config.WebCommandLine(port);
            SetState(LauncherState.Starting, "正在启动 Web 服务：" + commandLine);

            ProcessResult web = RunCommand(commandLine, 0, OnWebLine);

            if (web.Canceled)
            {
                SetState(LauncherState.Stopped, "Web 服务已停止");
                return;
            }

            if (!web.Started)
            {
                _log.Error("Web 服务无法启动：" + web.StartError);
                SetState(LauncherState.Failed, "Web 服务无法启动");
                return;
            }

            if (web.ExitCode == 0)
            {
                SetState(LauncherState.Stopped, "Web 服务已退出");
                return;
            }

            if (allowBuildRecovery && !Token.IsCancellationRequested && MentionsMissingBuild(web))
            {
                _log.Warn("dsh 报告构建产物与当前源码不一致，补做一次完整构建后重试。");
                if (Build(null) && !Token.IsCancellationRequested)
                {
                    RunWeb(port, false);
                    return;
                }

                if (Token.IsCancellationRequested)
                {
                    SetState(LauncherState.Stopped, "已取消");
                    return;
                }

                SetState(LauncherState.Failed, "构建失败，Web 服务未能启动");
                return;
            }

            _log.Error("Web 服务异常退出，退出码 " + web.ExitCode.ToString(CultureInfo.InvariantCulture) + "。");
            LogTail(web, 40);
            SetState(LauncherState.Failed, "Web 服务异常退出（退出码 " + web.ExitCode.ToString(CultureInfo.InvariantCulture) + "）");
        }

        /// <summary>判断 dsh 的输出是否在要求先执行一次完整构建。</summary>
        private static bool MentionsMissingBuild(ProcessResult web)
        {
            for (int i = 0; i < web.Lines.Count; i++)
            {
                string line = web.Lines[i];
                if (line.IndexOf("run a complete pnpm run build", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("client artifacts differ", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("client build record", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>从 Web 服务输出里取出带 token 的访问地址。</summary>
        private void OnWebLine(string line)
        {
            if (_url != null)
            {
                return;
            }

            Match match = UrlPattern.Match(line);
            if (!match.Success)
            {
                return;
            }

            _url = match.Value;
            _log.Success("Web 服务已就绪：" + _url);
            Action<string> handler = UrlDetected;
            if (handler != null)
            {
                handler(_url);
            }

            SetState(LauncherState.Running, "Web 服务运行中：" + _url);
            OpenInterface(_url);
        }

        /// <summary>
        /// 按配置打开界面：openTarget = app 时启动 appCommand（{url} 替换为带 token 的地址），
        /// none 时只显示地址；browser 由 dsh 自己完成，这里不重复打开。
        /// </summary>
        private void OpenInterface(string url)
        {
            if (!_config.OpensInApp)
            {
                return;
            }

            LaunchAppInterface(url);
        }

        /// <summary>
        /// 启动界面应用，并在后台记录它新打开的窗口，供退出时关闭。
        /// 返回是否成功发出启动命令。
        /// </summary>
        internal bool LaunchAppInterface(string url)
        {
            string command = _config.ResolveAppCommand(url);
            if (command.Length == 0)
            {
                _log.Warn("openTarget = app 但 appCommand 为空，界面地址：" + url);
                return false;
            }

            string fileName;
            string arguments;
            AppConfig.SplitCommand(command, out fileName, out arguments);

            // 先取窗口基线，之后新出现的窗口就是这次启动打开的应用窗口。
            List<IntPtr> baseline = _config.CloseAppOnExit ? WindowCloser.SnapshotHandles() : null;

            try
            {
                ProcessStartInfo info = new ProcessStartInfo(fileName);
                info.Arguments = arguments;
                info.UseShellExecute = true;
                Process.Start(info);
                _log.Success("已启动界面应用：" + command);
            }
            catch (Exception ex)
            {
                _log.Error("启动界面应用失败：" + ex.Message);
                _log.Info("可在浏览器中手动打开：" + url);
                return false;
            }

            if (_config.CloseAppOnExit)
            {
                StartAppWindowWatch(baseline);
            }

            return true;
        }

        /// <summary>后台等待应用窗口出现并记录其句柄，最长约 20 秒。</summary>
        private void StartAppWindowWatch(List<IntPtr> baseline)
        {
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    try
                    {
                        WindowInfo window = WindowCloser.FindNewAppWindow(baseline, _config.AppWindowTitle);
                        if (window != null)
                        {
                            lock (_appWindowGate)
                            {
                                _appWindow = window.Handle;
                                _appWindowTitle = window.Title;
                            }

                            _log.Info("已记录界面应用窗口：" + WindowCloser.NormalizeTitle(window.Title));
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("识别界面应用窗口失败：" + ex.Message);
                        return;
                    }

                    Thread.Sleep(500);
                }

                _log.Info("未识别到新的界面应用窗口，退出时不会关闭它。");
            });
        }

        /// <summary>
        /// 关闭界面应用窗口：优先关闭本次启动记录下来的窗口；句柄失效时按配置的标题关键字再找一次。
        /// 只投递 WM_CLOSE，不结束任何进程。
        /// </summary>
        internal bool CloseAppInterface()
        {
            if (!_config.CloseAppOnExit)
            {
                return false;
            }

            IntPtr recorded;
            string recordedTitle;
            lock (_appWindowGate)
            {
                recorded = _appWindow;
                recordedTitle = _appWindowTitle;
                _appWindow = IntPtr.Zero;
                _appWindowTitle = null;
            }

            IntPtr target = IntPtr.Zero;
            string targetTitle = null;

            if (recorded != IntPtr.Zero && WindowCloser.IsWindow(recorded))
            {
                target = recorded;
                targetTitle = recordedTitle;
            }
            else
            {
                WindowInfo found = WindowCloser.FindAppWindowByTitle(_config.AppWindowTitle);
                if (found != null)
                {
                    target = found.Handle;
                    targetTitle = found.Title;
                }
            }

            if (target == IntPtr.Zero)
            {
                return false;
            }

            string label = targetTitle == null ? "(未知标题)" : WindowCloser.NormalizeTitle(targetTitle);
            if (WindowCloser.RequestClose(target))
            {
                _log.Success("已请求关闭界面应用窗口：" + label);
                return true;
            }

            _log.Warn("关闭界面应用窗口失败，请手动关闭：" + label);
            return false;
        }

        private ProcessResult RunCommand(string commandLine, int timeoutMs, Action<string> onLine)
        {
            if (Token.IsCancellationRequested)
            {
                ProcessResult canceled = new ProcessResult();
                canceled.Command = commandLine;
                canceled.Started = true;
                canceled.Canceled = true;
                canceled.ExitCode = -1;
                return canceled;
            }

            ProcessSpec spec = new ProcessSpec();
            spec.Command = commandLine;
            spec.WorkingDirectory = _config.HarnessDir;
            spec.TimeoutMs = timeoutMs;
            spec.Echo = _config.EchoOutput;
            _log.Command(commandLine);
            return ProcessRunner.Run(spec, onLine, Token);
        }

        private void LogTail(ProcessResult result, int count)
        {
            string tail = result.Tail(count);
            if (tail.Length == 0)
            {
                return;
            }

            _log.Blank();
            _log.Info("—— 命令输出末尾 ——");
            string[] lines = tail.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length > 0)
                {
                    _log.Error(lines[i]);
                }
            }

            _log.Info("—— 输出结束 ——");
        }

        private static string ShortCommit(string commit)
        {
            if (string.IsNullOrEmpty(commit))
            {
                return "(未知)";
            }

            return commit.Length > 8 ? commit.Substring(0, 8) : commit;
        }
    }
}
