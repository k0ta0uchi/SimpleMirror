using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace SimpleMirror.Services;

/// <summary>
/// Apple Bonjour (dnssd.dll) および mDNS マルチキャストによる AirPlay サービス自動登録・告知マネージャー
/// </summary>
public class BonjourAirPlayRegistrar : IDisposable
{
    private IntPtr _airplaySdRef = IntPtr.Zero;
    private IntPtr _raopSdRef = IntPtr.Zero;
    private bool _isRegistered = false;
    private bool _isDisposed = false;
    private UdpClient? _mdnsBroadcaster;
    private System.Timers.Timer? _broadcastTimer;

    public bool IsBonjourAvailable { get; private set; }
    public string ActiveIpAddress { get; private set; } = "127.0.0.1";
    public string ActiveMacAddress { get; private set; } = "000000000000";

    // P/Invoke dnssd.dll
    private delegate void DNSServiceRegisterReply(
        IntPtr sdRef,
        uint flags,
        int errorCode,
        string name,
        string regtype,
        string domain,
        IntPtr context);

    [DllImport("dnssd.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern int DNSServiceRegister(
        out IntPtr sdRef,
        uint flags,
        uint interfaceIndex,
        string? serviceName,
        string regtype,
        string? domain,
        string? host,
        ushort port, // Network byte order
        ushort txtLen,
        byte[]? txtRecord,
        DNSServiceRegisterReply? callBack,
        IntPtr context);

    [DllImport("dnssd.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern void DNSServiceRefDeallocate(IntPtr sdRef);

    public BonjourAirPlayRegistrar()
    {
        CheckBonjourAvailability();
        ResolvePrimaryNetworkInfo();
    }

    private void CheckBonjourAvailability()
    {
        try
        {
            var testPtr = IntPtr.Zero;
            // dnssd.dll がロード可能か確認
            IsBonjourAvailable = File.Exists(Path.Combine(Environment.SystemDirectory, "dnssd.dll"));
        }
        catch
        {
            IsBonjourAvailable = false;
        }
    }

    private void ResolvePrimaryNetworkInfo()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                              !nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                              !nic.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) &&
                              !nic.Description.Contains("WSL", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var primaryNic = interfaces.FirstOrDefault(nic =>
                nic.GetIPProperties().UnicastAddresses.Any(u =>
                    u.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !u.Address.ToString().StartsWith("169.254.") &&
                    !u.Address.ToString().StartsWith("127."))) 
                ?? interfaces.FirstOrDefault();

            if (primaryNic != null)
            {
                var ip = primaryNic.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork && !u.Address.ToString().StartsWith("169.254."))?.Address;

                if (ip != null)
                {
                    ActiveIpAddress = ip.ToString();
                }

                var physical = primaryNic.GetPhysicalAddress().ToString();
                if (!string.IsNullOrEmpty(physical))
                {
                    ActiveMacAddress = physical;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BonjourAirPlayRegistrar] Resolve network info error: {ex.Message}");
        }
    }

    public bool RegisterServices(string serverName, int port = 7000)
    {
        UnregisterServices();
        ResolvePrimaryNetworkInfo();

        bool registered = false;

        if (IsBonjourAvailable)
        {
            try
            {
                registered = RegisterViaBonjour(serverName, port);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BonjourAirPlayRegistrar] dnssd register failed: {ex.Message}");
            }
        }

        // スタンドアロン mDNS ソケットマルチキャスト告知も同時に起動
        StartMdnsSocketBroadcaster(serverName, port);

        _isRegistered = registered;
        return registered;
    }

    private bool RegisterViaBonjour(string serverName, int port)
    {
        ushort portNetworkOrder = (ushort)IPAddress.HostToNetworkOrder((short)port);

        // 1. _airplay._tcp サービス登録
        // TXT Record 構築
        var airplayTxt = BuildAirPlayTxtRecord();
        int err1 = DNSServiceRegister(
            out _airplaySdRef,
            0,
            0,
            serverName,
            "_airplay._tcp",
            null,
            null,
            portNetworkOrder,
            (ushort)airplayTxt.Length,
            airplayTxt,
            null,
            IntPtr.Zero);

        // 2. _raop._tcp (AirTunes/Audio) サービス登録
        // サービス名は "MACADDRESS@ServerName"
        var formattedMac = FormatMacAddress(ActiveMacAddress);
        var raopName = $"{formattedMac}@{serverName}";
        var raopTxt = BuildRaopTxtRecord();

        int err2 = DNSServiceRegister(
            out _raopSdRef,
            0,
            0,
            raopName,
            "_raop._tcp",
            null,
            null,
            portNetworkOrder,
            (ushort)raopTxt.Length,
            raopTxt,
            null,
            IntPtr.Zero);

        Debug.WriteLine($"[BonjourAirPlayRegistrar] DNSServiceRegister AirPlay={err1}, RAOP={err2}");
        return err1 == 0 && err2 == 0;
    }

