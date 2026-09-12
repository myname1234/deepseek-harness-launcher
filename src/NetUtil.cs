using System;
using System.Net;
using System.Net.Sockets;

namespace DshLauncher
{
    /// <summary>本地端口探测：Web 服务固定监听 127.0.0.1。</summary>
    internal static class NetUtil
    {
        /// <summary>探测回环地址上的端口是否已有服务监听。</summary>
        internal static bool IsPortListening(int port)
        {
            if (port <= 0 || port > 65535)
            {
                return false;
            }

            TcpClient client = new TcpClient();
            try
            {
                IAsyncResult pending = client.BeginConnect(IPAddress.Loopback, port, null, null);
                if (!pending.AsyncWaitHandle.WaitOne(500))
                {
                    return false;
                }

                client.EndConnect(pending);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            finally
            {
                client.Close();
            }
        }

        /// <summary>从 startPort 起找一个空闲端口；找不到时返回 0。</summary>
        internal static int FindFreePort(int startPort)
        {
            for (int port = Math.Max(1, startPort); port < Math.Min(65535, startPort + 50); port++)
            {
                if (!IsPortListening(port))
                {
                    return port;
                }
            }

            return 0;
        }
    }
}
