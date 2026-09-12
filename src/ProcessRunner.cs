using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace DshLauncher
{
    /// <summary>一次外部命令的执行结果。</summary>
    internal sealed class ProcessResult
    {
        internal int ExitCode;
        internal bool Canceled;
        internal bool TimedOut;
        internal bool Started;
        internal string StartError;
        internal string Command;
        internal TimeSpan Duration;
        internal readonly List<string> Lines = new List<string>();

        /// <summary>末尾若干行，用于失败时在界面里给出可读的结论。</summary>
        internal string Tail(int count)
        {
            int from = Lines.Count > count ? Lines.Count - count : 0;
            StringBuilder builder = new StringBuilder();
            for (int i = from; i < Lines.Count; i++)
            {
                builder.AppendLine(Lines[i]);
            }

            return builder.ToString().TrimEnd();
        }
    }

    /// <summary>外部命令的描述。</summary>
    internal sealed class ProcessSpec
    {
        internal string Command;
        internal string WorkingDirectory;
        internal int TimeoutMs;
        internal bool Echo = true;
        internal readonly Dictionary<string, string> Environment =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 通过 cmd.exe 执行命令：pnpm 是 .CMD 批处理，交给命令解释器最稳妥，
    /// 同时用 <c>chcp 65001</c> 让 git/node 按 UTF-8 输出，避免中文乱码。
    /// </summary>
    internal static class ProcessRunner
    {
        private const int MaxCapturedLines = 4000;

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        internal static string CmdPath()
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrEmpty(system))
            {
                string candidate = Path.Combine(system, "cmd.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "cmd.exe";
        }

        internal static ProcessResult Run(ProcessSpec spec, Action<string> onLine, CancellationToken token)
        {
            ProcessResult result = new ProcessResult();
            result.Command = spec.Command;
            DateTime startedAt = DateTime.UtcNow;
            Process process = new Process();
            object lineGate = new object();

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = CmdPath();
                startInfo.Arguments = "/d /s /c \"" + "chcp 65001 >nul & " + spec.Command + "\"";
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.RedirectStandardInput = true;
                startInfo.StandardOutputEncoding = Utf8;
                startInfo.StandardErrorEncoding = Utf8;
                if (!string.IsNullOrEmpty(spec.WorkingDirectory))
                {
                    startInfo.WorkingDirectory = spec.WorkingDirectory;
                }

                foreach (KeyValuePair<string, string> pair in spec.Environment)
                {
                    startInfo.EnvironmentVariables[pair.Key] = pair.Value;
                }

                process.StartInfo = startInfo;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    Capture(result, lineGate, args.Data, onLine, spec.Echo);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    Capture(result, lineGate, args.Data, onLine, spec.Echo);
                };

                if (!process.Start())
                {
                    result.StartError = "进程无法启动";
                    return result;
                }

                result.Started = true;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // 闭合 stdin：让需要交互输入的程序立即拿到 EOF 而不是永久阻塞界面。
                try
                {
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                }

                bool exited = false;
                while (!exited)
                {
                    if (process.WaitForExit(200))
                    {
                        exited = true;
                        break;
                    }

                    if (token.IsCancellationRequested)
                    {
                        result.Canceled = true;
                        break;
                    }

                    if (spec.TimeoutMs > 0 && (DateTime.UtcNow - startedAt).TotalMilliseconds > spec.TimeoutMs)
                    {
                        result.TimedOut = true;
                        break;
                    }
                }

                if (!exited)
                {
                    KillTree(process);
                    // 已强杀时不再等异步读取自然结束，只做有界等待。
                    process.WaitForExit(10000);
                }
                else
                {
                    // 无参 WaitForExit 会等异步读取回调把剩余输出冲刷完。
                    process.WaitForExit();
                }

                try
                {
                    result.ExitCode = process.ExitCode;
                }
                catch (InvalidOperationException)
                {
                    result.ExitCode = -1;
                }
            }
            catch (Exception ex)
            {
                result.StartError = ex.Message;
                KillTree(process);
            }
            finally
            {
                result.Duration = DateTime.UtcNow - startedAt;
                process.Dispose();
            }

            return result;
        }

        private static void Capture(ProcessResult result, object gate, string line, Action<string> onLine, bool echo)
        {
            if (line == null)
            {
                return;
            }

            lock (gate)
            {
                if (result.Lines.Count < MaxCapturedLines)
                {
                    result.Lines.Add(line);
                }
            }

            if (echo && onLine != null)
            {
                onLine(line);
            }
        }

        /// <summary>结束整棵进程树：cmd → pnpm → node，避免残留的 Web 服务进程。</summary>
        internal static void KillTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    string taskkill = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "taskkill.exe");
                    ProcessStartInfo info = new ProcessStartInfo(taskkill);
                    info.Arguments = "/PID " + process.Id.ToString(CultureInfo.InvariantCulture) + " /T /F";
                    info.UseShellExecute = false;
                    info.CreateNoWindow = true;
                    info.RedirectStandardOutput = true;
                    info.RedirectStandardError = true;
                    Process killer = Process.Start(info);
                    if (killer != null)
                    {
                        killer.WaitForExit(10000);
                        killer.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                // taskkill 不可用时退回到直接终止子进程。
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception)
            {
                // 进程可能刚好自行退出，这里无需处理。
            }

            try
            {
                process.WaitForExit(5000);
            }
            catch (Exception)
            {
            }
        }
    }
}
