using System.Net;
using System.Net.Sockets;

namespace SyncPlayer;

static class PeerAddress
{
    public static IPEndPoint Parse(string address)
    {
        var parts = address.Trim().Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int port) || port < 1 || port > 65535)
            throw new ArgumentException("地址格式应为 IPv4地址:端口，例如 192.168.1.20:5000");
        if (!IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("请输入有效的 IPv4 地址");
        return new IPEndPoint(ip, port);
    }
}
