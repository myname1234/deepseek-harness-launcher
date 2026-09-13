using System;
using System.Collections.Generic;
using System.Globalization;

namespace DshLauncher
{
    /// <summary>
    /// 内置自检：覆盖版本比较、远端 tag 解析、更新判定与配置解析这几处最容易出错、
    /// 又不需要网络和真实仓库的逻辑。用 --self-test 运行。
    /// </summary>
    internal static class SelfTest
    {
        private static int _passed;
        private static int _failed;
        private static Log _log;

        internal static int Run(Log log)
        {
            _passed = 0;
            _failed = 0;
            _log = log;

            log.Info("=== 自检：版本比较 ===");
            Compare("dsh-v0.1.5-rc.2", ">", "dsh-v0.1.5-rc.1");
            Compare("dsh-v0.1.5", ">", "dsh-v0.1.5-rc.2");
            Compare("dsh-v0.1.5-rc.1", ">", "dsh-v0.1.5-alpha.2");
            Compare("dsh-v0.1.10", ">", "dsh-v0.1.9");
            Compare("dsh-v0.2.0", ">", "dsh-v0.1.99");
            Compare("dsh-v0.1.5-alpha.10", ">", "dsh-v0.1.5-alpha.2");
            Compare("dsh-v1.0.0", "=", "dsh-v1.0.0+build.7");
            Compare("dsh-v0.1.5-rc.2", "=", "dsh-v0.1.5-rc.2");

            log.Info("=== 自检：tag 解析 ===");
            Check("非法版本被拒绝", TagVersion.Parse("dsh-v0.1", "dsh-v") == null);
            Check("前缀不匹配被拒绝", TagVersion.Parse("vendor-v1.0.0", "dsh-v") == null);
            Check("非版本 tag 被拒绝", TagVersion.Parse("dsh-latest", "dsh-v") == null);
            Check("构建元数据不影响优先级",
                TagVersion.Parse("dsh-v1.2.3+abc", "dsh-v").CompareTo(TagVersion.Parse("dsh-v1.2.3", "dsh-v")) == 0);
            string latestTag;
            TagVersion latest = TagVersion.Latest(
                new string[] { "dsh-v0.1.5-rc.1", "dsh-v0.1.3-alpha.2", "dsh-v0.1.5-rc.2", "junk" },
                "dsh-v",
                out latestTag);
            Check("Latest 选出最高版本", latest != null && latestTag == "dsh-v0.1.5-rc.2");

            log.Info("=== 自检：git 引用行解析 ===");
            List<string> refs = new List<string>();
            refs.Add("1111111111111111111111111111111111111111\trefs/tags/dsh-v0.1.5-rc.2");
            refs.Add("2222222222222222222222222222222222222222\trefs/tags/dsh-v0.1.5-rc.2^{}");
            refs.Add("3333333333333333333333333333333333333333\trefs/tags/dsh-v0.1.5-rc.1");
            refs.Add("4444444444444444444444444444444444444444\trefs/heads/master");
            List<RemoteTag> parsed = GitRepository.ParseRefLines(refs, "refs/tags/");
            Check("只保留 tag 引用", parsed.Count == 2);
            Check("注解 tag 采用 ^{} 行的提交",
                parsed.Count == 2
                && parsed[0].Name == "dsh-v0.1.5-rc.2"
                && parsed[0].Commit == "2222222222222222222222222222222222222222");
            Check("轻量 tag 采用自身提交",
                parsed.Count == 2 && parsed[1].Name == "dsh-v0.1.5-rc.1"
                && parsed[1].Commit == "3333333333333333333333333333333333333333");

            log.Info("=== 自检：更新判定 ===");
            Check("非仓库不触发更新", !Evaluate(true, false, "dsh-v0.1.5-rc.2", "dsh-v0.1.5-rc.2", "aaa", "aaa").Available);
            Check("远端查询失败不触发更新",
                !Evaluate(false, true, null, "dsh-v0.1.5-rc.2", "aaa", "aaa").CheckPerformed);
            Check("本地同 tag 同提交：无更新",
                !Evaluate(true, true, "dsh-v0.1.5-rc.2", "dsh-v0.1.5-rc.2", "aaa", "aaa").Available);
            Check("远端出现新 tag：提示更新",
                Evaluate(true, true, "dsh-v0.1.5-rc.3", "dsh-v0.1.5-rc.2", null, "bbb").Available);
            Check("同名 tag 提交变化：提示更新",
                Evaluate(true, true, "dsh-v0.1.5-rc.2", "dsh-v0.1.5-rc.2", "aaa", "ccc").Available);
            Check("当前检出落后于远端最新：提示更新",
                Evaluate(true, true, "dsh-v0.1.5-rc.2", "dsh-v0.1.5-rc.1", "aaa", "bbb").Available);
            Check("master 领先 tag 时不再提示（本仓库现状）",
                !Evaluate(true, true, "dsh-v0.1.5-rc.2", "dsh-v0.1.5-rc.2", "aaa", "aaa").Available);
            Check("远端没有版本 tag 时不提示",
                !Evaluate(true, true, null, "dsh-v0.1.5-rc.2", "aaa", "aaa").Available);

            log.Info("=== 自检：构建判定 ===");
            Check("已是最新 + 干净 + 已有产物 → 跳过构建",
                !ShouldBuild(false, true, false, true, false, true, true));
            Check("已是最新 + 干净 + 无产物 → 构建",
                ShouldBuild(false, true, false, true, false, false, true));
            Check("已是最新 + 工作区脏 → 构建",
                ShouldBuild(false, true, true, true, false, true, true));
            Check("有可用更新 → 构建",
                ShouldBuild(false, true, false, true, true, true, true));
            Check("强制构建时即使已是最新也构建",
                ShouldBuild(true, true, false, true, false, true, true));
            Check("检查失败且配置为保守 → 构建",
                ShouldBuild(false, true, false, false, false, true, true));
            Check("检查失败且配置允许跳过 → 跳过",
                !ShouldBuild(false, true, false, false, false, true, false));
            Check("未开启检查（无判定结果）→ 按配置构建",
                BuildPolicy.Decide(false, MakeState(true, false), null, true, false, "测试").Build);
            Check("未开启检查且配置允许跳过 → 跳过",
                !BuildPolicy.Decide(false, MakeState(true, false), null, false, false, "测试").Build);

            log.Info("=== 自检：界面打开方式 ===");
            AppConfig browserConfig = new AppConfig();
            Check("默认由 dsh 打开浏览器", browserConfig.OpensInBrowser && !browserConfig.OpensInApp);
            Check("默认 web 命令不带 --no-open", browserConfig.WebCommandLine(3080) == "pnpm dsh web --port 3080");

            AppConfig appConfig = new AppConfig();
            appConfig.ApplyIni(
                "openTarget = app\r\n"
                + "appCommand = \"C:\\Program Files\\Edge\\msedge.exe\" --app=\"{url}\" --profile-directory=Default\r\n");
            Check("openTarget = app 生效", appConfig.OpensInApp && !appConfig.OpensInBrowser);
            Check("app 模式 web 命令带 --no-open",
                appConfig.WebCommandLine(3080) == "pnpm dsh web --port 3080 --no-open");
            Check("{url} 被替换成带 token 的地址",
                appConfig.ResolveAppCommand("http://127.0.0.1:3080/?token=abc")
                == "\"C:\\Program Files\\Edge\\msedge.exe\" --app=\"http://127.0.0.1:3080/?token=abc\" --profile-directory=Default");
            string appFile;
            string appArgs;
            AppConfig.SplitCommand(appConfig.ResolveAppCommand("U"), out appFile, out appArgs);
            Check("命令行按引号拆分可执行文件", appFile == "C:\\Program Files\\Edge\\msedge.exe");
            Check("命令行参数拆分正确", appArgs == "--app=\"U\" --profile-directory=Default");

            AppConfig noPlaceholder = new AppConfig();
            noPlaceholder.ApplyIni("openTarget = app\r\nappCommand = \"C:\\apps\\DeepSeek Harness.lnk\"\r\n");
            Check("无占位符时原样执行（可指向快捷方式）",
                noPlaceholder.ResolveAppCommand("http://x/") == "\"C:\\apps\\DeepSeek Harness.lnk\"");
            AppConfig.SplitCommand(noPlaceholder.ResolveAppCommand("http://x/"), out appFile, out appArgs);
            Check("快捷方式路径拆分正确", appFile == "C:\\apps\\DeepSeek Harness.lnk" && appArgs.Length == 0);

            AppConfig noneConfig = new AppConfig();
            noneConfig.ApplyIni("openTarget = none\r\n");
            Check("openTarget = none 不打开界面", !noneConfig.OpensInBrowser && !noneConfig.OpensInApp);
            Check("none 模式 web 命令带 --no-open",
                noneConfig.WebCommandLine(3080) == "pnpm dsh web --port 3080 --no-open");

            AppConfig badTarget = new AppConfig();
            badTarget.ApplyIni("openTarget = app\r\n");
            badTarget.Validate();
            Check("app 模式缺少 appCommand 时回退并告警",
                badTarget.OpensInBrowser && badTarget.Warnings.Count == 1);

            log.Info("=== 自检：界面窗口识别 ===");
            Check("默认开启关闭界面应用", new AppConfig().CloseAppOnExit);
            Check("去掉标题里的零宽字符",
                WindowCloser.NormalizeTitle("DSH\u200B Local Build") == "DSH Local Build");
            Check("浏览器主窗口标题被识别（Edge，含零宽字符）",
                WindowCloser.IsBrowserMainWindowTitle("创建 X — DSH 本地构建 和另外 11 个页面 - 个人 - Microsoft\u200B Edge"));
            Check("浏览器主窗口标题被识别（Chrome）",
                WindowCloser.IsBrowserMainWindowTitle("DeepSeek Harness - Google Chrome"));
            Check("应用窗口标题不会被误判为浏览器主窗口",
                !WindowCloser.IsBrowserMainWindowTitle("创建 X — DSH 本地构建"));
            Check("标题关键字匹配（中文标题）",
                WindowCloser.MatchesAppWindowTitle("创建 X — DSH 本地构建", "DSH 本地构建"));
            Check("标题关键字忽略大小写",
                WindowCloser.MatchesAppWindowTitle("DSH Local Build", "dsh local build"));
            Check("关键字为空时不匹配任何标题",
                !WindowCloser.MatchesAppWindowTitle("DSH Local Build", string.Empty));

            log.Info("=== 自检：目标源码目录 ===");
            string verdictDetail;
            Check("不存在的目录判为不可用",
                HarnessTargets.Validate(@"Z:\definitely\missing\dsh", out verdictDetail) == TargetVerdict.Missing);
            Check("空路径判为不可用",
                HarnessTargets.Validate("   ", out verdictDetail) == TargetVerdict.Missing);
            Check("非 harness 目录需要确认",
                HarnessTargets.Validate(System.IO.Path.GetTempPath(), out verdictDetail) != TargetVerdict.Ok);
            System.Collections.Generic.List<string> recent = HarnessTargets.ParseRecent(@"C:\a\dsh|D:\b\dsh|C:\a\dsh||");
            Check("最近使用解析去重且保持顺序",
                recent.Count == 2 && recent[0] == "C:\\a\\dsh" && recent[1] == "D:\\b\\dsh");
            Check("最近使用可回写成标签",
                HarnessTargets.FormatRecent(recent) == "C:\\a\\dsh|D:\\b\\dsh");
            System.Collections.Generic.List<string> added = HarnessTargets.AddRecent(recent, @"E:\c\dsh", "C:\\a\\dsh");
            Check("新目标排在最前且不含默认目录",
                added.Count == 2 && added[0] == "E:\\c\\dsh" && added[1] == "D:\\b\\dsh");
            Check("重复选择同一目录不会产生重复项",
                HarnessTargets.AddRecent(added, @"E:\c\dsh", "C:\\a\\dsh").Count == 2);
            Check("过长的路径被压缩显示",
                HarnessTargets.Shorten(@"C:\very\long\path\to\a\harness\checkout\with\many\segments", 20).Length == 20);
            string tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-launcher-selftest-" + Guid.NewGuid().ToString("N"));
            AppConfig store = new AppConfig();
            store.LauncherRoot = tempRoot;
            store.HarnessDir = tempRoot;
            store.DefaultHarnessDir = tempRoot;
            HarnessTargets.SaveRecent(store, added);
            System.Collections.Generic.List<string> reloaded = HarnessTargets.LoadRecent(store);
            Check("最近使用可写入并原样读回",
                reloaded.Count == added.Count && reloaded[0] == added[0] && reloaded[1] == added[1]);
            try
            {
                System.IO.Directory.Delete(tempRoot, true);
            }
            catch (System.IO.IOException)
            {
                // 临时目录清理失败不影响结论。
            }
            Check("相对路径按启动器根目录解析",
                ResolveHarnessPath(@"C:\launcher", "..\\deepseek-harness") == "C:\\deepseek-harness");

            log.Info("=== 自检：配置解析 ===");
            AppConfig config = new AppConfig();
            config.LauncherRoot = "C:\\launcher";
            config.ApplyIni(
                "# 注释\r\n"
                + "harnessDir = ..\\deepseek-harness\r\n"
                + "port = 3200\r\n"
                + "checkUpdates = false\r\n"
                + "buildWhenUpToDate = true\r\n"
                + "buildWhenCheckFailed = false\r\n"
                + "webExtraArgs = --trusted-host 10.0.0.2:3080\r\n"
                + "未知项 = 1\r\n"
                + "port2\r\n");
            Check("读取字符串项", config.HarnessDir == "..\\deepseek-harness");
            Check("读取整数项", config.Port == 3200);
            Check("读取布尔项", config.CheckUpdates == false);
            Check("读取 buildWhenUpToDate", config.BuildWhenUpToDate);
            Check("读取 buildWhenCheckFailed", !config.BuildWhenCheckFailed);
            Check("读取含空格的参数", config.WebExtraArgs == "--trusted-host 10.0.0.2:3080");
            Check("默认跳过构建（已是最新时）", !new AppConfig().BuildWhenUpToDate);
            Check("默认检查失败时构建", new AppConfig().BuildWhenCheckFailed);
            Check("未知项被记录为警告", config.Warnings.Count == 2);
            Check("Web 命令拼接正确", config.WebCommandLine(3200) == "pnpm dsh web --port 3200 --trusted-host 10.0.0.2:3080");
            Check("自动端口不拼 --port", config.WebCommandLine(0) == "pnpm dsh web --trusted-host 10.0.0.2:3080");
            Check("构建命令拼接正确", new AppConfig().BuildCommandLine() == "pnpm run build");

            log.Blank();
            if (_failed == 0)
            {
                log.Success(string.Format(CultureInfo.InvariantCulture, "自检通过：{0} 项断言全部成立。", _passed));
                ConsoleBridge.WriteLine(string.Format(CultureInfo.InvariantCulture, "自检通过：{0} 项断言全部成立。", _passed));
                return 0;
            }

            log.Error(string.Format(CultureInfo.InvariantCulture, "自检失败：{0} 项通过，{1} 项不成立。", _passed, _failed));
            ConsoleBridge.WriteLine(string.Format(CultureInfo.InvariantCulture, "自检失败：{0} 项通过，{1} 项不成立。", _passed, _failed));
            return 1;
        }

