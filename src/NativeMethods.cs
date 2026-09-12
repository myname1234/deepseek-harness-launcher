using System;
using System.Runtime.InteropServices;

namespace DshLauncher
{
    /// <summary>启动器需要的少量 Win32 入口。</summary>
    internal static class NativeMethods
    {
        internal const int ATTACH_PARENT_PROCESS = -1;

        /// <summary>把 GUI 子系统的进程挂到父进程的控制台，用于 --diagnose 的文本输出。</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FreeConsole();
    }
}
