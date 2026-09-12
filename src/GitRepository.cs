using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DshLauncher
{
    /// <summary>定位外部工具，仅用于诊断与日志展示。</summary>
    internal static class ToolLocator
    {
        internal static List<string> Find(string name)
        {
            List<string> found = new List<string>();
            ProcessResult result = ProcessRunner.Run(
                new ProcessSpec { Command = "where " + name, TimeoutMs = 20000, Echo = false },
                null,
                CancellationToken.None);

            if (result.Started && result.ExitCode == 0)
            {
                for (int i = 0; i < result.Lines.Count; i++)
                {
                    string line = result.Lines[i].Trim();
                    if (line.Length > 0)
                    {
                        found.Add(line);
                    }
                }
            }

            return found;
        }
    }

    /// <summary>
    /// deepseek-harness 仓库的只读查询与 tag 更新。所有 git 调用都通过 -C 指定仓库目录，
    /// 不改动启动器的工作目录。首次失败且开启回退时，用 openssl 证书后端重试一次
    /// （部分 Windows 环境的 schannel 无法完成 TLS 握手）。
    /// </summary>
    internal sealed class GitRepository
    {
        private const int DefaultTimeoutMs = 120000;
        private const int NetworkTimeoutMs = 180000;

        private static readonly Regex TagNamePattern = new Regex("\"name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex ShaPattern = new Regex("\"sha\"\\s*:\\s*\"([0-9a-fA-F]{40})\"", RegexOptions.Compiled);

        private readonly AppConfig _config;
        private readonly Log _log;
        private readonly CancellationToken _token;

        internal GitRepository(AppConfig config, Log log, CancellationToken token)
        {
            _config = config;
            _log = log;
            _token = token;
        }

        /// <summary>执行一条 git 子命令（参数不含 git 自身）。</summary>
        internal ProcessResult Run(string arguments, int timeoutMs, bool echoCommand)
        {
            string commandLine = "git " + arguments;
            if (echoCommand)
            {
                _log.Command(commandLine);
            }

            ProcessSpec spec = new ProcessSpec();
            spec.Command = commandLine;
            spec.WorkingDirectory = _config.HarnessDir;
            spec.TimeoutMs = timeoutMs;
            spec.Echo = _config.EchoOutput;
            return ProcessRunner.Run(spec, null, _token);
        }

        /// <summary>需要访问远端的 git 命令：先按默认证书后端执行，失败后按配置回退到 openssl。</summary>
        internal ProcessResult RunNetwork(string arguments, int timeoutMs)
        {
            ProcessResult result = Run(arguments, timeoutMs, true);
            if (result.Started && result.ExitCode == 0)
            {
                return result;
            }

            if (!_config.GitSslFallback || _token.IsCancellationRequested)
            {
                return result;
            }

            _log.Warn("git 访问失败，改用 openssl 证书后端重试。");
            ProcessResult retry = Run("-c http.sslBackend=openssl " + arguments, timeoutMs, true);
            if (retry.Started && retry.ExitCode == 0)
            {
                return retry;
            }

            return retry;
        }

        private string RepoArguments(string arguments)
        {
            return "-C \"" + _config.HarnessDir + "\" " + arguments;
        }

        /// <summary>读取本地仓库状态；目录不存在或不是仓库时返回 IsRepository=false。</summary>
        internal RepoState ReadState()
        {
            return ReadState(true);
        }

        /// <summary>
        /// 读取本地仓库状态。<paramref name="includeTags"/> 为 false 时跳过 tag 列表，
        /// 只刷新「当前提交/分支/tag/是否脏」，用于更新之后重新定位当前提交。
        /// </summary>
        internal RepoState ReadState(bool includeTags)
        {
            RepoState state = new RepoState();
            ProcessResult inside = Run(RepoArguments("rev-parse --is-inside-work-tree"), DefaultTimeoutMs, false);
            if (!inside.Started || inside.ExitCode != 0 || inside.Lines.Count == 0
                || !string.Equals(inside.Lines[0].Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                return state;
            }

            state.IsRepository = true;

            ProcessResult head = Run(RepoArguments("rev-parse HEAD"), DefaultTimeoutMs, false);
            state.HeadCommit = FirstLine(head);

            ProcessResult branch = Run(RepoArguments("rev-parse --abbrev-ref HEAD"), DefaultTimeoutMs, false);
            state.Branch = FirstLine(branch);

            ProcessResult describe = Run(RepoArguments("describe --tags --abbrev=0"), DefaultTimeoutMs, false);
            state.CurrentTag = describe.ExitCode == 0 ? FirstLine(describe) : null;

            ProcessResult status = Run(RepoArguments("status --porcelain"), DefaultTimeoutMs, false);
            if (status.Started && status.ExitCode == 0)
            {
                for (int i = 0; i < status.Lines.Count; i++)
                {
                    if (status.Lines[i].Trim().Length > 0)
                    {
                        state.DirtyCount++;
                    }
                }
            }

            if (!includeTags)
            {
                return state;
            }

            // 一次 show-ref 取回所有 tag 及其提交：注解 tag 的 ^{} 行给出真正的提交。
            ProcessResult tags = Run(RepoArguments("show-ref --tags -d"), DefaultTimeoutMs, false);
            if (tags.Started)
            {
                List<RemoteTag> localTags = ParseRefLines(tags.Lines, "refs/tags/");
                for (int i = 0; i < localTags.Count; i++)
                {
                    RemoteTag tag = localTags[i];
                    if (TagVersion.Parse(tag.Name, _config.TagPrefix) == null)
                    {
                        continue;
                    }

                    state.LocalTags.Add(tag.Name);
                    if (tag.Commit != null)
                    {
                        state.LocalTagCommits[tag.Name] = tag.Commit;
                    }
                }
            }

            return state;
        }

        /// <summary>列出远端 tag。git ls-remote 失败时回退到 GitHub REST API。</summary>
        internal List<RemoteTag> ListRemoteTags(out string error)
        {
            error = null;
            string remote = string.IsNullOrEmpty(_config.RemoteName) ? "origin" : _config.RemoteName;
            ProcessResult result = RunNetwork(RepoArguments("ls-remote --tags " + remote), NetworkTimeoutMs);

            if (!result.Started || result.ExitCode != 0)
            {
                string primaryError = result.StartError != null
                    ? result.StartError
                    : result.Tail(5);
                _log.Warn("git ls-remote 失败：" + OneLine(primaryError));

                List<RemoteTag> apiTags = ListRemoteTagsViaApi(out error);
                if (apiTags != null)
                {
                    _log.Info("已通过 GitHub API 取得 " + apiTags.Count.ToString(CultureInfo.InvariantCulture) + " 个 tag。");
                    return apiTags;
                }

                if (string.IsNullOrEmpty(error))
                {
                    error = primaryError;
                }

                return null;
            }

            return ParseLsRemote(result.Lines);
        }

        /// <summary>解析 <c>git ls-remote --tags</c> 输出；注解 tag 以 ^{} 行给出真正的提交。</summary>
        internal static List<RemoteTag> ParseLsRemote(List<string> lines)
        {
            return ParseRefLines(lines, "refs/tags/");
        }

        /// <summary>
        /// 解析 <c>&lt;sha&gt;&lt;空白&gt;&lt;引用名&gt;</c> 形式的行（ls-remote 与 show-ref 共用），
        /// 同名 tag 的 ^{} 行覆盖前面的行，从而拿到注解 tag 背后的提交。
        /// </summary>
        internal static List<RemoteTag> ParseRefLines(List<string> lines, string refPrefix)
        {
            List<RemoteTag> tags = new List<RemoteTag>();
            Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                int separator = IndexOfWhitespace(line);
                if (separator <= 0)
                {
                    continue;
                }

                string sha = line.Substring(0, separator).Trim();
                string reference = line.Substring(separator).Trim();
                if (!reference.StartsWith(refPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string name = reference.Substring(refPrefix.Length);
                bool peeled = name.EndsWith("^{}", StringComparison.Ordinal);
                if (peeled)
                {
                    name = name.Substring(0, name.Length - 3);
                }

                int existing;
                if (index.TryGetValue(name, out existing))
                {
                    RemoteTag known = tags[existing];
                    if (peeled || known.Commit == null)
                    {
                        known.Commit = sha;
                    }

                    continue;
                }

                RemoteTag tag = new RemoteTag();
                tag.Name = name;
                // 轻量 tag 的 sha 就是提交；注解 tag 随后会被 ^{} 行的提交覆盖。
                tag.Commit = sha;
                index[name] = tags.Count;
                tags.Add(tag);
            }

            return tags;
        }

        private static int IndexOfWhitespace(string line)
        {
            for (int i = 0; i < line.Length; i++)
            {
                if (char.IsWhiteSpace(line[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 通过 GitHub REST API 查询 tag。仅作为 git 不可用时的兜底，
        /// 因此用正则在稳定字段顺序（name 在前、commit.sha 在后）上取值。
        /// </summary>
        private List<RemoteTag> ListRemoteTagsViaApi(out string error)
        {
            error = null;
            string owner;
            string repository;
            if (!TryParseGitHubRemote(_config.RemoteUrl, out owner, out repository))
            {
                error = "remoteUrl 不是 GitHub 地址，无法使用 API 兜底";
                return null;
            }

            string url = "https://api.github.com/repos/" + owner + "/" + repository + "/tags?per_page=100";
            try
            {
                // 数字字面量：.NET 4.0 的 SecurityProtocolType 枚举里没有 Tls11/Tls12 成员。
                ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol
                    | (SecurityProtocolType)768
                    | (SecurityProtocolType)3072;

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.UserAgent = "DeepSeekHarnessLauncher";
                request.Accept = "application/vnd.github+json";
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;

                string body;
                using (WebResponse response = request.GetResponse())
                {
                    using (Stream stream = response.GetResponseStream())
                    {
                        using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false)))
                        {
                            body = reader.ReadToEnd();
                        }
                    }
                }

                List<RemoteTag> tags = new List<RemoteTag>();
                MatchCollection names = TagNamePattern.Matches(body);
                MatchCollection shas = ShaPattern.Matches(body);
                int shaCursor = 0;
                for (int i = 0; i < names.Count; i++)
                {
                    string name = names[i].Groups[1].Value;
                    string sha = null;
                    while (shaCursor < shas.Count && shas[shaCursor].Index < names[i].Index)
                    {
                        shaCursor++;
                    }

                    if (shaCursor < shas.Count)
                    {
                        sha = shas[shaCursor].Groups[1].Value;
                        shaCursor++;
                    }

                    RemoteTag tag = new RemoteTag();
                    tag.Name = name;
                    tag.Commit = sha;
                    tags.Add(tag);
                }

                _log.Info("GitHub API 返回 " + tags.Count.ToString(CultureInfo.InvariantCulture) + " 个 tag。");
                return tags;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Warn("GitHub API 兜底也失败：" + OneLine(ex.Message));
                return null;
            }
        }

        private static bool TryParseGitHubRemote(string remoteUrl, out string owner, out string repository)
        {
            owner = null;
            repository = null;
            if (string.IsNullOrEmpty(remoteUrl))
            {
                return false;
            }

            Match match = Regex.Match(remoteUrl, @"github\.com[:/](?<owner>[^/]+)/(?<repo>[^/\s]+?)(\.git)?$");
            if (!match.Success)
            {
                return false;
            }

            owner = match.Groups["owner"].Value;
            repository = match.Groups["repo"].Value;
            return true;
        }

        /// <summary>拉取 tag 并检出到分离头指针状态；任一步失败即返回 false。</summary>
        internal bool FetchAndCheckout(string tag)
        {
            string remote = string.IsNullOrEmpty(_config.RemoteName) ? "origin" : _config.RemoteName;
            ProcessResult fetch = RunNetwork(RepoArguments("fetch " + remote + " --tags --force"), NetworkTimeoutMs);
            if (!fetch.Started || fetch.ExitCode != 0)
            {
                _log.Error("git fetch 失败：" + OneLine(fetch.StartError != null ? fetch.StartError : fetch.Tail(5)));
                return false;
            }

            // fetch 成功但未落到目标 tag 时（远端未发布该 tag），不继续改动工作区。
            ProcessResult verify = Run(RepoArguments("rev-parse --verify " + Quote("refs/tags/" + tag)), DefaultTimeoutMs, false);
            if (!verify.Started || verify.ExitCode != 0)
            {
                _log.Error("远端不存在 tag " + tag + "，已中止更新。");
                return false;
            }

            ProcessResult checkout = Run(
                RepoArguments("-c advice.detachedHead=false checkout --detach " + Quote("refs/tags/" + tag)),
                NetworkTimeoutMs,
                true);
            if (!checkout.Started || checkout.ExitCode != 0)
            {
                _log.Error("git checkout 失败：" + OneLine(checkout.StartError != null ? checkout.StartError : checkout.Tail(5)));
                return false;
            }

            return true;
        }

        /// <summary>把本地未提交修改收进 stash，返回是否成功。</summary>
        internal bool StashLocalChanges(string message)
        {
            ProcessResult result = Run(
                RepoArguments("stash push --include-untracked -m " + Quote(message)),
                DefaultTimeoutMs,
                true);
            if (!result.Started || result.ExitCode != 0)
            {
                _log.Error("git stash 失败：" + OneLine(result.StartError != null ? result.StartError : result.Tail(5)));
                return false;
            }

            return true;
        }

        /// <summary>把更新前的分支与提交写入 state\pre-update-state.txt，便于回退。</summary>
        internal void SavePreUpdateState(RepoState state, UpdateDecision decision)
        {
            try
            {
                Directory.CreateDirectory(_config.StateDirectory);
                string path = Path.Combine(_config.StateDirectory, "pre-update-state.txt");
                StringBuilder builder = new StringBuilder();
                builder.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                builder.AppendLine("仓库：" + _config.HarnessDir);
                builder.AppendLine("分支：" + (state.Branch == null ? "(未知)" : state.Branch));
                builder.AppendLine("提交：" + (state.HeadCommit == null ? "(未知)" : state.HeadCommit));
                builder.AppendLine("检出 tag：" + (state.CurrentTag == null ? "(无)" : state.CurrentTag));
                builder.AppendLine("目标 tag：" + (decision.LatestRemote == null ? "(无)" : decision.LatestRemote.Name));
                builder.AppendLine("未提交修改行数：" + state.DirtyCount.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine();
                builder.AppendLine("回退方式：在源码目录执行");
                builder.AppendLine("  git checkout " + (state.Branch == null ? "master" : state.Branch));
                builder.AppendLine("  git stash list   # 若启动器执行过 stash，可在此查看并用 git stash pop 恢复");
                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
                _log.Info("更新前状态已记录：" + path);
            }
            catch (Exception ex)
            {
                _log.Warn("无法写入更新前状态文件：" + ex.Message);
            }
        }

        private static string FirstLine(ProcessResult result)
        {
            if (result == null || result.Lines.Count == 0)
            {
                return null;
            }

            string line = result.Lines[0].Trim();
            return line.Length == 0 ? null : line;
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }

        private static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string flattened = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flattened.Length > 400 ? flattened.Substring(0, 400) + "…" : flattened;
        }
    }
}
