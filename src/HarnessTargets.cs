using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DshLauncher
{
    /// <summary>目标目录的校验结论。</summary>
    internal enum TargetVerdict
    {
        /// <summary>可以启动。</summary>
        Ok,

        /// <summary>路径为空或目录不存在，无法启动。</summary>
        Missing,

        /// <summary>目录存在但看起来不是 deepseek-harness，需要用户确认。</summary>
        NotHarness,
    }

    /// <summary>
    /// 本地 harness 目标目录：校验，以及“最近使用”列表的持久化。
    ///
    /// 默认目标始终来自配置文件的 harnessDir（或 --harness 指定）；
    /// 界面里切换的目标只影响本次启动，并记录到 state\harness-targets.ini，
    /// 下次可以从下拉框里直接选回来。
    /// </summary>
    internal static class HarnessTargets
    {
        private const string FileName = "harness-targets.ini";
        private const string RecentKey = "recent";

        internal const int MaxRecent = 4;

        /// <summary>去掉引号与结尾分隔符，尽量转成绝对路径。转换失败时返回原值。</summary>
        internal static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string trimmed = path.Trim().Trim('"').Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(trimmed);
            }
            catch (ArgumentException)
            {
                return trimmed;
            }
            catch (NotSupportedException)
            {
                return trimmed;
            }
            catch (PathTooLongException)
            {
                return trimmed;
            }
        }

        /// <summary>校验目标目录；<paramref name="detail"/> 给出可读原因。</summary>
        internal static TargetVerdict Validate(string path, out string detail)
        {
            detail = null;
            string normalized = Normalize(path);
            if (normalized.Length == 0)
            {
                detail = "路径为空";
                return TargetVerdict.Missing;
            }

            if (!Directory.Exists(normalized))
            {
                detail = "目录不存在：" + normalized;
                return TargetVerdict.Missing;
            }

            string manifest = Path.Combine(normalized, "package.json");
            if (!File.Exists(manifest))
            {
                detail = "目录里没有 package.json，看起来不是 deepseek-harness 源码目录";
                return TargetVerdict.NotHarness;
            }

            string text;
            try
            {
                text = File.ReadAllText(manifest, new UTF8Encoding(false));
            }
            catch (IOException)
            {
                detail = "无法读取 package.json";
                return TargetVerdict.NotHarness;
            }
            catch (UnauthorizedAccessException)
            {
                detail = "无法读取 package.json";
                return TargetVerdict.NotHarness;
            }

            // dsh 启动脚本是 harness 根清单的固定成员，可作为“这是 harness”的判据。
            if (text.IndexOf("\"dsh\"", StringComparison.Ordinal) < 0)
            {
                detail = "package.json 里没有 dsh 启动脚本，看起来不是 deepseek-harness 源码目录";
                return TargetVerdict.NotHarness;
            }

            detail = normalized;
            return TargetVerdict.Ok;
        }

        /// <summary>读取最近使用的目标目录列表（最近在前）。</summary>
        internal static List<string> LoadRecent(AppConfig config)
        {
            string path = Path.Combine(config.StateDirectory, FileName);
            if (!File.Exists(path))
            {
                return new List<string>();
            }

            try
            {
                string[] lines = File.ReadAllLines(path, new UTF8Encoding(false));
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    if (string.Equals(line.Substring(0, separator).Trim(), RecentKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return ParseRecent(line.Substring(separator + 1));
                    }
                }
            }
            catch (IOException)
            {
                // 读不到就当作没有历史记录。
            }
            catch (UnauthorizedAccessException)
            {
            }

            return new List<string>();
        }

        /// <summary>写入最近使用的目标目录列表；写失败只影响下次的便利性。</summary>
        internal static void SaveRecent(AppConfig config, List<string> recent)
        {
            try
            {
                Directory.CreateDirectory(config.StateDirectory);
                StringBuilder builder = new StringBuilder();
                builder.AppendLine("# DeepSeek Harness 启动器：最近使用的源码目录（最近在前）");
                builder.AppendLine(RecentKey + " = " + FormatRecent(recent));
                File.WriteAllText(Path.Combine(config.StateDirectory, FileName), builder.ToString(), new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>把标签拆成列表（纯函数，便于自检）。</summary>
        internal static List<string> ParseRecent(string value)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(value))
            {
                return result;
            }

            string[] parts = value.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string item = Normalize(parts[i]);
                if (item.Length == 0 || Contains(result, item))
                {
                    continue;
                }

                result.Add(item);
                if (result.Count >= MaxRecent)
                {
                    break;
                }
            }

            return result;
        }

        /// <summary>把列表拼成标签（纯函数，便于自检）。</summary>
        internal static string FormatRecent(List<string> recent)
        {
            if (recent == null || recent.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < recent.Count && i < MaxRecent; i++)
            {
                if (i > 0)
                {
                    builder.Append('|');
                }

                builder.Append(recent[i]);
            }

            return builder.ToString();
        }

        /// <summary>把新目标放到列表最前，去掉默认目录与重复项。</summary>
        internal static List<string> AddRecent(List<string> recent, string path, string defaultPath)
        {
            List<string> result = new List<string>();
            string candidate = Normalize(path);
            string fallback = Normalize(defaultPath);
            if (candidate.Length > 0 && !string.Equals(candidate, fallback, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(candidate);
            }

            if (recent != null)
            {
                for (int i = 0; i < recent.Count; i++)
                {
                    string item = Normalize(recent[i]);
                    if (item.Length == 0 || string.Equals(item, fallback, StringComparison.OrdinalIgnoreCase) || Contains(result, item))
                    {
                        continue;
                    }

                    result.Add(item);
                    if (result.Count >= MaxRecent)
                    {
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>路径过长时保留头尾，便于在下拉框里辨认。</summary>
        internal static string Shorten(string path, int maxLength)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= maxLength || maxLength < 12)
            {
                return path;
            }

            int head = (maxLength - 3) / 2;
            int tail = maxLength - 3 - head;
            return path.Substring(0, head) + "..." + path.Substring(path.Length - tail);
        }

        private static bool Contains(List<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
