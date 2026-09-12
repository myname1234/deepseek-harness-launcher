using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DshLauncher
{
    /// <summary>一个可见的顶层窗口。</summary>
    internal sealed class WindowInfo
    {
        internal IntPtr Handle;
        internal uint ProcessId;
        internal string ProcessName;
        internal string ClassName;
        internal string Title;
    }

    /// <summary>
    /// 顶层窗口的枚举与关闭。
    ///
    /// 关闭界面应用只能靠窗口消息：Edge/Chrome 的“应用窗口”由浏览器主进程持有，
    /// 结束进程会连带关掉用户的所有浏览窗口，因此这里只对识别出的那一个窗口
    /// 发送 WM_CLOSE，且绝不杀进程。
    /// </summary>
    internal static class WindowCloser
    {
        private const int WM_CLOSE = 0x0010;

        /// <summary>浏览器主窗口标题的固定后缀；应用窗口没有这个后缀。</summary>
        private static readonly string[] BrowserTitleSuffixes = new string[]
        {
            "microsoft edge",
            "google chrome",
            "chromium",
            "brave",
            "mozilla firefox",
            "vivaldi",
            "opera",
        };

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        /// <summary>枚举当前所有可见且有标题的顶层窗口。</summary>
        internal static List<WindowInfo> ListVisibleWindows()
        {
            List<WindowInfo> windows = new List<WindowInfo>();
            EnumWindowsProc callback = delegate(IntPtr handle, IntPtr parameter)
            {
                if (IsWindowVisible(handle))
                {
                    int length = GetWindowTextLength(handle);
                    if (length > 0)
                    {
                        StringBuilder title = new StringBuilder(length + 2);
                        if (GetWindowText(handle, title, title.Capacity) > 0)
                        {
                            WindowInfo info = new WindowInfo();
                            info.Handle = handle;
                            info.Title = title.ToString();
                            info.ClassName = ClassNameOf(handle);
                            uint processId;
                            GetWindowThreadProcessId(handle, out processId);
                            info.ProcessId = processId;
                            info.ProcessName = ProcessNameOf(processId);
                            windows.Add(info);
                        }
                    }
                }

                return true;
            };

            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return windows;
        }

        private static string ClassNameOf(IntPtr handle)
        {
            StringBuilder builder = new StringBuilder(256);
            GetClassName(handle, builder, builder.Capacity);
            return builder.ToString();
        }

        private static string ProcessNameOf(uint processId)
        {
            try
            {
                return Process.GetProcessById((int)processId).ProcessName;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
        }

        /// <summary>去掉标题里的零宽字符（Edge 的标题里带有 U+200B）。</summary>
        internal static string NormalizeTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(title.Length);
            for (int i = 0; i < title.Length; i++)
            {
                char c = title[i];
                if (c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\uFEFF')
                {
                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString().Trim();
        }

        /// <summary>判断标题是否属于浏览器主窗口；这类窗口永远不在关闭范围内。</summary>
        internal static bool IsBrowserMainWindowTitle(string title)
        {
            string normalized = NormalizeTitle(title);
            if (normalized.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < BrowserTitleSuffixes.Length; i++)
            {
                string suffix = BrowserTitleSuffixes[i];
                if (normalized.Length >= suffix.Length
                    && normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>按配置的关键字匹配应用窗口标题（可留空表示不按标题匹配）。</summary>
        internal static bool MatchesAppWindowTitle(string title, string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                return false;
            }

            string normalized = NormalizeTitle(title);
            return normalized.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>找出在 <paramref name="known"/> 之后新出现的、看起来像应用窗口的窗口。</summary>
        internal static WindowInfo FindNewAppWindow(List<IntPtr> known, string titleKeyword)
        {
            List<WindowInfo> windows = ListVisibleWindows();
            for (int i = 0; i < windows.Count; i++)
            {
                WindowInfo window = windows[i];
                if (IsBrowserMainWindowTitle(window.Title))
                {
                    continue;
                }

                if (known != null && known.Contains(window.Handle))
                {
                    continue;
                }

                // 配置了关键字时只接受匹配的窗口；否则接受任何新出现的窗口。
                if (!string.IsNullOrEmpty(titleKeyword) && !MatchesAppWindowTitle(window.Title, titleKeyword))
                {
                    continue;
                }

                return window;
            }

            return null;
        }

        /// <summary>按标题关键字找已存在的应用窗口（用于应用本来就开着的情况）。</summary>
        internal static WindowInfo FindAppWindowByTitle(string titleKeyword)
        {
            if (string.IsNullOrEmpty(titleKeyword))
            {
                return null;
            }

            List<WindowInfo> windows = ListVisibleWindows();
            for (int i = 0; i < windows.Count; i++)
            {
                WindowInfo window = windows[i];
                if (IsBrowserMainWindowTitle(window.Title))
                {
                    continue;
                }

                if (MatchesAppWindowTitle(window.Title, titleKeyword))
                {
                    return window;
                }
            }

            return null;
        }

        /// <summary>向窗口投递 WM_CLOSE（异步、不阻塞、不杀进程）。</summary>
        internal static bool RequestClose(IntPtr handle)
        {
            if (handle == IntPtr.Zero || !IsWindow(handle))
            {
                return false;
            }

            return PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>收集当前窗口句柄，作为“启动前已存在”的基线。</summary>
        internal static List<IntPtr> SnapshotHandles()
        {
            List<WindowInfo> windows = ListVisibleWindows();
            List<IntPtr> handles = new List<IntPtr>(windows.Count);
            for (int i = 0; i < windows.Count; i++)
            {
                handles.Add(windows[i].Handle);
            }

            return handles;
        }
    }
}
