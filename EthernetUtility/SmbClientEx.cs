using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NetUtility
{
    /// <summary>
    /// 网络共享访问管理：
    /// - 支持以指定的域/用户名/密码临时连接到 \\server\share
    /// - 支持使用 using 块自动断开
    /// - 适合在访问网络共享文件夹前先建立会话
    /// </summary>
    public class SmbClientEx
    {
        private const int RESOURCETYPE_DISK = 0x00000001;

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetAddConnection2(
            ref NETRESOURCE netResource,
            string? password,
            string? username,
            int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetCancelConnection2(
            string name,
            int flags,
            bool force);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string? lpLocalName;
            public string lpRemoteName;
            public string? lpComment;
            public string? lpProvider;
        }

        /// <summary>
        /// 表示一个已建立的网络共享会话，可以在 using 块结束时自动断开
        /// </summary>
        public sealed class NetworkShareSession : IDisposable
        {
            private readonly string _remotePath;
            private bool _disposed;

            internal NetworkShareSession(string remotePath)
            {
                _remotePath = remotePath;
            }

            /// <summary>
            /// 手动断开连接
            /// </summary>
            public void Disconnect(bool force = false)
            {
                if (_disposed) return;

                // flags: 0 = no update profile
                WNetCancelConnection2(_remotePath, 0, force);
                _disposed = true;
            }

            public void Dispose()
            {
                Disconnect(force: false);
                GC.SuppressFinalize(this);
            }
        }

        /// <summary>
        /// 连接到网络共享（例如：\\SERVER\Share），返回一个会话对象
        /// 使用完成后调用 Dispose 或使用 using 块自动断开
        /// </summary>
        /// <param name="remotePath">UNC 路径，如 \\SERVER\Share</param>
        /// <param name="userName">用户名，可以是 user 或 domain\\user 形式</param>
        /// <param name="password">密码</param>
        /// <param name="persistent">是否保存凭据（一般工具不建议持久化，默认 false）</param>
        public NetworkShareSession Connect(string remotePath, string userName, string password, bool persistent = false)
        {
            if (string.IsNullOrWhiteSpace(remotePath))
                throw new ArgumentException("远程路径不能为空", nameof(remotePath));
            if (string.IsNullOrWhiteSpace(userName))
                throw new ArgumentException("用户名不能为空", nameof(userName));

            var nr = new NETRESOURCE
            {
                dwScope = 0,
                dwType = RESOURCETYPE_DISK,
                dwDisplayType = 0,
                dwUsage = 0,
                lpLocalName = null,           // 不映射盘符，只建立会话
                lpRemoteName = remotePath,
                lpComment = null,
                lpProvider = null
            };

            int flags = persistent ? 1 : 0;   // CONNECT_UPDATE_PROFILE = 0x00000001

            int result = WNetAddConnection2(ref nr, password, userName, flags);
            if (result != 0)
            {
                throw new Win32Exception(result, $"连接到网络共享失败: {remotePath}, 错误码: {result}");
            }

            return new NetworkShareSession(remotePath);
        }

        /// <summary>
        /// 仅断开当前进程建立的到指定共享路径的连接
        /// </summary>
        public void Disconnect(string remotePath, bool force = false)
        {
            if (string.IsNullOrWhiteSpace(remotePath))
                throw new ArgumentException("远程路径不能为空", nameof(remotePath));

            WNetCancelConnection2(remotePath, 0, force);
        }

        /// <summary>
        /// 便捷静态方法：在 using 代码块中临时连接到网络共享
        /// </summary>
        public static NetworkShareSession Use(string remotePath, string userName, string password, bool persistent = false)
        {
            var mgr = new SmbClientEx();
            return mgr.Connect(remotePath, userName, password, persistent);
        }
    }
}