        private static UpdateDecision Evaluate(
            bool isRepository,
            bool remoteAvailable,
            string remoteLatestTag,
            string currentTag,
            string localCommit,
            string remoteCommit)
        {
            RepoState state = new RepoState();
            state.IsRepository = isRepository;
            state.CurrentTag = currentTag;
            state.HeadCommit = "0000000000000000000000000000000000000000";
            if (currentTag != null)
            {
                state.LocalTags.Add(currentTag);
                if (localCommit != null)
                {
                    state.LocalTagCommits[currentTag] = localCommit;
                }
            }

            if (remoteLatestTag != null && currentTag != null && remoteLatestTag != currentTag)
            {
                // 远端版本更高时，本地没有该 tag；更低时它已存在于本地。
                if (TagVersion.Parse(remoteLatestTag, "dsh-v").CompareTo(TagVersion.Parse(currentTag, "dsh-v")) < 0)
                {
                    state.LocalTags.Add(remoteLatestTag);
                    state.LocalTagCommits[remoteLatestTag] = remoteCommit;
                }
            }

            List<RemoteTag> remoteTags = null;
            if (remoteAvailable)
            {
                remoteTags = new List<RemoteTag>();
                if (remoteLatestTag != null)
                {
                    RemoteTag tag = new RemoteTag();
                    tag.Name = remoteLatestTag;
                    tag.Commit = remoteCommit;
                    remoteTags.Add(tag);
                }
            }

            return UpdatePolicy.Evaluate(state, remoteTags, "dsh-v", remoteAvailable ? null : "模拟网络失败");
        }

