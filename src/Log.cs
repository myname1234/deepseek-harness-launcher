using System;
using System.IO;
using System.Text;

namespace DshLauncher
{
    /// <summary>日志级别，决定界面中的文字颜色。</summary>
    internal enum LogLevel
    {
        Info,
        Command,
        Success,
        Warn,
        Error,
    }

    /// <summary>
    /// 同时写入 logs\launcher.log 与界面的事件源。
    /// 事件在调用线程上触发，界面订阅者负责切回 UI 线程。
    /// </summary>
    internal sealed class Log
    {
        private const long MaxBytes = 4L * 1024 * 1024;

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private readonly object _gate = new object();
        private readonly string _filePath;
        private readonly bool _echoToConsole;

        internal Log(string logDirectory, bool echoToConsole)
        {
            _echoToConsole = echoToConsole;
            try
            {
                Directory.CreateDirectory(logDirectory);
                _filePath = Path.Combine(logDirectory, "launcher.log");
                if (File.Exists(_filePath) && new FileInfo(_filePath).Length > MaxBytes)
                {
                    string rotated = Path.Combine(logDirectory, "launcher.1.log");
                    if (File.Exists(rotated))
                    {
                        File.Delete(rotated);
                    }

                    File.Move(_filePath, rotated);
                }
            }
            catch (IOException)
            {
                // 日志目录不可写时仍然保留界面输出，只是没有落盘文件。
                _filePath = null;
            }
            catch (UnauthorizedAccessException)
            {
                _filePath = null;
            }
        }

        internal string FilePath
        {
            get { return _filePath; }
        }

        internal event Action<LogLevel, string> Entry;

        internal void Info(string message)
        {
            Write(LogLevel.Info, message);
        }

        internal void Command(string commandLine)
        {
            Write(LogLevel.Command, "> " + commandLine);
        }

        internal void Success(string message)
        {
            Write(LogLevel.Success, message);
        }

        internal void Warn(string message)
        {
            Write(LogLevel.Warn, message);
        }

        internal void Error(string message)
        {
            Write(LogLevel.Error, message);
        }

        internal void Blank()
        {
            Write(LogLevel.Info, string.Empty);
        }

        private void Write(LogLevel level, string message)
        {
            if (_filePath != null)
            {
                string line = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}",
                    DateTime.Now,
                    level.ToString().ToUpperInvariant(),
                    message);
                lock (_gate)
                {
                    try
                    {
                        File.AppendAllText(_filePath, line + Environment.NewLine, Utf8);
                    }
                    catch (IOException)
                    {
                        // 磁盘写入失败不影响启动流程，界面输出仍然可用。
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }

            Action<LogLevel, string> handler = Entry;
            if (handler != null)
            {
                handler(level, message);
            }

            if (_echoToConsole)
            {
                ConsoleBridge.WriteLine(message);
            }
        }
    }
}
