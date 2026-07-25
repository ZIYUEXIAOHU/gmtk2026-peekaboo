using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

/// <summary>局域网发现地址解析：本机互连时避免连到虚拟网卡 IP。</summary>
public static class LanAddressUtil
{
    /// <summary>
    /// 若发现到的 IP 其实是本机某一网卡地址，则改用 127.0.0.1，
    /// 避免双编辑器/ParrelSync 连上 Hyper-V/WSL 的 172.x 却 TCP 不通。
    /// </summary>
    public static string ResolveClientConnectAddress(string discoveredIp)
    {
        if (string.IsNullOrWhiteSpace(discoveredIp))
            return discoveredIp;

        string trimmed = discoveredIp.Trim();
        if (trimmed == "127.0.0.1" || trimmed == "::1" ||
            trimmed.Equals("localhost", System.StringComparison.OrdinalIgnoreCase))
            return "127.0.0.1";

        if (!IPAddress.TryParse(trimmed, out IPAddress target))
            return trimmed;

        if (IPAddress.IsLoopback(target))
            return "127.0.0.1";

        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                foreach (UnicastIPAddressInformation uni in nic.GetIPProperties().UnicastAddresses)
                {
                    if (uni.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    if (uni.Address.Equals(target))
                        return "127.0.0.1";
                }
            }
        }
        catch
        {
            // 忽略枚举失败，沿用原地址
        }

        return trimmed;
    }
}
