using System;
using System.IO;
using System.Text;

namespace DshLauncher
{
    /// <summary>
    /// GUI 子系统进程的控制台桥接：--diagnose 既写报告文件，也尽量回显到调用者的控制台。
    /// 无控制台可用时静默降级，报告文件仍然完整。
    /// </summary>
    internal static class ConsoleBridge
    {
        private static bool _initialized;

        internal static void Init()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            try
            {
                if (NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS))
                {
                    StreamWriter writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                    writer.AutoFlush = true;
                    Console.SetOut(writer);
                }
            }
            catch (Exception)
            {
                // 典型情况：父进程没有控制台（从资源管理器双击启动）。
                // 此时没有可写的标准输出，报告文件是唯一的输出通道。
            }
        }

        internal static void Write(string text)
        {
            try
            {
                Console.Out.Write(text);
            }
            catch (Exception)
            {
                // 同上：无控制台句柄时忽略，调用方已经写入报告文件。
            }
        }

        internal static void WriteLine(string text)
        {
            Write(text + Environment.NewLine);
        }
    }
}
