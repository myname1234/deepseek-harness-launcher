using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DshLauncher
{
    /// <summary>
    /// 判断「当前检出的源码是否已经有对应的构建产物」，用于决定能否跳过 pnpm run build。
    ///
    /// 两个证据，任一成立即认为产物有效：
    ///   1. 启动器自己的记录 state\last-build.ini：该提交被启动器以干净工作区构建成功过；
    ///   2. 仓库自己的记录 .dsh-build\client-build-environment.json 中的 DSH_CLIENT_COMMIT_HASH。
    /// 两者都只读，无法确认时返回 false（宁可多构建一次，也不让 dsh web 因缺产物而失败）。
    /// </summary>
    internal static class BuildState
    {
        private const string RecordFileName = "last-build.ini";
        private const string HarnessRecordPath = @".dsh-build\client-build-environment.json";

        private static readonly Regex CommitHashPattern = new Regex(
            "\"DSH_CLIENT_COMMIT_HASH\"\\s*:\\s*\"([^\"]+)\"",
            RegexOptions.Compiled);

        /// <summary>启动器记录的最近一次成功构建。</summary>
        internal sealed class BuildRecord
        {
            internal string HarnessDir;
            internal string Commit;
            internal string Branch;
            internal bool Dirty;
            internal string Time;
            internal string Command;
        }

        internal static string RecordPath(AppConfig config)
        {
            return Path.Combine(config.StateDirectory, RecordFileName);
        }

        /// <summary>读取启动器自己的构建记录；不存在或损坏时返回 null。</summary>
        internal static BuildRecord ReadRecord(AppConfig config)
        {
            string path = RecordPath(config);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                BuildRecord record = new BuildRecord();
                string[] lines = File.ReadAllLines(path, new UTF8Encoding(false));
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    {
                        continue;
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim().ToLowerInvariant();
                    string value = line.Substring(separator + 1).Trim();
                    switch (key)
                    {
                        case "harness": record.HarnessDir = value; break;
                        case "commit": record.Commit = value; break;
                        case "branch": record.Branch = value; break;
                        case "dirty": record.Dirty = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        case "time": record.Time = value; break;
                        case "command": record.Command = value; break;
                    }
                }

                return string.IsNullOrEmpty(record.Commit) ? null : record;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>记录一次成功构建。写失败只影响下次的跳过判断，不打断流程。</summary>
        internal static void WriteRecord(AppConfig config, RepoState state, string commandLine)
        {
            try
            {
                Directory.CreateDirectory(config.StateDirectory);
                StringBuilder builder = new StringBuilder();
                builder.AppendLine("# DeepSeek Harness 启动器：最近一次成功构建，用于判断能否跳过 pnpm run build");
                builder.AppendLine("harness = " + config.HarnessDir);
                builder.AppendLine("commit = " + (state == null || state.HeadCommit == null ? string.Empty : state.HeadCommit));
                builder.AppendLine("branch = " + (state == null || state.Branch == null ? string.Empty : state.Branch));
                builder.AppendLine("dirty = " + (state != null && state.IsDirty ? "true" : "false"));
                builder.AppendLine("time = " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                builder.AppendLine("command = " + commandLine);
                File.WriteAllText(RecordPath(config), builder.ToString(), new UTF8Encoding(false));
            }
            catch (IOException)
            {
                // 记录写不进去时，下次仍会构建，属于安全方向。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>读取仓库自身的构建记录里的提交号；不存在或不可读时返回 null。</summary>
        internal static string ReadHarnessCommitHash(AppConfig config)
        {
            string path = Path.Combine(config.HarnessDir, HarnessRecordPath);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                Match match = CommitHashPattern.Match(File.ReadAllText(path, new UTF8Encoding(false)));
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// 当前工作区是否可以跳过构建。<paramref name="detail"/> 说明依据，供日志与界面展示。
        /// </summary>
        internal static bool ArtifactsCurrent(AppConfig config, RepoState state, out string detail)
        {
            detail = null;
            if (state == null || string.IsNullOrEmpty(state.HeadCommit))
            {
                detail = "无法确定当前提交";
                return false;
            }

            BuildRecord record = ReadRecord(config);
            if (record != null && !string.IsNullOrEmpty(record.HarnessDir)
                && !string.Equals(HarnessTargets.Normalize(record.HarnessDir), HarnessTargets.Normalize(config.HarnessDir), StringComparison.OrdinalIgnoreCase))
            {
                // 记录来自另一个源码目录：产物与当前目标无关，必须重新构建。
                detail = "最近一次构建记录属于另一个源码目录（" + HarnessTargets.Shorten(record.HarnessDir, 60) + "）";
                return false;
            }

            if (record != null && !record.Dirty && CommitMatches(record.Commit, state.HeadCommit))
            {
                detail = "启动器上次已为提交 " + Short(state.HeadCommit) + " 构建成功（" + record.Time + "）";
                return true;
            }

            string harnessHash = ReadHarnessCommitHash(config);
            if (harnessHash != null && CommitMatches(harnessHash, state.HeadCommit))
            {
                detail = "仓库构建记录 .dsh-build 对应提交 " + Short(state.HeadCommit);
                return true;
            }

            if (record != null)
            {
                detail = "最近一次构建记录是 " + Short(record.Commit)
                    + (record.Dirty ? "（当时工作区有未提交修改）" : string.Empty)
                    + "，与当前提交 " + Short(state.HeadCommit) + " 不一致";
                return false;
            }

            detail = "未找到当前提交 " + Short(state.HeadCommit) + " 的构建记录";
            return false;
        }

        /// <summary>记录里的短提交号与完整提交号互相前缀匹配即视为同一提交。</summary>
        private static bool CommitMatches(string recorded, string head)
        {
            if (string.IsNullOrEmpty(recorded) || string.IsNullOrEmpty(head) || recorded.Length < 7)
            {
                return false;
            }

            return head.StartsWith(recorded, StringComparison.OrdinalIgnoreCase)
                || recorded.StartsWith(head, StringComparison.OrdinalIgnoreCase);
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
