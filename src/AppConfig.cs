using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DshLauncher
{
    /// <summary>
    /// launcher.config.ini 的解析结果。除 LauncherRoot 外的相对路径都相对配置文件所在目录解析，
    /// 因此 dist\ 下的可执行文件与仓库根目录的配置文件可以共存。
    /// </summary>
    internal sealed class AppConfig
    {
        internal const string FileName = "launcher.config.ini";

        internal string ConfigPath;
        internal string LauncherRoot;

        /// <summary>本次启动实际使用的源码目录（界面里可切换）。</summary>
        internal string HarnessDir;

        /// <summary>配置解析出的默认源码目录，用于「恢复默认」。</summary>
        internal string DefaultHarnessDir;
        internal string RemoteName = "origin";
        internal string RemoteUrl = "https://github.com/deepseek-ai/deepseek-harness.git";
        internal string TagPrefix = "dsh-v";
        internal int Port = 3080;
        internal bool CheckUpdates = true;
        internal bool InstallAfterUpdate = true;
        internal bool BuildWhenUpToDate;
        internal bool BuildWhenCheckFailed = true;
        internal bool GitSslFallback = true;
        internal bool CloseStopsService = true;
        internal bool EchoOutput = true;
        internal string BuildCommand = "run build";
        internal string WebCommand = "dsh web";
        internal string WebExtraArgs = string.Empty;

        /// <summary>Web 服务就绪后用哪种方式打开界面：browser（dsh 自己开默认浏览器）、app（启动 appCommand）、none。</summary>
        internal string OpenTarget = "browser";

        /// <summary>openTarget = app 时执行的命令；出现 {url} 时替换为带 token 的地址。</summary>
        internal string AppCommand = string.Empty;

        /// <summary>启动器退出/停止时关闭由它打开的界面应用窗口。</summary>
        internal bool CloseAppOnExit = true;

        /// <summary>可选的界面窗口标题关键字；留空表示只关闭本次启动新打开的那个窗口。</summary>
        internal string AppWindowTitle = string.Empty;

        /// <summary>界面主题：auto（跟随 Windows 应用模式）/ dark / light。</summary>
        internal string Theme = "auto";

        internal readonly List<string> Warnings = new List<string>();

        /// <summary>是否交给 dsh 自己打开默认浏览器。</summary>
        internal bool OpensInBrowser
        {
            get { return !string.Equals(OpenTarget, "app", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(OpenTarget, "none", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>是否由启动器启动指定的应用/快捷方式。</summary>
        internal bool OpensInApp
        {
            get { return string.Equals(OpenTarget, "app", StringComparison.OrdinalIgnoreCase); }
        }

        internal string LogDirectory
        {
            get { return Path.Combine(LauncherRoot, "logs"); }
        }

        internal string StateDirectory
        {
            get { return Path.Combine(LauncherRoot, "state"); }
        }

        /// <summary>构建命令：固定使用 pnpm，只让配置决定其后的参数。</summary>
        internal string BuildCommandLine()
        {
            return "pnpm " + BuildCommand;
        }

        /// <summary>
        /// Web 命令：把配置端口与附加参数拼在 <c>pnpm dsh web</c> 之后。
        /// 由启动器负责打开界面时补上 --no-open，避免 dsh 再开一个默认浏览器标签页。
        /// </summary>
        internal string WebCommandLine(int port)
        {
            StringBuilder builder = new StringBuilder("pnpm ");
            builder.Append(WebCommand);
            if (port > 0)
            {
                builder.Append(" --port ").Append(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (!OpensInBrowser)
            {
                builder.Append(" --no-open");
            }

            if (!string.IsNullOrEmpty(WebExtraArgs))
            {
                builder.Append(' ').Append(WebExtraArgs);
            }

            return builder.ToString();
        }

        /// <summary>更新后按需重装依赖；--frozen-lockfile 保证不会改写 tag 里的 lockfile。</summary>
        internal string InstallCommandLine()
        {
            return "pnpm install --frozen-lockfile";
        }

        /// <summary>
        /// 把 appCommand 变成可直接执行的命令行：替换 {url} 占位符。
        /// 没有占位符时原样返回，便于直接打开已安装应用的快捷方式。
        /// </summary>
        internal string ResolveAppCommand(string url)
        {
            string command = AppCommand == null ? string.Empty : AppCommand.Trim();
            if (command.Length == 0)
            {
                return string.Empty;
            }

            if (command.IndexOf("{url}", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Replace(command, "{url}", url == null ? string.Empty : url);
            }

            return command;
        }

        private static string Replace(string text, string token, string value)
        {
            int index = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                text = text.Substring(0, index) + value + text.Substring(index + token.Length);
                index = text.IndexOf(token, index + value.Length, StringComparison.OrdinalIgnoreCase);
            }

            return text;
        }

        /// <summary>把命令行拆成可执行文件与参数两部分，支持双引号包裹的路径。</summary>
        internal static void SplitCommand(string commandLine, out string fileName, out string arguments)
        {
            fileName = string.Empty;
            arguments = string.Empty;
            if (commandLine == null)
            {
                return;
            }

            string text = commandLine.Trim();
            if (text.Length == 0)
            {
                return;
            }

            if (text[0] == '"')
            {
                int end = text.IndexOf('"', 1);
                if (end < 0)
                {
                    fileName = text.Substring(1);
                    return;
                }

                fileName = text.Substring(1, end - 1);
                arguments = text.Substring(end + 1).Trim();
                return;
            }

            int space = text.IndexOf(' ');
            if (space < 0)
            {
                fileName = text;
                return;
            }

            fileName = text.Substring(0, space);
            arguments = text.Substring(space + 1).Trim();
        }

        /// <summary>
        /// 从可执行文件目录向上查找配置文件并解析。显式传入的路径优先，找不到时使用内置默认值。
        /// </summary>
        internal static AppConfig Load(string exeDirectory, string explicitConfigPath)
        {
            AppConfig config = new AppConfig();
            string configPath = null;

            if (!string.IsNullOrEmpty(explicitConfigPath))
            {
                string candidate = Path.IsPathRooted(explicitConfigPath)
                    ? explicitConfigPath
                    : Path.Combine(exeDirectory, explicitConfigPath);
                if (File.Exists(candidate))
                {
                    configPath = Path.GetFullPath(candidate);
                }
                else
                {
                    config.Warnings.Add("指定的配置文件不存在，改用默认值：" + candidate);
                }
            }

            if (configPath == null)
            {
                DirectoryInfo dir = new DirectoryInfo(exeDirectory);
                for (int depth = 0; depth < 3 && dir != null; depth++)
                {
                    string candidate = Path.Combine(dir.FullName, FileName);
                    if (File.Exists(candidate))
                    {
                        configPath = candidate;
                        break;
                    }

                    dir = dir.Parent;
                }
            }

            if (configPath == null)
            {
                config.LauncherRoot = exeDirectory;
                config.Warnings.Add("未找到 " + FileName + "，使用内置默认配置。");
            }
            else
            {
                config.ConfigPath = configPath;
                config.LauncherRoot = Path.GetDirectoryName(configPath);
                config.ApplyIni(File.ReadAllText(configPath, new UTF8Encoding(false)));
            }

            config.ResolvePaths();
            config.Validate();
            return config;
        }

        /// <summary>解析 INI 文本；未知键记录为警告而非错误。</summary>
        internal void ApplyIni(string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[')
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    Warnings.Add(string.Format("第 {0} 行无法解析：{1}", i + 1, line));
                    continue;
                }

                string key = line.Substring(0, separator).Trim().ToLowerInvariant();
                string rawValue = line.Substring(separator + 1).Trim();
                string value = Unquote(rawValue);
                switch (key)
                {
                    case "harnessdir": HarnessDir = value; break;
                    case "remotename": RemoteName = value; break;
                    case "remoteurl": RemoteUrl = value; break;
                    case "tagprefix": TagPrefix = value; break;
                    case "port": Port = ParseInt(key, value); break;
                    case "checkupdates": CheckUpdates = ParseBool(key, value); break;
                    case "installafterupdate": InstallAfterUpdate = ParseBool(key, value); break;
                    case "buildwhenuptodate": BuildWhenUpToDate = ParseBool(key, value); break;
                    case "buildwhencheckfailed": BuildWhenCheckFailed = ParseBool(key, value); break;
                    case "gitsslfallback": GitSslFallback = ParseBool(key, value); break;
                    case "closestopsservice": CloseStopsService = ParseBool(key, value); break;
                    case "echooutput": EchoOutput = ParseBool(key, value); break;
                    case "buildcommand": BuildCommand = value; break;
                    case "webcommand": WebCommand = value; break;
                    case "webextraargs": WebExtraArgs = value; break;
                    case "opentarget": OpenTarget = ParseOpenTarget(value); break;
                    // appCommand 是命令行而不是单个值，路径里的引号必须保留。
                    case "appcommand": AppCommand = rawValue; break;
                    case "closeapponexit": CloseAppOnExit = ParseBool(key, value); break;
                    case "appwindowtitle": AppWindowTitle = value; break;
                    case "theme": Theme = ParseTheme(value); break;
                    default:
                        Warnings.Add(string.Format("第 {0} 行是未知配置项：{1}", i + 1, key));
                        break;
                }
            }
        }

        private void ResolvePaths()
        {
            if (string.IsNullOrEmpty(HarnessDir))
            {
                HarnessDir = "..\\deepseek-harness";
                Warnings.Add("未配置 harnessDir，使用默认值 " + HarnessDir);
            }

            HarnessDir = Path.IsPathRooted(HarnessDir)
                ? Path.GetFullPath(HarnessDir)
                : Path.GetFullPath(Path.Combine(LauncherRoot, HarnessDir));
            DefaultHarnessDir = HarnessDir;
        }

        /// <summary>
        /// 切换本次启动使用的源码目录（界面下拉框、浏览按钮或 --harness 使用）。
        /// 相对路径按启动器根目录解析；只影响本次运行，不写回配置文件。
        /// </summary>
        internal void SetHarnessDir(string path)
        {
            string value = path == null ? string.Empty : path.Trim().Trim('"');
            if (value.Length == 0)
            {
                return;
            }

            HarnessDir = Path.IsPathRooted(value)
                ? Path.GetFullPath(value)
                : Path.GetFullPath(Path.Combine(LauncherRoot, value));
        }

        /// <summary>当前目标是否就是配置里的默认目录。</summary>
        internal bool IsDefaultHarnessDir
        {
            get
            {
                return string.Equals(
                    HarnessTargets.Normalize(HarnessDir),
                    HarnessTargets.Normalize(DefaultHarnessDir),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        private int ParseInt(string key, string value)
        {
            int parsed;
            if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsed)
                && parsed >= 0 && parsed <= 65535)
            {
                return parsed;
            }

            Warnings.Add(string.Format("配置项 {0} 不是合法端口：{1}", key, value));
            return 0;
        }

        private string ParseTheme(string value)
        {
            string lowered = value.ToLowerInvariant();
            if (lowered == "auto" || lowered == "dark" || lowered == "light")
            {
                return lowered;
            }

            Warnings.Add(string.Format("配置项 theme 只能是 auto / dark / light：{0}", value));
            return "auto";
        }

        private string ParseOpenTarget(string value)        {
            string lowered = value.ToLowerInvariant();
            if (lowered == "browser" || lowered == "app" || lowered == "none")
            {
                return lowered;
            }

            Warnings.Add(string.Format("配置项 openTarget 只能是 browser / app / none：{0}", value));
            return "browser";
        }

        /// <summary>
        /// 解析完成后的整体校验：配置项书写顺序不该影响结果，因此跨键的检查放在这里。
        /// </summary>
        internal void Validate()
        {
            if (OpensInApp && string.IsNullOrEmpty(AppCommand))
            {
                Warnings.Add("openTarget = app 但没有配置 appCommand，仍会由 dsh 打开默认浏览器。");
                OpenTarget = "browser";
            }
        }

        private bool ParseBool(string key, string value)
        {
            string lowered = value.ToLowerInvariant();
            if (lowered == "true" || lowered == "yes" || lowered == "1" || lowered == "on")
            {
                return true;
            }

            if (lowered == "false" || lowered == "no" || lowered == "0" || lowered == "off")
            {
                return false;
            }

            Warnings.Add(string.Format("配置项 {0} 不是合法布尔值：{1}", key, value));
            return false;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }
    }
}
