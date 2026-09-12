using System;
using System.Collections.Generic;
using System.Text;

namespace DshLauncher
{
    /// <summary>本地仓库的只读快照。</summary>
    internal sealed class RepoState
    {
        internal bool IsRepository;
        internal string HeadCommit;
        internal string Branch;
        internal string CurrentTag;
        internal int DirtyCount;
        internal readonly List<string> LocalTags = new List<string>();

        /// <summary>本地同名 tag 指向的提交，用于识别远端移动过的 tag。</summary>
        internal readonly Dictionary<string, string> LocalTagCommits =
            new Dictionary<string, string>(StringComparer.Ordinal);

        internal bool IsDirty
        {
            get { return DirtyCount > 0; }
        }
    }

    /// <summary>远端 tag 及其指向的提交。</summary>
    internal sealed class RemoteTag
    {
        internal string Name;
        internal string Commit;
    }

    /// <summary>更新判定结果，供提示框、日志与自检复用。</summary>
    internal sealed class UpdateDecision
    {
        internal bool CheckPerformed;
        internal string CheckError;
        internal bool Available;
        internal string Reason;
        internal RemoteTag LatestRemote;
        internal string LatestLocalTag;
        internal string CurrentTag;
        internal int RemoteTagCount;
        internal readonly List<RemoteTag> RemoteTags = new List<RemoteTag>();

        /// <summary>面向提示框的多行说明。</summary>
        internal string Describe(string tagPrefix)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("远端仓库发布了本地还没有的版本 tag。");
            builder.AppendLine();
            builder.AppendLine("当前检出：" + (CurrentTag == null ? "（不在任何 tag 上）" : CurrentTag));
            builder.AppendLine("本地最新 tag：" + (LatestLocalTag == null ? "（无）" : LatestLocalTag));
            builder.Append("远端最新 tag：" + (LatestRemote == null ? "（无）" : LatestRemote.Name));
            if (LatestRemote != null && LatestRemote.Commit != null && LatestRemote.Commit.Length >= 8)
            {
                builder.Append("  (" + LatestRemote.Commit.Substring(0, 8) + ")");
            }

            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("判定依据：" + Reason);
            builder.AppendLine();
            builder.AppendLine("选择“是”：git fetch 该 tag 并检出，然后 pnpm install、pnpm run build、pnpm dsh web。");
            builder.AppendLine("选择“否”：跳过更新，直接用当前代码执行 pnpm run build、pnpm dsh web。");
            return builder.ToString();
        }
    }

    /// <summary>
    /// 纯函数形式的更新判定，不接触 git 与文件系统，因此可以被 --self-test 直接覆盖。
    /// </summary>
    internal static class UpdatePolicy
    {
        /// <summary>
        /// 判定是否需要提示更新。任一条件成立即视为有新 tag：
        /// 远端最新 tag 高于当前检出的 tag；或该 tag 本地还没有；或同名 tag 指向了别的提交。
        /// </summary>
        internal static UpdateDecision Evaluate(
            RepoState state,
            List<RemoteTag> remoteTags,
            string tagPrefix,
            string checkError)
        {
            UpdateDecision decision = new UpdateDecision();
            decision.CheckError = checkError;

            if (state == null || !state.IsRepository)
            {
                decision.Reason = "目标目录不是 git 仓库";
                return decision;
            }

            if (remoteTags == null)
            {
                decision.Reason = "无法获取远端 tag 列表";
                return decision;
            }

            decision.CheckPerformed = true;
            decision.CurrentTag = state.CurrentTag;
            decision.RemoteTagCount = remoteTags.Count;

            List<string> names = new List<string>();
            Dictionary<string, string> commits = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < remoteTags.Count; i++)
            {
                names.Add(remoteTags[i].Name);
                commits[remoteTags[i].Name] = remoteTags[i].Commit;
            }

            decision.RemoteTags.AddRange(remoteTags);

            string latestRemoteTag;
            TagVersion latestRemote = TagVersion.Latest(names, tagPrefix, out latestRemoteTag);
            TagVersion latestLocal = TagVersion.Latest(state.LocalTags, tagPrefix, out decision.LatestLocalTag);

            if (latestRemote == null)
            {
                decision.Reason = "远端没有匹配前缀 " + tagPrefix + " 的 tag";
                return decision;
            }

            decision.LatestRemote = new RemoteTag();
            decision.LatestRemote.Name = latestRemoteTag;
            decision.LatestRemote.Commit = commits.ContainsKey(latestRemoteTag) ? commits[latestRemoteTag] : null;

            bool presentLocally = state.LocalTags.Contains(latestRemoteTag);
            TagVersion current = TagVersion.Parse(state.CurrentTag, tagPrefix);

            if (!presentLocally)
            {
                decision.Available = true;
                decision.Reason = "本地尚未获取 tag " + latestRemoteTag;
                return decision;
            }

            string localCommit = state.LocalTagCommits.ContainsKey(latestRemoteTag)
                ? state.LocalTagCommits[latestRemoteTag]
                : null;

            if (localCommit != null && decision.LatestRemote.Commit != null
                && !string.Equals(localCommit, decision.LatestRemote.Commit, StringComparison.OrdinalIgnoreCase))
            {
                decision.Available = true;
                decision.Reason = "tag " + latestRemoteTag + " 指向的提交与本地不一致（远端已移动该 tag）";
                return decision;
            }

            if (current == null)
            {
                decision.Available = true;
                decision.Reason = "当前检出不是版本 tag，而远端最新版本是 " + latestRemoteTag;
                return decision;
            }

            if (latestRemote.CompareTo(current) > 0)
            {
                decision.Available = true;
                decision.Reason = "远端最新版本 " + latestRemoteTag + " 高于当前检出的 " + state.CurrentTag;
                return decision;
            }

            decision.Available = false;
            decision.Reason = "本地已包含远端最新 tag（当前检出 " + state.CurrentTag
                + (latestLocal != null ? "，本地最新 " + decision.LatestLocalTag : string.Empty) + "）";
            return decision;
        }
    }
}
