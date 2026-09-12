using System;
using System.Collections.Generic;
using System.Globalization;

namespace DshLauncher
{
    /// <summary>
    /// 形如 <c>dsh-v0.1.5-rc.2</c> 的版本 tag，按 Semantic Versioning 2.0.0 的优先级规则比较。
    /// 前缀（默认 dsh-v）之后必须是 major.minor.patch，可选预发布标识；构建元数据被忽略。
    /// </summary>
    internal sealed class TagVersion : IComparable<TagVersion>
    {
        private readonly string[] _prerelease;

        private TagVersion(int major, int minor, int patch, string[] prerelease, string raw)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            _prerelease = prerelease;
            Raw = raw;
        }

        internal readonly int Major;
        internal readonly int Minor;
        internal readonly int Patch;
        internal readonly string Raw;

        internal bool IsPrerelease
        {
            get { return _prerelease.Length > 0; }
        }

        /// <summary>解析 tag；前缀不匹配或格式不合法时返回 null。</summary>
        internal static TagVersion Parse(string tag, string prefix)
        {
            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(prefix))
            {
                return null;
            }

            if (!tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string rest = tag.Substring(prefix.Length);

            int plus = rest.IndexOf('+');
            if (plus >= 0)
            {
                rest = rest.Substring(0, plus);
            }

            string[] sections = rest.Split(new char[] { '-' }, 2);
            string[] numbers = sections[0].Split('.');
            if (numbers.Length != 3)
            {
                return null;
            }

            int major;
            int minor;
            int patch;
            if (!TryParseNumber(numbers[0], out major)
                || !TryParseNumber(numbers[1], out minor)
                || !TryParseNumber(numbers[2], out patch))
            {
                return null;
            }

            string[] prerelease = sections.Length > 1 && sections[1].Length > 0
                ? sections[1].Split('.')
                : new string[0];

            for (int i = 0; i < prerelease.Length; i++)
            {
                if (prerelease[i].Length == 0)
                {
                    return null;
                }
            }

            return new TagVersion(major, minor, patch, prerelease, tag);
        }

        private static bool TryParseNumber(string text, out int value)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
                && text.Length > 0;
        }

        public int CompareTo(TagVersion other)
        {
            if (other == null)
            {
                return 1;
            }

            int result = Major.CompareTo(other.Major);
            if (result != 0)
            {
                return result;
            }

            result = Minor.CompareTo(other.Minor);
            if (result != 0)
            {
                return result;
            }

            result = Patch.CompareTo(other.Patch);
            if (result != 0)
            {
                return result;
            }

            // 正式版优先级高于任何预发布版本。
            if (!IsPrerelease && other.IsPrerelease)
            {
                return 1;
            }

            if (IsPrerelease && !other.IsPrerelease)
            {
                return -1;
            }

            for (int i = 0; ; i++)
            {
                bool left = i < _prerelease.Length;
                bool right = i < other._prerelease.Length;
                if (!left && !right)
                {
                    return 0;
                }

                if (!left)
                {
                    return -1;
                }

                if (!right)
                {
                    return 1;
                }

                string a = _prerelease[i];
                string b = other._prerelease[i];
                bool aNumeric = IsNumeric(a);
                bool bNumeric = IsNumeric(b);
                if (aNumeric && bNumeric)
                {
                    result = CompareNumeric(a, b);
                }
                else if (aNumeric)
                {
                    result = -1;
                }
                else if (bNumeric)
                {
                    result = 1;
                }
                else
                {
                    result = string.CompareOrdinal(a, b);
                }

                if (result != 0)
                {
                    return result;
                }
            }
        }

        private static bool IsNumeric(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }

            return text.Length > 0;
        }

        /// <summary>先比长度再比字典序，避免超出 int 范围的数字标识溢出。</summary>
        private static int CompareNumeric(string a, string b)
        {
            string left = a.TrimStart('0');
            string right = b.TrimStart('0');
            if (left.Length != right.Length)
            {
                return left.Length.CompareTo(right.Length);
            }

            return string.CompareOrdinal(left, right);
        }

        public override string ToString()
        {
            return Raw;
        }

        /// <summary>去掉前缀的版本号，用于界面展示。</summary>
        internal string Display(string prefix)
        {
            if (!string.IsNullOrEmpty(prefix) && Raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Raw.Substring(prefix.Length);
            }

            return Raw;
        }

        /// <summary>从候选 tag 名称里挑出优先级最高的一个。</summary>
        internal static TagVersion Latest(IEnumerable<string> tags, string prefix, out string latestTag)
        {
            TagVersion best = null;
            latestTag = null;
            foreach (string tag in tags)
            {
                TagVersion parsed = Parse(tag, prefix);
                if (parsed == null)
                {
                    continue;
                }

                if (best == null || parsed.CompareTo(best) > 0)
                {
                    best = parsed;
                    latestTag = tag;
                }
            }

            return best;
        }
    }
}