    private byte[] BuildAirPlayTxtRecord()
    {
        var entries = new List<string>
        {
            "model=AppleTV3,2",
            "flags=0x4",
            "srcvers=220.68",
            "features=0x5A7FFFF7,0x1E",
            "pk=b07727d6f6cd6e08b58ede525ec3cdeaa252ad9f683fedc4d1fac80f9b58097b",
            $"deviceid={FormatMacAddressWithColons(ActiveMacAddress)}",
            "vv=2"
        };

        return EncodeTxtRecord(entries);
    }

    private byte[] BuildRaopTxtRecord()
    {
        var entries = new List<string>
        {
            "tp=UDP",
            "sm=false",
            "sv=false",
            "ek=1",
            "et=0,1",
            "cn=0,1",
            "ch=2",
            "ss=16",
            "sr=44100",
            "vn=65537",
            "txtvers=1"
        };

        return EncodeTxtRecord(entries);
    }

    private byte[] EncodeTxtRecord(List<string> entries)
    {
        var ms = new MemoryStream();
        foreach (var entry in entries)
        {
            var bytes = Encoding.UTF8.GetBytes(entry);
            if (bytes.Length <= 255)
            {
                ms.WriteByte((byte)bytes.Length);
                ms.Write(bytes, 0, bytes.Length);
            }
        }
        return ms.ToArray();
    }

    private string FormatMacAddress(string mac)
    {
        return mac.Replace(":", "").Replace("-", "").ToUpperInvariant();
    }

    private string FormatMacAddressWithColons(string mac)
    {
        var clean = FormatMacAddress(mac);
        if (clean.Length == 12)
        {
            return string.Join(":", Enumerable.Range(0, 6).Select(i => clean.Substring(i * 2, 2)));
        }
        return "00:11:22:33:44:55";
    }

    private void StartMdnsSocketBroadcaster(string serverName, int port)
    {
        try
        {
            _broadcastTimer?.Stop();
            _mdnsBroadcaster?.Dispose();

            _mdnsBroadcaster = new UdpClient();
            _mdnsBroadcaster.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _mdnsBroadcaster.EnableBroadcast = true;

            var targetEndpoint = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);

            _broadcastTimer = new System.Timers.Timer(3000); // 3秒毎に定期アナウンス
            _broadcastTimer.Elapsed += (s, e) =>
            {
                try
                {
                    // mDNS 告知パケットを送信
                    var packet = BuildMdnsAnnouncementPacket(serverName, port);
                    _mdnsBroadcaster.Send(packet, packet.Length, targetEndpoint);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MdnsSocketBroadcaster] Broadcast error: {ex.Message}");
                }
            };
            _broadcastTimer.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BonjourAirPlayRegistrar] Socket broadcaster init error: {ex.Message}");
        }
    }

    private byte[] BuildMdnsAnnouncementPacket(string serverName, int port)
    {
        // DNS Packet Header (Standard Query Response, Authoritative)
        var ms = new MemoryStream();
        ms.Write([0x00, 0x00]); // ID 0
        ms.Write([0x84, 0x00]); // Flags: Response, Authoritative
        ms.Write([0x00, 0x00]); // Questions: 0
        ms.Write([0x00, 0x01]); // Answer RRs: 1
        ms.Write([0x00, 0x00]); // Authority RRs: 0
        ms.Write([0x00, 0x00]); // Additional RRs: 0

        // PTR Answer Record: _airplay._tcp.local
        WriteDnsName(ms, "_airplay._tcp.local");
        ms.Write([0x00, 0x0C]); // Type PTR (12)
        ms.Write([0x80, 0x01]); // Class IN (Cache flush)
        ms.Write([0x00, 0x00, 0x11, 0x94]); // TTL 4500s

        var nameBuf = new MemoryStream();
        WriteDnsName(nameBuf, $"{serverName}._airplay._tcp.local");
        var nameBytes = nameBuf.ToArray();

        ms.Write([(byte)(nameBytes.Length >> 8), (byte)(nameBytes.Length & 0xFF)]);
        ms.Write(nameBytes, 0, nameBytes.Length);

        return ms.ToArray();
    }

    private void WriteDnsName(MemoryStream ms, string name)
    {
        var parts = name.Split('.');
        foreach (var part in parts)
        {
            var bytes = Encoding.UTF8.GetBytes(part);
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }
        ms.WriteByte(0); // Root label
    }

    public void UnregisterServices()
    {
        _broadcastTimer?.Stop();
        _broadcastTimer?.Dispose();
        _broadcastTimer = null;

        _mdnsBroadcaster?.Dispose();
        _mdnsBroadcaster = null;

        if (_airplaySdRef != IntPtr.Zero)
        {
            try { DNSServiceRefDeallocate(_airplaySdRef); } catch { }
            _airplaySdRef = IntPtr.Zero;
        }

        if (_raopSdRef != IntPtr.Zero)
        {
            try { DNSServiceRefDeallocate(_raopSdRef); } catch { }
            _raopSdRef = IntPtr.Zero;
        }

        _isRegistered = false;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            UnregisterServices();
            _isDisposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
