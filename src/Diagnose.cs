using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace DshLauncher
{
    /// <summary>
    /// 只读诊断：检查配置、外部工具、本地仓库状态、远端 tag 判定与端口占用，
    /// 并打印接下来会执行的命令。全过程不执行 fetch / checkout / install / build，
    /// 也不改写源码目录中的任何文件。
    /// </summary>
    internal static class Diagnose
    {
        /// <summary>
        /// 执行诊断。返回 0 表示环境可启动，2 表示存在阻断项。
        /// <paramref name="writeReport"/> 为 true 时另外写出 logs\diagnose-report.txt。
        /// </summary>
        internal static int Run(AppConfig config, Log log, bool writeReport)
        {
            StringBuilder report = new StringBuilder();
            int problems = 0;

            Action<string> emit = delegate(string text)
            {
                report.AppendLine(text);
            };

            log.Info("=== 1/7 配置 ===");
            string line;
            line = "配置文件：" + (config.ConfigPath == null ? "(未找到，使用内置默认值)" : config.ConfigPath);
            log.Info(line); emit(line);
            line = "启动器根目录：" + config.LauncherRoot;
            log.Info(line); emit(line);
            line = "源码目录：" + config.HarnessDir + "（" + (Directory.Exists(config.HarnessDir) ? "存在" : "不存在") + "）";
            if (Directory.Exists(config.HarnessDir))
            {
                log.Info(line);
            }
            else
            {
                log.Error(line);
                problems++;
            }

            emit(line);
            line = "远端：" + config.RemoteName + " / " + config.RemoteUrl;
            log.Info(line); emit(line);
            line = "tag 前缀：" + config.TagPrefix;
            log.Info(line); emit(line);
            line = string.Format(
                CultureInfo.InvariantCulture,
                "开关：checkUpdates={0} installAfterUpdate={1} buildWhenUpToDate={2} buildWhenCheckFailed={3} gitSslFallback={4} closeStopsService={5} echoOutput={6}",
                config.CheckUpdates,
                config.InstallAfterUpdate,
                config.BuildWhenUpToDate,
                config.BuildWhenCheckFailed,
                config.GitSslFallback,
                config.CloseStopsService,
                config.EchoOutput);
            log.Info(line); emit(line);

            line = "界面打开方式：" + config.OpenTarget;
            log.Info(line); emit(line);
            if (config.OpensInApp)
            {
                string resolved = config.ResolveAppCommand("http://127.0.0.1:<端口>/?token=<启动令牌>");
                line = "界面应用命令：" + resolved;
                log.Info(line); emit(line);
                string appFile;
                string appArgs;
                AppConfig.SplitCommand(resolved, out appFile, out appArgs);
                if (appFile.IndexOf('\\') >= 0 || appFile.IndexOf('/') >= 0)
                {
                    if (File.Exists(appFile))
                    {
                        line = "界面应用可执行文件存在：" + appFile;
                        log.Info(line); emit(line);
                    }
                    else
                    {
                        line = "界面应用可执行文件不存在：" + appFile;
                        log.Error(line); emit(line);
                        problems++;
                    }
                }
            }
            else if (!config.OpensInBrowser)
            {
                line = "不会自动打开界面，仅在启动器窗口显示地址。";
                log.Info(line); emit(line);
            }

            if (config.CloseAppOnExit)
            {
                line = "关闭启动器时会一并关闭界面应用窗口"
                    + (string.IsNullOrEmpty(config.AppWindowTitle)
                        ? "（只关闭本次启动新打开的那个窗口）"
                        : "（按标题关键字匹配：" + config.AppWindowTitle + "）");
                log.Info(line); emit(line);

                if (!string.IsNullOrEmpty(config.AppWindowTitle))
                {
                    WindowInfo existing = WindowCloser.FindAppWindowByTitle(config.AppWindowTitle);
                    line = existing == null
                        ? "当前没有标题匹配的界面窗口。"
                        : "当前已存在标题匹配的界面窗口：" + WindowCloser.NormalizeTitle(existing.Title);
                    log.Info(line); emit(line);
                }
            }
            else
            {
                line = "关闭启动器时不会关闭界面应用窗口。";
                log.Info(line); emit(line);
            }

            for (int i = 0; i < config.Warnings.Count; i++)
            {
                log.Warn(config.Warnings[i]);
                emit("警告：" + config.Warnings[i]);
            }

            log.Blank();
            log.Info("=== 2/7 外部工具 ===");
            problems += ReportTool("git", log, emit);
            problems += ReportTool("node", log, emit);
            problems += ReportTool("pnpm", log, emit);

            log.Blank();
            log.Info("=== 3/7 本地仓库 ===");
            GitRepository repository = new GitRepository(config, log, CancellationToken.None);
            RepoState state = repository.ReadState();
            if (!state.IsRepository)
            {
                line = "源码目录不是 git 仓库，将跳过 tag 检查与更新。";
                log.Warn(line); emit(line);
            }
            else
            {
                line = "分支：" + (state.Branch == null ? "(未知)" : state.Branch)
                    + "  提交：" + Short(state.HeadCommit);
                log.Info(line); emit(line);
                line = "当前 tag（git describe）：" + (state.CurrentTag == null ? "(不在 tag 上)" : state.CurrentTag);
                log.Info(line); emit(line);
                line = "本地版本 tag 数：" + state.LocalTags.Count.ToString(CultureInfo.InvariantCulture);
                log.Info(line); emit(line);
                string latestLocalTag;
                TagVersion latestLocal = TagVersion.Latest(state.LocalTags, config.TagPrefix, out latestLocalTag);
                line = "本地最新版本 tag：" + (latestLocal == null ? "(无)" : latestLocalTag + " → " + Short(CommitOf(state, latestLocalTag)));
                log.Info(line); emit(line);
                line = "未提交修改：" + state.DirtyCount.ToString(CultureInfo.InvariantCulture) + " 项";
                if (state.IsDirty)
                {
                    log.Warn(line);
                }
                else
                {
                    log.Info(line);
                }

                emit(line);
            }

            log.Blank();
            log.Info("=== 4/7 远端 tag 与更新判定 ===");
            UpdateDecision decision = null;
            if (!config.CheckUpdates)
            {
                line = "配置中 checkUpdates = false，启动时不会查询远端。";
                log.Info(line); emit(line);
            }
            else if (!state.IsRepository)
            {
                line = "非 git 仓库，跳过查询。";
                log.Warn(line); emit(line);
            }
            else
            {
                string error;
                List<RemoteTag> remoteTags = repository.ListRemoteTags(out error);
                decision = UpdatePolicy.Evaluate(state, remoteTags, config.TagPrefix, error);
                if (!decision.CheckPerformed)
                {
                    line = "无法获取远端 tag：" + (decision.CheckError == null ? decision.Reason : decision.CheckError);
                    log.Error(line); emit(line);
                    problems++;
                }
                else
                {
                    line = "远端 tag 数：" + decision.RemoteTagCount.ToString(CultureInfo.InvariantCulture);
                    log.Info(line); emit(line);
                    line = "远端最新版本 tag：" + (decision.LatestRemote == null
                        ? "(无)"
                        : decision.LatestRemote.Name + " → " + Short(decision.LatestRemote.Commit));
                    log.Info(line); emit(line);
                    line = "判定：" + (decision.Available ? "有新 tag，启动时会提示更新" : "无新 tag") + "（" + decision.Reason + "）";
                    if (decision.Available)
                    {
                        log.Warn(line);
                    }
                    else
                    {
                        log.Success(line);
                    }

                    emit(line);
                }
            }

            log.Blank();
            log.Info("=== 5/7 端口 ===");
            if (config.Port <= 0)
            {
                line = "端口配置为 0（自动分配），跳过检查。";
                log.Info(line); emit(line);
            }
            else
            {
                bool busy = NetUtil.IsPortListening(config.Port);
                int free = busy ? NetUtil.FindFreePort(config.Port + 1) : config.Port;
                line = "端口 " + config.Port.ToString(CultureInfo.InvariantCulture)
                    + (busy ? " 已被占用" : " 空闲");
                if (busy)
                {
                    log.Warn(line);
                }
                else
                {
                    log.Info(line);
                }

                emit(line);
                line = busy
                    ? "启动时会询问是否改用空闲端口 " + free.ToString(CultureInfo.InvariantCulture) + "。"
                    : "启动时使用该端口。";
                log.Info(line); emit(line);
            }

            log.Blank();
            log.Info("=== 6/7 构建判定 ===");
            bool artifactsCurrent = false;
            string artifactsDetail = null;
            if (state.IsRepository && !state.IsDirty && (decision == null || !decision.Available))
            {
                artifactsCurrent = BuildState.ArtifactsCurrent(config, state, out artifactsDetail);
            }
            else if (state.IsRepository && state.IsDirty)
            {
                artifactsDetail = "工作区有未提交修改";
            }

            line = "构建产物判定：" + (artifactsCurrent ? "有效（" + artifactsDetail + "）" : "需要构建（" + (artifactsDetail == null ? "未检查" : artifactsDetail) + "）");
            log.Info(line); emit(line);

            BuildVerdict verdict = BuildPolicy.Decide(
                config.BuildWhenUpToDate,
                state,
                decision,
                config.BuildWhenCheckFailed,
                artifactsCurrent,
                artifactsDetail);
            line = "启动时将" + (verdict.Build ? "执行 pnpm run build" : "跳过 pnpm run build，直接 pnpm dsh web")
                + "（" + verdict.Reason + "）";
            if (verdict.Build)
            {
                log.Info(line);
            }
            else
            {
                log.Success(line);
            }

            emit(line);

            log.Blank();
            log.Info("=== 7/7 计划执行的命令（诊断不会执行）===");
            if (verdict.Build)
            {
                EmitCommand(config.BuildCommandLine(), config, log, emit);
            }
            else
            {
                line = "  （跳过 pnpm run build）";
                log.Info(line); emit(line);
            }

            if (decision != null && decision.Available)
            {
                EmitCommand("git fetch " + config.RemoteName + " --tags --force", config, log, emit);
                EmitCommand("git -c advice.detachedHead=false checkout --detach refs/tags/" + decision.LatestRemote.Name, config, log, emit);
                if (config.InstallAfterUpdate)
                {
                    EmitCommand(config.InstallCommandLine(), config, log, emit);
                }

                if (verdict.Build)
                {
                    EmitCommand(config.BuildCommandLine(), config, log, emit);
                }
            }

            EmitCommand(config.WebCommandLine(config.Port), config, log, emit);

            if (writeReport)
            {
                string path = null;
                try
                {
                    Directory.CreateDirectory(config.LogDirectory);
                    path = Path.Combine(config.LogDirectory, "diagnose-report.txt");
                    StringBuilder header = new StringBuilder();
                    header.AppendLine("DeepSeek Harness 启动器 - 只读诊断报告");
                    header.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    header.AppendLine("说明：本报告由 --diagnose 生成，未执行 fetch / checkout / install / build。");
                    header.AppendLine();
                    header.Append(report);
                    File.WriteAllText(path, header.ToString(), new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    log.Warn("无法写入诊断报告：" + ex.Message);
                    path = null;
                }

                if (path != null)
                {
                    log.Info("诊断报告：" + path);
                    ConsoleBridge.WriteLine("诊断报告：" + path);
                }
            }

            log.Blank();
            if (problems == 0)
            {
                log.Success("诊断完成：环境检查通过，可以启动。");
                ConsoleBridge.WriteLine("诊断完成：环境检查通过，可以启动。");
                return 0;
            }

            log.Error(string.Format(CultureInfo.InvariantCulture, "诊断完成：发现 {0} 个阻断项。", problems));
            ConsoleBridge.WriteLine(string.Format(CultureInfo.InvariantCulture, "诊断完成：发现 {0} 个阻断项。", problems));
            return 2;
        }

        private static int ReportTool(string name, Log log, Action<string> emit)
        {
            List<string> found = ToolLocator.Find(name);
            if (found.Count == 0)
            {
                string missing = "未找到命令：" + name;
                log.Error(missing);
                emit(missing);
                return 1;
            }

            log.Info(name + " → " + string.Join(" | ", found.ToArray()));
            emit(name + " → " + string.Join(" | ", found.ToArray()));

            ProcessResult version = ProcessRunner.Run(
                new ProcessSpec { Command = name + " --version", TimeoutMs = 30000, Echo = false },
                null,
                CancellationToken.None);
            string text = version.Started && version.Lines.Count > 0
                ? version.Lines[0].Trim()
                : "(版本获取失败：" + (version.StartError == null ? "退出码 " + version.ExitCode.ToString(CultureInfo.InvariantCulture) : version.StartError) + ")";
            log.Info("   版本：" + text);
            emit("   版本：" + text);
            return version.Started && version.ExitCode == 0 ? 0 : 1;
        }

        private static void EmitCommand(string commandLine, AppConfig config, Log log, Action<string> emit)
        {
            string full = "cd \"" + config.HarnessDir + "\" && " + commandLine;
            log.Command(full);
            emit("  " + full);
        }

        private static string CommitOf(RepoState state, string tag)
        {
            if (tag == null || !state.LocalTagCommits.ContainsKey(tag))
            {
                return null;
            }

            return state.LocalTagCommits[tag];
        }

        private static string Short(string commit)
        {
            if (string.IsNullOrEmpty(commit))
            {
                return "(未知)";
            }

            return commit.Length > 8 ? commit.Substring(0, 8) : commit;
        }
    }
}
