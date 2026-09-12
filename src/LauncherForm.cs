using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 启动器主窗口：顶部显示阶段与仓库信息，中间是构建/运行日志，底部是操作按钮。
    /// 打开窗口即自动开始一次启动流程，窗口本身作为运行期监视台保留。
    /// </summary>
    internal sealed class LauncherForm : Form
    {
        private readonly AppConfig _config;
        private readonly Log _log;
        private readonly HarnessRunner _runner;
        private readonly bool _noAutoStart;
        private readonly bool _assumeYes;

        private readonly Label _stateLabel;
        private readonly Label _detailLabel;
        private readonly ProgressBar _progress;
        private readonly RichTextBox _console;
        private readonly Button _startButton;
        private readonly Button _stopButton;
        private readonly Button _diagnoseButton;
        private readonly Button _browseButton;
        private readonly CheckBox _closeStops;
        private string _lastUrl;
        private bool _diagnosing;

        internal LauncherForm(AppConfig config, Log log, bool noAutoStart, bool assumeYes, bool forceBuild)
        {
            _config = config;
            _log = log;
            _noAutoStart = noAutoStart;
            _assumeYes = assumeYes;

            Text = "DeepSeek Harness 启动器";
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            ClientSize = new Size(940, 640);
            MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(243, 244, 246);
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (ArgumentException)
            {
                // 资源管理器视图下没有关联图标时使用默认窗口图标。
            }

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 116;
            header.BackColor = Color.White;
            header.Padding = new Padding(18, 12, 18, 8);

            Label title = new Label();
            title.AutoSize = true;
            title.Font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Point);
            title.ForeColor = Color.FromArgb(17, 24, 39);
            title.Location = new Point(16, 10);
            title.Text = "DeepSeek Harness";

            _stateLabel = new Label();
            _stateLabel.AutoSize = false;
            _stateLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
            _stateLabel.Location = new Point(18, 44);
            _stateLabel.Size = new Size(884, 22);
            _stateLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _stateLabel.Text = "准备中…";

            _detailLabel = new Label();
            _detailLabel.AutoSize = false;
            _detailLabel.Font = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point);
            _detailLabel.ForeColor = Color.FromArgb(107, 114, 128);
            _detailLabel.Location = new Point(18, 68);
            _detailLabel.Size = new Size(884, 36);
            _detailLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _detailLabel.Text = string.Empty;

            _progress = new ProgressBar();
            _progress.Dock = DockStyle.Bottom;
            _progress.Height = 6;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 30;
            _progress.Visible = false;

            header.Controls.Add(title);
            header.Controls.Add(_stateLabel);
            header.Controls.Add(_detailLabel);
            header.Controls.Add(_progress);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 60;
            footer.BackColor = Color.White;
            footer.Padding = new Padding(14, 12, 14, 12);

            _startButton = MakeButton("启动", 88);
            _startButton.Click += OnStartClicked;
            _stopButton = MakeButton("停止", 88);
            _stopButton.Enabled = false;
            _stopButton.Click += OnStopClicked;
            _diagnoseButton = MakeButton("环境诊断", 100);
            _diagnoseButton.Click += OnDiagnoseClicked;
            _browseButton = MakeButton("打开界面", 100);
            _browseButton.Enabled = false;
            _browseButton.Click += OnBrowseClicked;
            Button folderButton = MakeButton("源码目录", 92);
            folderButton.Click += OnOpenFolderClicked;
            Button logButton = MakeButton("打开日志", 92);
            logButton.Click += OnOpenLogClicked;
            Button clearButton = MakeButton("清空", 72);
            clearButton.Click += OnClearClicked;

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Left;
            buttons.AutoSize = true;
            buttons.WrapContents = false;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.Controls.Add(_startButton);
            buttons.Controls.Add(_stopButton);
            buttons.Controls.Add(_diagnoseButton);
            buttons.Controls.Add(_browseButton);
            buttons.Controls.Add(folderButton);
            buttons.Controls.Add(logButton);
            buttons.Controls.Add(clearButton);

            _closeStops = new CheckBox();
            _closeStops.Text = "关闭窗口时停止服务并关闭界面应用";
            _closeStops.AutoSize = true;
            _closeStops.Checked = _config.CloseStopsService;
            _closeStops.Dock = DockStyle.Right;
            _closeStops.Padding = new Padding(0, 8, 4, 0);

            footer.Controls.Add(_closeStops);
            footer.Controls.Add(buttons);

            _console = new RichTextBox();
            _console.Dock = DockStyle.Fill;
            _console.ReadOnly = true;
            _console.BackColor = Color.FromArgb(17, 24, 39);
            _console.ForeColor = Color.FromArgb(229, 231, 235);
            _console.Font = new Font("Consolas", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            _console.WordWrap = false;
            _console.ScrollBars = RichTextBoxScrollBars.Both;
            _console.BorderStyle = BorderStyle.None;
            _console.DetectUrls = false;
            _console.HideSelection = false;

            Panel consoleHost = new Panel();
            consoleHost.Dock = DockStyle.Fill;
            consoleHost.Padding = new Padding(14, 10, 14, 10);
            consoleHost.BackColor = Color.FromArgb(243, 244, 246);
            consoleHost.Controls.Add(_console);

            Controls.Add(consoleHost);
            Controls.Add(footer);
            Controls.Add(header);

            _log.Entry += OnLogEntry;

            _runner = new HarnessRunner(_config, _log);
            _runner.ForceBuild = forceBuild;
            _runner.StateChanged += OnStateChanged;
            _runner.UrlDetected += OnUrlDetected;
            _runner.ConfirmUpdate = ConfirmUpdateOnUiThread;
            _runner.ConfirmDirty = ConfirmDirtyOnUiThread;
            _runner.ConfirmPortBusy = ConfirmPortBusyOnUiThread;

            UpdateDetail();
            Shown += OnShown;
        }

        private static Button MakeButton(string text, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 30;
            button.Margin = new Padding(0, 0, 8, 0);
            button.FlatStyle = FlatStyle.System;
            return button;
        }

        private void OnShown(object sender, EventArgs e)
        {
            AnnounceEnvironment();
            if (_noAutoStart)
            {
                _log.Info("已按 --no-auto-start 跳过自动启动，点击“启动”开始。");
                _stateLabel.Text = "空闲";
                _stateLabel.ForeColor = Color.FromArgb(75, 85, 99);
                _detailLabel.Text = "已跳过自动启动；点击“启动”开始构建并拉起 Web 服务。" + Environment.NewLine + DetailText();
                _startButton.Enabled = true;
                _stopButton.Enabled = false;
                return;
            }

            StartPipeline();
        }

        private void AnnounceEnvironment()
        {
            _log.Info("DeepSeek Harness 启动器已启动。");
            _log.Info("配置文件：" + (_config.ConfigPath == null ? "(未找到，使用默认值)" : _config.ConfigPath));
            _log.Info("日志文件：" + (_log.FilePath == null ? "(不可写)" : _log.FilePath));
            for (int i = 0; i < _config.Warnings.Count; i++)
            {
                _log.Warn(_config.Warnings[i]);
            }
        }

        private void StartPipeline()
        {
            _startButton.Enabled = false;
            _stopButton.Enabled = true;
            _runner.Start();
        }

        private void OnStartClicked(object sender, EventArgs e)
        {
            if (_runner.IsBusy)
            {
                return;
            }

            _log.Blank();
            _log.Info("—— 重新启动 ——");
            StartPipeline();
        }

        private void OnStopClicked(object sender, EventArgs e)
        {
            _stopButton.Enabled = false;
            _log.Warn("正在停止…");
            // 服务停下后应用窗口就没用了，一并关掉。
            _runner.CloseAppInterface();
            _runner.Stop();
        }

        private void OnDiagnoseClicked(object sender, EventArgs e)
        {
            if (_diagnosing || _runner.IsBusy)
            {
                return;
            }

            _diagnosing = true;
            _diagnoseButton.Enabled = false;
            _log.Blank();
            _log.Info("—— 只读环境诊断（不会修改源码目录）——");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    Diagnose.Run(_config, _log, false);
                }
                catch (Exception ex)
                {
                    _log.Error("诊断失败：" + ex.Message);
                }
                finally
                {
                    SafeInvoke(delegate
                    {
                        _diagnosing = false;
                        _diagnoseButton.Enabled = true;
                    });
                }
            });
        }

        private void OnBrowseClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_lastUrl))
            {
                return;
            }

            // 配置了界面应用时按同样方式打开（并同样记录窗口），否则交给默认浏览器。
            if (_config.OpensInApp)
            {
                if (_runner.LaunchAppInterface(_lastUrl))
                {
                    return;
                }

                _log.Warn("改为用默认浏览器打开界面。");
            }

            OpenTarget(_lastUrl);
        }

        private void OnOpenFolderClicked(object sender, EventArgs e)
        {
            OpenTarget(_config.HarnessDir);
        }

        private void OnOpenLogClicked(object sender, EventArgs e)
        {
            if (_log.FilePath == null)
            {
                MessageBox.Show(this, "日志文件不可写，请查看界面输出。", "打开日志", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            OpenTarget(_log.FilePath);
        }

        private void OnClearClicked(object sender, EventArgs e)
        {
            _console.Clear();
        }

        private void OpenTarget(string target)
        {
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开：" + target + Environment.NewLine + ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnLogEntry(LogLevel level, string message)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<LogLevel, string>(OnLogEntry), level, message);
                }
                catch (InvalidOperationException)
                {
                    // 窗口在日志到达前关闭，丢弃该条日志。
                }

                return;
            }

            AppendConsole(level, message);
        }

        private void AppendConsole(LogLevel level, string message)
        {
            const int Cap = 300000;
            if (_console.TextLength > Cap)
            {
                _console.Select(0, Cap / 2);
                _console.SelectedText = string.Empty;
            }

            _console.SelectionStart = _console.TextLength;
            _console.SelectionLength = 0;
            _console.SelectionColor = ColorFor(level);
            _console.AppendText(message + Environment.NewLine);
            _console.SelectionColor = _console.ForeColor;
            _console.SelectionStart = _console.TextLength;
            _console.ScrollToCaret();
        }

        private static Color ColorFor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Command:
                    return Color.FromArgb(147, 197, 253);
                case LogLevel.Success:
                    return Color.FromArgb(134, 239, 172);
                case LogLevel.Warn:
                    return Color.FromArgb(252, 211, 77);
                case LogLevel.Error:
                    return Color.FromArgb(252, 165, 165);
                default:
                    return Color.FromArgb(229, 231, 235);
            }
        }

        private void OnStateChanged(LauncherState state, string detail)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<LauncherState, string>(OnStateChanged), state, detail);
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            _stateLabel.Text = StateText(state);
            _stateLabel.ForeColor = StateColor(state);
            _detailLabel.Text = detail + Environment.NewLine + DetailText();
            _progress.Visible = state == LauncherState.Checking || state == LauncherState.Updating
                || state == LauncherState.Installing || state == LauncherState.Building
                || state == LauncherState.Starting || state == LauncherState.WaitingUser;

            bool busy = _runner.IsBusy;
            _startButton.Enabled = !busy;
            _stopButton.Enabled = busy;
            _diagnoseButton.Enabled = !busy && !_diagnosing;
            Text = "DeepSeek Harness 启动器 — " + StateText(state);
        }

        private void OnUrlDetected(string url)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string>(OnUrlDetected), url);
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            _lastUrl = url;
            _browseButton.Enabled = true;
        }

        private void UpdateDetail()
        {
            _detailLabel.Text = DetailText();
        }

        private string DetailText()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "源码目录：{0}    端口：{1}    更新检查：{2}",
                _config.HarnessDir,
                _config.Port <= 0 ? "自动" : _config.Port.ToString(CultureInfo.InvariantCulture),
                _config.CheckUpdates ? "开" : "关");
        }

        private static string StateText(LauncherState state)
        {
            switch (state)
            {
                case LauncherState.Checking:
                    return "检查更新与环境";
                case LauncherState.WaitingUser:
                    return "等待确认";
                case LauncherState.Updating:
                    return "更新源码";
                case LauncherState.Installing:
                    return "安装依赖";
                case LauncherState.Building:
                    return "构建中";
                case LauncherState.Starting:
                    return "启动 Web 服务";
                case LauncherState.Running:
                    return "运行中";
                case LauncherState.Stopped:
                    return "已停止";
                case LauncherState.Failed:
                    return "失败";
                default:
                    return "空闲";
            }
        }

        private static Color StateColor(LauncherState state)
        {
            switch (state)
            {
                case LauncherState.Running:
                    return Color.FromArgb(21, 128, 61);
                case LauncherState.Failed:
                    return Color.FromArgb(185, 28, 28);
                case LauncherState.Stopped:
                    return Color.FromArgb(75, 85, 99);
                default:
                    return Color.FromArgb(29, 78, 216);
            }
        }

        private bool ConfirmUpdateOnUiThread(UpdateDecision decision)
        {
            if (_assumeYes)
            {
                _log.Info("--yes：自动确认更新到 " + decision.LatestRemote.Name + "。");
                return true;
            }

            return (bool)Invoke(new Func<bool>(delegate
            {
                string text = decision.Describe(_config.TagPrefix)
                    + Environment.NewLine
                    + "现在更新到 " + decision.LatestRemote.Name + " 吗？";
                DialogResult result = MessageBox.Show(
                    this,
                    text,
                    "发现新版本 tag",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);
                return result == DialogResult.Yes;
            }));
        }

        private DirtyChoice ConfirmDirtyOnUiThread(string targetTag, int dirtyCount)
        {
            if (_assumeYes)
            {
                _log.Warn("--yes：工作区有未提交修改，自动跳过更新而不触碰本地改动。");
                return DirtyChoice.SkipUpdate;
            }

            return (DirtyChoice)Invoke(new Func<DirtyChoice>(delegate
            {
                string text = string.Format(
                    CultureInfo.InvariantCulture,
                    "工作区有 {0} 项未提交修改，直接检出 {1} 可能会失败或丢失修改。{2}{2}"
                    + "“是”：先把修改收进 git stash，再更新（推荐，可用 git stash pop 恢复）{2}"
                    + "“否”：保留当前修改，跳过本次更新，直接用现有代码构建{2}"
                    + "“取消”：终止本次启动",
                    dirtyCount,
                    targetTag,
                    Environment.NewLine);
                DialogResult result = MessageBox.Show(
                    this,
                    text,
                    "工作区有未提交修改",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1);

                if (result == DialogResult.Yes)
                {
                    return DirtyChoice.StashAndUpdate;
                }

                if (result == DialogResult.No)
                {
                    return DirtyChoice.SkipUpdate;
                }

                return DirtyChoice.Cancel;
            }));
        }

        private bool ConfirmPortBusyOnUiThread(int busyPort, int suggestedPort)
        {
            if (_assumeYes)
            {
                _log.Info("--yes：端口 " + busyPort.ToString(CultureInfo.InvariantCulture)
                    + " 被占用，自动改用 " + suggestedPort.ToString(CultureInfo.InvariantCulture) + "。");
                return true;
            }

            return (bool)Invoke(new Func<bool>(delegate
            {
                string text = string.Format(
                    CultureInfo.InvariantCulture,
                    "端口 {0} 已被占用（可能已经有一个 DeepSeek Harness 在运行）。{1}{1}"
                    + "改用端口 {2} 启动吗？{1}选择“否”将终止本次启动，"
                    + "请先关闭占用该端口的程序，或修改 {3} 中的 port。",
                    busyPort,
                    Environment.NewLine,
                    suggestedPort,
                    AppConfig.FileName);
                DialogResult result = MessageBox.Show(
                    this,
                    text,
                    "端口被占用",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1);
                return result == DialogResult.Yes;
            }));
        }

        private void SafeInvoke(Action action)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                Invoke(action);
            }
            catch (InvalidOperationException)
            {
                // 窗口已关闭，忽略这次界面更新。
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            if (!_closeStops.Checked)
            {
                return;
            }

            // 先摘掉需要用户回答的回调：关闭过程中不再弹窗，避免与下面的等待互相阻塞。
            _runner.ConfirmUpdate = null;
            _runner.ConfirmDirty = null;
            _runner.ConfirmPortBusy = null;

            // 先关界面应用窗口（此时服务还在，窗口显示的是正常页面），再停服务。
            _runner.CloseAppInterface();
            _runner.Stop();
            if (_runner.IsBusy && !_runner.WaitForExit(8000))
            {
                _log.Warn("Web 服务未在 8 秒内退出，可能仍有残留进程。");
            }
        }
    }
}
