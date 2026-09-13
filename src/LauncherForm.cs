using System;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 启动器主窗口。
    ///
    /// 布局策略：文字控件一律按内容自动尺寸（AutoSize），换行标签由
    /// <see cref="UpdateWrapWidths"/> 跟随窗口宽度重算 MaximumSize，外层用
    /// TableLayoutPanel 的 AutoSize 行承载，因此窗口在任何尺寸下文字都不会被裁剪，
    /// 只有中间的控制台区域会伸缩。
    ///
    /// 视觉风格取自 DSH Web 客户端的设计令牌（见 <see cref="UiTheme"/>）。
    /// </summary>
    internal sealed class LauncherForm : Form
    {
        private readonly AppConfig _config;
        private readonly Log _log;
        private readonly HarnessRunner _runner;
        private readonly UiTheme _theme;
        private readonly bool _noAutoStart;
        private readonly bool _assumeYes;

        private BorderPanel _headerCard;
        private TableLayoutPanel _headerStack;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private StatusPill _pill;
        private Label _detailLabel;
        private Label _metaLabel;
        private BusyBar _busyBar;

        private BorderPanel _consoleCard;
        private RichTextBox _console;

        private FlowLayoutPanel _buttonFlow;
        private AppButton _startButton;
        private AppButton _stopButton;
        private AppButton _diagnoseButton;
        private AppButton _browseButton;
        private AppButton _folderButton;
        private AppButton _logButton;
        private AppButton _clearButton;
        private CheckBox _closeStops;
        private TargetSelector _targetSelector;
        private System.Collections.Generic.List<string> _recentTargets = new System.Collections.Generic.List<string>();

        private string _lastUrl;
        private bool _diagnosing;

        internal LauncherForm(AppConfig config, Log log, bool noAutoStart, bool assumeYes, bool forceBuild)
        {
            _config = config;
            _log = log;
            _noAutoStart = noAutoStart;
            _assumeYes = assumeYes;
            _theme = UiTheme.Select(config.Theme);

            Text = "DeepSeek Harness 启动器";
            AutoScaleMode = AutoScaleMode.Font;
            Font = _theme.BodyFont;
            ClientSize = new Size(940, 620);
            MinimumSize = new Size(660, 430);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = _theme.AppBackground;
            ForeColor = _theme.TextPrimary;
            DoubleBuffered = true;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (ArgumentException)
            {
                // 没有可提取的关联图标时使用默认窗口图标。
            }

            BuildUi();

            _log.Entry += OnLogEntry;

            _runner = new HarnessRunner(_config, _log);
            _runner.ForceBuild = forceBuild;
            _runner.StateChanged += OnStateChanged;
            _runner.UrlDetected += OnUrlDetected;
            _runner.ConfirmUpdate = ConfirmUpdateOnUiThread;
            _runner.ConfirmDirty = ConfirmDirtyOnUiThread;
            _runner.ConfirmPortBusy = ConfirmPortBusyOnUiThread;

            ApplyState(LauncherState.Idle, "准备中…");
            Shown += OnShown;
        }

        internal UiTheme Theme
        {
            get { return _theme; }
        }

        // ---------------- 布局 ----------------


        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = _theme.AppBackground;
            root.Padding = new Padding(12, 10, 12, 10);
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _headerCard = new BorderPanel();
            _headerCard.Dock = DockStyle.Fill;
            _headerCard.AutoSize = true;
            _headerCard.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _headerCard.Padding = new Padding(14, 11, 14, 9);
            _headerCard.Margin = new Padding(0);
            _headerCard.FillColor = _theme.Surface;
            _headerCard.BorderColor = _theme.Border;
            _headerCard.Radius = 10;
            _headerCard.BackColor = _theme.Surface;

            _headerStack = new TableLayoutPanel();
            _headerStack.Dock = DockStyle.Top;
            _headerStack.AutoSize = true;
            _headerStack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _headerStack.ColumnCount = 1;
            _headerStack.RowCount = 4;
            _headerStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _headerStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _headerStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _headerStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _headerStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F));

            // 第一行：标题 / 副标题 + 右侧状态徽标
            TableLayoutPanel topRow = new TableLayoutPanel();
            topRow.Dock = DockStyle.Fill;
            topRow.AutoSize = true;
            topRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            topRow.Margin = new Padding(0);
            topRow.ColumnCount = 2;
            topRow.RowCount = 1;
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            TableLayoutPanel titleStack = new TableLayoutPanel();
            titleStack.Dock = DockStyle.Fill;
            titleStack.AutoSize = true;
            titleStack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            titleStack.Margin = new Padding(0);
            titleStack.ColumnCount = 1;
            titleStack.RowCount = 2;
            titleStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _titleLabel = new Label();
            _titleLabel.AutoSize = true;
            _titleLabel.Margin = new Padding(0);
            _titleLabel.Font = _theme.TitleFont;
            _titleLabel.ForeColor = _theme.TextPrimary;
            _titleLabel.BackColor = Color.Transparent;
            _titleLabel.Text = "DeepSeek Harness";

            _subtitleLabel = new Label();
            _subtitleLabel.AutoSize = true;
            _subtitleLabel.Margin = new Padding(0, 1, 0, 0);
            _subtitleLabel.Font = _theme.SubtitleFont;
            _subtitleLabel.ForeColor = _theme.TextTertiary;
            _subtitleLabel.BackColor = Color.Transparent;
            _subtitleLabel.Text = "启动器 · 检查远端 tag、按需更新、构建并拉起 Web 服务";

            titleStack.Controls.Add(_titleLabel, 0, 0);
            titleStack.Controls.Add(_subtitleLabel, 0, 1);

            _pill = new StatusPill();
            _pill.Font = _theme.PillFont;
            _pill.Margin = new Padding(10, 3, 0, 0);
            _pill.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            topRow.Controls.Add(titleStack, 0, 0);
            topRow.Controls.Add(_pill, 1, 0);

            // 第二行：当前阶段说明（自动换行）
            _detailLabel = new Label();
            _detailLabel.AutoSize = true;
            _detailLabel.Margin = new Padding(0, 7, 0, 0);
            _detailLabel.Font = _theme.BodyFont;
            _detailLabel.ForeColor = _theme.TextSecondary;
            _detailLabel.BackColor = Color.Transparent;
            _detailLabel.Text = string.Empty;

            // 第三行：仓库 / 端口等次要信息（自动换行）
            _metaLabel = new Label();
            _metaLabel.AutoSize = true;
            _metaLabel.Margin = new Padding(0, 3, 0, 0);
            _metaLabel.Font = _theme.MetaFont;
            _metaLabel.ForeColor = _theme.TextTertiary;
            _metaLabel.BackColor = Color.Transparent;
            _metaLabel.Text = string.Empty;

            _busyBar = new BusyBar();
            _busyBar.Dock = DockStyle.Fill;
            _busyBar.Margin = new Padding(0, 7, 0, 0);
            _busyBar.TrackColor = _theme.SurfaceAlt;
            _busyBar.BarColor = _theme.Accent;

            _headerStack.Controls.Add(topRow, 0, 0);
            _headerStack.Controls.Add(_detailLabel, 0, 1);
            _headerStack.Controls.Add(_metaLabel, 0, 2);
            _headerStack.Controls.Add(_busyBar, 0, 3);
            _headerCard.Controls.Add(_headerStack);

            // 控制台卡片
            _consoleCard = new BorderPanel();
            _consoleCard.Dock = DockStyle.Fill;
            _consoleCard.Margin = new Padding(0, 8, 0, 0);
            _consoleCard.Padding = new Padding(10, 8, 10, 8);
            _consoleCard.FillColor = _theme.ConsoleBackground;
            _consoleCard.BorderColor = _theme.Border;
            _consoleCard.Radius = 10;
            _consoleCard.BackColor = _theme.ConsoleBackground;

            _console = new RichTextBox();
            _console.Dock = DockStyle.Fill;
            _console.ReadOnly = true;
            _console.BackColor = _theme.ConsoleBackground;
            _console.ForeColor = _theme.TextSecondary;
            _console.Font = _theme.MonoFont;
            _console.WordWrap = true;
            _console.ScrollBars = RichTextBoxScrollBars.Vertical;
            _console.BorderStyle = BorderStyle.None;
            _console.DetectUrls = false;
            _console.HideSelection = false;
            _consoleCard.Controls.Add(_console);

            // 底部操作区
            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.AutoSize = true;
            footer.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            footer.Margin = new Padding(0);
            footer.ColumnCount = 1;
            footer.RowCount = 2;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _buttonFlow = new FlowLayoutPanel();
            _buttonFlow.Dock = DockStyle.Fill;
            _buttonFlow.AutoSize = true;
            _buttonFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            // 不换行：7 个按钮在最窄窗口（660px）下也排得成一行。
            // 换行会让 FlowLayoutPanel 在零宽度测量时报告多行高度，把底部撑高。
            _buttonFlow.WrapContents = false;
            _buttonFlow.Margin = new Padding(0, 8, 0, 0);
            _buttonFlow.FlowDirection = FlowDirection.LeftToRight;

            _startButton = new AppButton(_theme, "启动", ButtonRole.Primary);
            _startButton.Click += OnStartClicked;
            _stopButton = new AppButton(_theme, "停止", ButtonRole.Ghost);
            _stopButton.Enabled = false;
            _stopButton.Click += OnStopClicked;
            _diagnoseButton = new AppButton(_theme, "环境诊断", ButtonRole.Ghost);
            _diagnoseButton.Click += OnDiagnoseClicked;
            _browseButton = new AppButton(_theme, "打开界面", ButtonRole.Ghost);
            _browseButton.Enabled = false;
            _browseButton.Click += OnBrowseClicked;
            _folderButton = new AppButton(_theme, "源码目录", ButtonRole.Ghost);
            _folderButton.Click += OnOpenFolderClicked;
            _logButton = new AppButton(_theme, "打开日志", ButtonRole.Ghost);
            _logButton.Click += OnOpenLogClicked;
            _clearButton = new AppButton(_theme, "清空", ButtonRole.Ghost);
            _clearButton.Click += OnClearClicked;

            _buttonFlow.Controls.Add(_startButton);
            _buttonFlow.Controls.Add(_stopButton);
            _buttonFlow.Controls.Add(_diagnoseButton);
            _buttonFlow.Controls.Add(_browseButton);
            _buttonFlow.Controls.Add(_folderButton);
            _buttonFlow.Controls.Add(_logButton);
            _buttonFlow.Controls.Add(_clearButton);

            // 底部第二行：左边勾选框，右边目标目录选择器（DSH 风格的字段 + 弹出菜单）。
            _closeStops = new CheckBox();
            _closeStops.AutoSize = true;
            _closeStops.FlatStyle = FlatStyle.Flat;
            _closeStops.Font = _theme.MetaFont;
            _closeStops.ForeColor = _theme.TextSecondary;
            _closeStops.BackColor = Color.Transparent;
            _closeStops.Margin = new Padding(0, 6, 14, 0);
            _closeStops.Text = "关闭窗口时停止服务并关闭界面应用";
            _closeStops.Checked = _config.CloseStopsService;

            _targetSelector = new TargetSelector(_theme);
            _targetSelector.Dock = DockStyle.Fill;
            _targetSelector.Margin = new Padding(0, 2, 0, 0);
            _targetSelector.TargetPicked += OnTargetPicked;
            _targetSelector.BrowseRequested += OnTargetBrowseRequested;

            TableLayoutPanel targetRow = new TableLayoutPanel();
            targetRow.Dock = DockStyle.Fill;
            targetRow.AutoSize = true;
            targetRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            targetRow.Margin = new Padding(0);
            targetRow.ColumnCount = 2;
            targetRow.RowCount = 1;
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            targetRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            targetRow.Controls.Add(_closeStops, 0, 0);
            targetRow.Controls.Add(_targetSelector, 1, 0);

            footer.Controls.Add(_buttonFlow, 0, 0);
            footer.Controls.Add(targetRow, 0, 1);

            root.Controls.Add(_headerCard, 0, 0);
            root.Controls.Add(_consoleCard, 0, 1);
            root.Controls.Add(footer, 0, 2);

            Controls.Add(root);
            RefreshTargets();
        }

        // ---------------- 目标源码目录 ----------------

        /// <summary>把当前配置的默认目录 / 当前目录 / 最近使用同步到选择器。</summary>
        private void RefreshTargets()
        {
            _recentTargets = HarnessTargets.LoadRecent(_config);
            _targetSelector.SetTargets(_config.DefaultHarnessDir, _config.HarnessDir, _recentTargets);
        }

        private void OnTargetPicked(string path)
        {
            if (!ApplyTarget(path, true))
            {
                return;
            }

            RememberTarget();
            _log.Info("已切换源码目录：" + _config.HarnessDir);
        }

        private void OnTargetBrowseRequested()
        {
            BrowseForTarget();
        }

        /// <summary>校验并应用目标目录；返回 false 表示这次不能启动。</summary>
        private bool ApplyTarget(string path, bool interactive)
        {
            if (path == null)
            {
                return false;
            }

            string detail;
            TargetVerdict verdict = HarnessTargets.Validate(path, out detail);
            if (verdict == TargetVerdict.Missing)
            {
                if (interactive)
                {
                    MessageBox.Show(this, detail, "源码目录不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    _log.Error("源码目录不可用：" + detail);
                    ApplyState(LauncherState.Failed, "源码目录不可用：" + detail + "（可在底部换一个目录后点“启动”）");
                }

                return false;
            }

            if (verdict == TargetVerdict.NotHarness)
            {
                if (interactive)
                {
                    DialogResult answer = MessageBox.Show(
                        this,
                        detail + Environment.NewLine + Environment.NewLine
                        + "仍然用它作为源码目录吗？（找不到 dsh 启动脚本时，启动很可能失败）",
                        "确认源码目录",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes)
                    {
                        return false;
                    }
                }
                else
                {
                    _log.Warn(detail + "（继续使用）");
                }
            }

            _config.SetHarnessDir(path);
            _metaLabel.Text = MetaText();
            _targetSelector.SetTargets(_config.DefaultHarnessDir, _config.HarnessDir, _recentTargets);
            UpdateWrapWidths();
            return true;
        }

        /// <summary>记住本次使用的目录，下次可从菜单里的「最近使用」直接选。</summary>
        private void RememberTarget()
        {
            if (_config.IsDefaultHarnessDir)
            {
                return;
            }

            _recentTargets = HarnessTargets.AddRecent(_recentTargets, _config.HarnessDir, _config.DefaultHarnessDir);
            HarnessTargets.SaveRecent(_config, _recentTargets);
            _targetSelector.SetTargets(_config.DefaultHarnessDir, _config.HarnessDir, _recentTargets);
        }

        private void BrowseForTarget()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 deepseek-harness 源码目录";
                dialog.ShowNewFolderButton = false;
                dialog.SelectedPath = Directory.Exists(_config.HarnessDir) ? _config.HarnessDir : _config.LauncherRoot;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                if (ApplyTarget(dialog.SelectedPath, true))
                {
                    RememberTarget();
                    _log.Info("已切换源码目录：" + _config.HarnessDir);
                }
            }
        }
        /// <summary>换行标签的可用宽度随窗口变化，这里同步它们的 MaximumSize。</summary>
        private void UpdateWrapWidths()
        {
            // 布局可能在 BuildUi 之前就被触发（设置 AutoScaleMode / Font 时就会），
            // 那时控件还不存在，必须先返回，否则会在布局回调里空引用。
            if (_headerCard == null || _headerStack == null || _detailLabel == null
                || _metaLabel == null || _titleLabel == null || _subtitleLabel == null || _pill == null)
            {
                return;
            }

            int headerWidth = _headerCard.ClientSize.Width - _headerCard.Padding.Horizontal;
            if (headerWidth > 0)
            {
                SetWrapWidth(_detailLabel, headerWidth);
                SetWrapWidth(_metaLabel, headerWidth);
            }

            int topWidth = _headerStack.ClientSize.Width;
            if (topWidth > 0)
            {
                int titleWidth = Math.Max(140, topWidth - _pill.Width - _pill.Margin.Horizontal);
                SetWrapWidth(_titleLabel, titleWidth);
                SetWrapWidth(_subtitleLabel, titleWidth);
            }
        }

        /// <summary>只在宽度真正变化时赋值，避免触发布局递归。</summary>
        private static void SetWrapWidth(Label label, int width)
        {
            if (label.MaximumSize.Width != width)
            {
                label.MaximumSize = new Size(width, 0);
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            UpdateWrapWidths();
        }

        // ---------------- 运行状态 ----------------

        private void OnShown(object sender, EventArgs e)
        {
            UpdateWrapWidths();
            AnnounceEnvironment();
            if (_noAutoStart)
            {
                _log.Info("已按 --no-auto-start 跳过自动启动，点击“启动”开始。");
                ApplyState(LauncherState.Idle, "已跳过自动启动；点击“启动”开始构建并拉起 Web 服务。");
                return;
            }

            StartPipeline(false);
        }

        private void AnnounceEnvironment()
        {
            _log.Info("DeepSeek Harness 启动器已启动。");
            _log.Info("界面主题：" + (_theme.Dark ? "深色" : "浅色") + "（theme = " + _config.Theme + "）");
            _log.Info("配置文件：" + (_config.ConfigPath == null ? "(未找到，使用默认值)" : _config.ConfigPath));
            _log.Info("日志文件：" + (_log.FilePath == null ? "(不可写)" : _log.FilePath));
            for (int i = 0; i < _config.Warnings.Count; i++)
            {
                _log.Warn(_config.Warnings[i]);
            }
        }

        /// <summary>
        /// 开始一次启动流程。<paramref name="interactive"/> 表示这次是用户点了「启动」，
        /// 目录不可用时可以弹窗；自动启动则只记录原因。
        /// </summary>
        private void StartPipeline(bool interactive)
        {
            if (!ApplyTarget(_config.HarnessDir, interactive))
            {
                _log.Warn("未选择可用的源码目录，已取消启动。");
                return;
            }

            RememberTarget();
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
            StartPipeline(true);
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
            if (_config.OpensInApp && _runner.LaunchAppInterface(_lastUrl))
            {
                return;
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

        // ---------------- 日志输出 ----------------

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
            _console.SelectionColor = _theme.LevelColor(level);
            _console.AppendText(message + Environment.NewLine);
            _console.SelectionColor = _console.ForeColor;
            _console.SelectionStart = _console.TextLength;
            _console.ScrollToCaret();
        }

        // ---------------- 状态展示 ----------------

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

            ApplyState(state, detail);
        }

        private void ApplyState(LauncherState state, string detail)
        {
            bool busy = state == LauncherState.Checking || state == LauncherState.Updating
                || state == LauncherState.Installing || state == LauncherState.Building
                || state == LauncherState.Starting || state == LauncherState.WaitingUser;

            _pill.Apply(StateText(state), StateDot(state), StateFill(state), _theme.PillText(state));
            _detailLabel.Text = detail;
            _metaLabel.Text = MetaText();
            _busyBar.SetActive(busy);

            bool running = _runner != null && _runner.IsBusy;
            _startButton.Enabled = !running;
            _stopButton.Enabled = running;
            _diagnoseButton.Enabled = !running && !_diagnosing;
            Text = "DeepSeek Harness 启动器 — " + StateText(state);
            UpdateWrapWidths();
        }


        /// <summary>仅供 --ui-check 使用：往日志区写入文字，返回写入后的长度。</summary>
        internal int SeedConsoleForCheck(string text)
        {
            _console.Text = text;
            return _console.TextLength;
        }

        /// <summary>仅供 --ui-check 使用：日志区当前文本长度。</summary>
        internal int ConsoleLength
        {
            get { return _console.TextLength; }
        }

        /// <summary>
        /// 仅供 --ui-check 使用：把界面文字替换为最长可能内容，
        /// 这样布局自检覆盖的是最坏情况而不是空标签。
        /// </summary>
        internal void LoadWorstCaseContent()
        {
            _lastUrl = "http://127.0.0.1:3080/?token=ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            _browseButton.Enabled = true;
            ApplyState(
                LauncherState.WaitingUser,
                "等待确认是否更新到 dsh-v0.1.5-rc.2：工作区有 12 项未提交修改，这段刻意写长的说明用于验证换行后不会被裁剪");
        }

        private void OnUrlDetected(string url)        {
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
            _metaLabel.Text = MetaText();
            UpdateWrapWidths();
        }

        /// <summary>标题下方的次要信息：源码目录、端口、更新检查、界面方式与地址。</summary>
        private string MetaText()
        {
            string text = "源码目录：" + _config.HarnessDir + (_config.IsDefaultHarnessDir ? "" : "（自定义）")
                + "   ·   端口：" + (_config.Port <= 0 ? "自动" : _config.Port.ToString(CultureInfo.InvariantCulture))
                + "   ·   更新检查：" + (_config.CheckUpdates ? "开" : "关")
                + "   ·   界面：" + OpenTargetText()
                + "   ·   日志：" + (_log.FilePath == null ? "不可写" : _log.FilePath);
            if (!string.IsNullOrEmpty(_lastUrl))
            {
                text += "   ·   " + _lastUrl;
            }

            return text;
        }

        private string OpenTargetText()
        {
            if (_config.OpensInApp)
            {
                return "应用";
            }

            return _config.OpensInBrowser ? "浏览器" : "不打开";
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

        private Color StateDot(LauncherState state)
        {
            switch (state)
            {
                case LauncherState.Running:
                    return _theme.Success;
                case LauncherState.Failed:
                    return _theme.Error;
                case LauncherState.Stopped:
                    return _theme.TextTertiary;
                case LauncherState.Idle:
                    return _theme.TextTertiary;
                default:
                    return _theme.Accent;
            }
        }

        private Color StateFill(LauncherState state)
        {
            switch (state)
            {
                case LauncherState.Running:
                    return _theme.PillSuccessFill;
                case LauncherState.Failed:
                    return _theme.PillErrorFill;
                case LauncherState.Stopped:
                case LauncherState.Idle:
                    return _theme.PillNeutralFill;
                default:
                    return _theme.PillInfoFill;
            }
        }


        // ---------------- 提示框 ----------------

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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Shown -= OnShown;
                _log.Entry -= OnLogEntry;
            }

            base.Dispose(disposing);
        }
    }
}
