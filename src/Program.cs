using System;
using System.Threading;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>启动器入口：默认打开窗口，也支持只读诊断与自检两个命令行模式。</summary>
    internal static class Program
    {
        private const string MutexName = "Local\\DeepSeekHarnessLauncher.SingleInstance";

        [STAThread]
        private static int Main(string[] args)
        {
            CommandLine commandLine = CommandLine.Parse(args);
            if (commandLine.ShowHelp)
            {
                ConsoleBridge.Init();
                ConsoleBridge.Write(HelpText());
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            AppConfig config = AppConfig.Load(exeDirectory, commandLine.ConfigPath);
            Log log = new Log(config.LogDirectory, commandLine.Console);

            if (!string.IsNullOrEmpty(commandLine.HarnessPath))
            {
                string detail;
                TargetVerdict verdict = HarnessTargets.Validate(commandLine.HarnessPath, out detail);
                if (verdict == TargetVerdict.Missing)
                {
                    ConsoleBridge.Init();
                    ConsoleBridge.WriteLine("--harness " + detail);
                    log.Error("--harness " + detail);
                    return 2;
                }

                if (verdict == TargetVerdict.NotHarness)
                {
                    log.Warn("--harness " + detail + "（仍然使用它）");
                }

                config.SetHarnessDir(commandLine.HarnessPath);
                log.Info("--harness 指定源码目录：" + config.HarnessDir);
            }

            if (commandLine.SelfTest)
            {
                ConsoleBridge.Init();
                return SelfTest.Run(log);
            }

            if (commandLine.Diagnose)
            {
                ConsoleBridge.Init();
                return Diagnose.Run(config, log, true);
            }

            if (commandLine.UiCheck)
            {
                ConsoleBridge.Init();
                return UiCheck.Run(config, log);
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew && !commandLine.AllowMultiple)
                {
                    MessageBox.Show(
                        "DeepSeek Harness 启动器已经在运行。" + Environment.NewLine
                        + "请使用已打开的窗口，或先关闭它再重新启动。" + Environment.NewLine
                        + "（如需同时运行多个实例，可加 --allow-multiple。）",
                        "DeepSeek Harness 启动器",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return 1;
                }

                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    log.Error("界面线程异常：" + e.Exception);
                    MessageBox.Show(
                        "启动器遇到未处理的错误：" + Environment.NewLine + e.Exception.Message + Environment.NewLine
                        + Environment.NewLine + "详情见日志：" + log.FilePath,
                        "启动器错误",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                };

                try
                {
                    Application.Run(new LauncherForm(
                        config,
                        log,
                        commandLine.NoAutoStart,
                        commandLine.AssumeYes,
                        commandLine.ForceBuild));
                }
                finally
                {
                    GC.KeepAlive(mutex);
                }
            }

            return 0;
        }

        /// <summary>解析启动器自身的命令行参数。</summary>
        internal sealed class CommandLine
        {
            internal bool Diagnose;
            internal bool SelfTest;
            internal bool ShowHelp;
            internal bool Console;
            internal bool NoAutoStart;
            internal bool AssumeYes;
            internal bool ForceBuild;
            internal bool AllowMultiple;
            internal bool UiCheck;
            internal string HarnessPath;
            internal string ConfigPath;

            internal static CommandLine Parse(string[] args)
            {
                CommandLine parsed = new CommandLine();
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (string.Equals(arg, "--diagnose", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(arg, "--check", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed.Diagnose = true;
                    }
                    else if (string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed.SelfTest = true;
                    }
                    else if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed.ShowHelp = true;
                    }
                    else if (string.Equals(arg, "--console", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed.Console = true;
                    }
                    else if (string.Equals(arg, "--no-auto-start", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed.NoAutoStart = true;
                    }
                    else if (string.Equals(arg, "--yes", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(arg, "-y", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed.AssumeYes = true;
                    }
                    else if (string.Equals(arg, "--force-build", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed.ForceBuild = true;
                    }
                    else if (string.Equals(arg, "--allow-multiple", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed.AllowMultiple = true;
                    }
                    else if (string.Equals(arg, "--ui-check", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed.UiCheck = true;
                    }
                    else if (string.Equals(arg, "--harness", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        parsed.HarnessPath = args[++i];
                    }
                    else if (string.Equals(arg, "--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        parsed.ConfigPath = args[++i];
                    }
                }

                // 诊断与自检是命令行模式：默认把日志同时回显到控制台。
                if (parsed.Diagnose || parsed.SelfTest || parsed.UiCheck)
                {
                    parsed.Console = true;
                }

                return parsed;
            }
        }

        private static string HelpText()
        {
            return "DeepSeek Harness 启动器" + Environment.NewLine
                + Environment.NewLine
                + "用法：" + Environment.NewLine
                + "  DeepSeekHarnessLauncher.exe                打开启动器窗口并自动开始启动流程" + Environment.NewLine
                + "  DeepSeekHarnessLauncher.exe --no-auto-start 只打开窗口，不自动开始（便于先看配置）" + Environment.NewLine
                + "  DeepSeekHarnessLauncher.exe --yes           无人值守：自动确认更新与端口提示" + Environment.NewLine
                + "  DeepSeekHarnessLauncher.exe --force-build   本次强制执行 pnpm run build" + Environment.NewLine
                + "  DeepSeekHarnessLauncher.exe --allow-multiple 允许与已运行的启动器实例并存" + Environment.NewLine
                + "  DeepSeekHarnessLauncher.exe --diagnose     只读环境诊断（不改动源码目录）" + Environment.NewLine
                + "  DeepSeekHarnessLauncher.exe --self-test     运行内置逻辑自检" + Environment.NewLine
                + "  DeepSeekHarnessLauncher.exe --ui-check      界面布局自检（不显示窗口、不启动流程）" + Environment.NewLine
                + "  DeepSeekHarnessLauncher.exe --harness <路径> 指定本次使用的源码目录（默认用配置里的）" + Environment.NewLine
                + "  DeepSeekHarnessLauncher.exe --config <路径> 指定 launcher.config.ini" + Environment.NewLine
                + Environment.NewLine
                + "启动流程：检查远端 tag → 提示是否更新 → pnpm run build（已是最新且已有产物时跳过）→ pnpm dsh web" + Environment.NewLine;
        }
    }
}