        private static string ResolveHarnessPath(string launcherRoot, string relative)
        {
            AppConfig config = new AppConfig();
            config.LauncherRoot = launcherRoot;
            config.HarnessDir = "x";
            config.DefaultHarnessDir = "x";
            config.SetHarnessDir(relative);
            return config.HarnessDir;
        }

        private static RepoState MakeState(bool isRepository, bool dirty)
        {
            RepoState state = new RepoState();
            state.IsRepository = isRepository;
            state.HeadCommit = "0123456789abcdef0123456789abcdef01234567";
            state.Branch = "master";
            state.DirtyCount = dirty ? 1 : 0;
            return state;
        }

        private static bool ShouldBuild(
            bool force,
            bool isRepository,
            bool dirty,
            bool checkPerformed,
            bool available,
            bool artifactsCurrent,
            bool buildWhenCheckFailed)
        {
            UpdateDecision decision = new UpdateDecision();
            decision.CheckPerformed = checkPerformed;
            decision.Available = available;
            return BuildPolicy.Decide(
                force,
                MakeState(isRepository, dirty),
                decision,
                buildWhenCheckFailed,
                artifactsCurrent,
                "测试依据").Build;
        }

        private static void Compare(string left, string relation, string right)
        {
            TagVersion a = TagVersion.Parse(left, "dsh-v");
            TagVersion b = TagVersion.Parse(right, "dsh-v");
            if (a == null || b == null)
            {
                Check(left + " " + relation + " " + right, false);
                return;
            }

            int result = a.CompareTo(b);
            bool ok;
            if (relation == ">")
            {
                ok = result > 0;
            }
            else if (relation == "<")
            {
                ok = result < 0;
            }
            else
            {
                ok = result == 0;
            }

            Check(left + " " + relation + " " + right, ok);
        }

        private static void Check(string name, bool condition)
        {
            if (condition)
            {
                _passed++;
                return;
            }

            _failed++;
            if (_log != null)
            {
                _log.Error("断言不成立：" + name);
            }
        }
    }
}
