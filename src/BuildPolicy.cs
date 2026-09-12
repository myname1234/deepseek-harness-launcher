using System;

namespace DshLauncher
{
    /// <summary>构建判定的结论。</summary>
    internal sealed class BuildVerdict
    {
        internal bool Build;
        internal string Reason;
    }

    /// <summary>
    /// 决定是否执行 <c>pnpm run build</c> 的纯函数，便于 --self-test 直接覆盖。
    ///
    /// 规则（自上而下，命中即返回）：
    ///   1. 强制构建（--force-build 或 buildWhenUpToDate = true）→ 构建；
    ///   2. 工作区有未提交修改 → 构建（产物无法代表当前源码）；
    ///   3. 存在可用更新 → 构建（更新后的新源码必须重新构建）；
    ///   4. 未完成 tag 检查（未开启检查或检查失败）→ 由 buildWhenCheckFailed 决定；
    ///   5. 检查完成且无可用更新（即“已是最新”）→ 仅当能证明当前提交已有构建产物时跳过。
    /// </summary>
    internal static class BuildPolicy
    {
        internal static BuildVerdict Decide(
            bool forceBuild,
            RepoState state,
            UpdateDecision update,
            bool buildWhenCheckFailed,
            bool artifactsCurrent,
            string artifactsDetail)
        {
            BuildVerdict verdict = new BuildVerdict();

            if (forceBuild)
            {
                verdict.Build = true;
                verdict.Reason = "已按参数或配置强制构建";
                return verdict;
            }

            if (state != null && state.IsRepository && state.IsDirty)
            {
                verdict.Build = true;
                verdict.Reason = "工作区有 " + state.DirtyCount + " 项未提交修改";
                return verdict;
            }

            if (update != null && update.Available)
            {
                verdict.Build = true;
                verdict.Reason = "存在可用更新（" + (update.LatestRemote == null ? "远端有更高版本" : update.LatestRemote.Name) + "）";
                return verdict;
            }

            bool checkPerformed = update != null && update.CheckPerformed;
            if (!checkPerformed)
            {
                verdict.Build = buildWhenCheckFailed;
                verdict.Reason = buildWhenCheckFailed
                    ? "无法确认远端 tag 状态（未检查或检查失败），按配置先构建"
                    : "无法确认远端 tag 状态，但配置允许跳过构建";
                return verdict;
            }

            if (artifactsCurrent)
            {
                verdict.Build = false;
                verdict.Reason = "已是最新且" + (artifactsDetail == null ? "构建产物有效" : artifactsDetail);
                return verdict;
            }

            verdict.Build = true;
            verdict.Reason = "已是最新，但" + (artifactsDetail == null ? "缺少当前提交的构建产物" : artifactsDetail) + "，先构建一次";
            return verdict;
        }
    }
}
