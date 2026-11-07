// The actual mouse: listens on UDP for "M dx dy" / "B down" / "B up" lines and drives the cursor with SendInput.
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

sealed class Receiver
{
    public const int Port = 51516;
    public volatile float Gain = 400;      // px per unit of delta; a full arm sweep is only ~0.1 units
    public volatile float Smooth = 0.5f;   // 0 = raw, 0.9 = very laggy
    public volatile bool FlipY = true;     // pen up is +dy, screen up is -y
    public const int ClickMs = 250;        // button held shorter than this = click

    public long Packets;
    public volatile string Sender = "";
    public DateTime LastPacket { get; private set; }

    readonly UdpClient udp = new(Port);
    readonly Stopwatch pressed = new();
    bool down;
    double fx, fy, rx, ry;   // filtered delta, sub-pixel remainder

    public void Start() => new Thread(Loop) { IsBackground = true, Name = "udp" }.Start();

    void Loop()
    {
        var ci = CultureInfo.InvariantCulture;
        while (true)
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            byte[] buf;
            try { buf = udp.Receive(ref ep); } catch (SocketException) { continue; }
            Packets++; Sender = ep.Address.ToString(); LastPacket = DateTime.UtcNow;
            foreach (var line in Encoding.UTF8.GetString(buf).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = line.Trim().Split(' ');
                if (p[0] == "M" && p.Length == 3
                    && double.TryParse(p[1], NumberStyles.Float, ci, out var dx)
                    && double.TryParse(p[2], NumberStyles.Float, ci, out var dy))
                {
                    double s = Smooth;
                    fx = s * fx + (1 - s) * dx;
                    fy = s * fy + (1 - s) * (FlipY ? -dy : dy);
                    rx += fx * Gain; ry += fy * Gain;
                    int ix = (int)rx, iy = (int)ry;   // truncate toward zero, keep the fraction
                    rx -= ix; ry -= iy;
                    if (ix != 0 || iy != 0) Mouse.Move(ix, iy);
                }
                else if (p[0] == "B" && p.Length == 2)
                {
                    if (p[1] == "down" && !down) { down = true; pressed.Restart(); fx = fy = 0; }
                    else if (p[1] == "up" && down)
                    {
                        down = false;
                        if (pressed.ElapsedMilliseconds < ClickMs) Mouse.Click();
                    }
                }
                else if (p[0] == "SPENMOUSE?")   // a phone asking directly, for Wi-Fi that blocks broadcast
                    try { udp.Send(Beacon.Message, Beacon.Message.Length, new IPEndPoint(ep.Address, Beacon.Port)); } catch (SocketException) { }
            }
        }
    }
}

// Shouts "SPENMOUSE <port> <name>" to the LAN once a second so the phone can list this machine instead of asking for an IP.
static class Beacon
{
    public const int Port = 51517;
    public static readonly byte[] Message = Encoding.UTF8.GetBytes($"SPENMOUSE {Receiver.Port} {Environment.MachineName}");

    public static void Start() => new Thread(() =>
    {
        using var udp = new UdpClient { EnableBroadcast = true };
        while (true)
        {
            foreach (var bcast in Net.BroadcastAddresses())
                try { udp.Send(Message, Message.Length, new IPEndPoint(bcast, Port)); } catch (SocketException) { }
            Thread.Sleep(1000);
        }
    }) { IsBackground = true, Name = "beacon" }.Start();
}

static class Net
{
    // Every IPv4 address on an interface that is up and not loopback, Wi-Fi first: what the user types if discovery fails.
    public static IEnumerable<(IPAddress ip, IPAddress mask, string nic)> Addresses() =>
        from nic in NetworkInterface.GetAllNetworkInterfaces()
        where nic.OperationalStatus == OperationalStatus.Up
           && (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
           && !nic.Name.StartsWith("vEthernet") && !nic.Description.Contains("Virtual")   // WSL, Hyper-V, VMware: not where the phone is
        orderby nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 descending
        from a in nic.GetIPProperties().UnicastAddresses
        where a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.")
        select (a.Address, a.IPv4Mask, nic.Name);

    public static IEnumerable<IPAddress> BroadcastAddresses()
    {
        foreach (var (ip, mask, _) in Addresses())
        {
            var i = ip.GetAddressBytes(); var m = mask.GetAddressBytes();
            for (int k = 0; k < 4; k++) i[k] = (byte)(i[k] | ~m[k]);
            yield return new IPAddress(i);
        }
    }
}

static class Mouse
{
    const uint MoveFlag = 0x0001, LeftDown = 0x0002, LeftUp = 0x0004;

    public static void Move(int dx, int dy) => Send(dx, dy, MoveFlag);
    public static void Click() { Send(0, 0, LeftDown); Send(0, 0, LeftUp); }

    static void Send(int dx, int dy, uint flags)
    {
        var input = new Input { type = 0, mi = new MouseInput { dx = dx, dy = dy, dwFlags = flags } };
        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MouseInput { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    // INPUT is a union of MOUSEINPUT / KEYBDINPUT / HARDWAREINPUT; MOUSEINPUT is the largest, so this layout matches.
    [StructLayout(LayoutKind.Sequential)]
    struct Input { public uint type; public MouseInput mi; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint n, Input[] inputs, int size);
}
