// spen-mouse receiver. Listens on UDP 51516 for "M dx dy" / "B down" / "B up" lines
// and drives the Windows cursor with SendInput. Usage: dotnet run [gain] [smoothing]
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

const int Port = 51516;
const int ClickMs = 250;              // button held shorter than this = click
double gain = args.Length > 0 ? double.Parse(args[0], CultureInfo.InvariantCulture) : 40;   // px per unit of delta
double smooth = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 0.5; // 0 = raw, 0.9 = very laggy
var ci = CultureInfo.InvariantCulture;

var udp = new UdpClient(Port);
var pressed = Stopwatch.StartNew();
bool down = false;
double fx = 0, fy = 0;   // filtered delta
double rx = 0, ry = 0;   // sub-pixel remainder
long packets = 0;

Console.WriteLine($"spen-mouse listening on udp/{Port}  gain={gain} smooth={smooth}  (Ctrl+C to quit)");
while (true)
{
    var ep = new IPEndPoint(IPAddress.Any, 0);
    byte[] buf;
    try { buf = udp.Receive(ref ep); } catch (SocketException) { continue; }
    packets++;
    foreach (var line in Encoding.UTF8.GetString(buf).Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        var p = line.Trim().Split(' ');
        if (p[0] == "M" && p.Length == 3
            && double.TryParse(p[1], NumberStyles.Float, ci, out var dx)
            && double.TryParse(p[2], NumberStyles.Float, ci, out var dy))
        {
            fx = smooth * fx + (1 - smooth) * dx;
            fy = smooth * fy + (1 - smooth) * dy;
            rx += fx * gain; ry += fy * gain;
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
        else Console.WriteLine($"? {line}");
    }
    if (packets % 200 == 0) Console.Write($"\r{packets} packets from {ep.Address}   ");
}

static class Mouse
{
    const uint MoveFlag = 0x0001, LeftDown = 0x0002, LeftUp = 0x0004;

    public static void Move(int dx, int dy) => Send(dx, dy, MoveFlag);
    public static void Click() { Send(0, 0, LeftDown); Send(0, 0, LeftUp); }

    static void Send(int dx, int dy, uint flags)
    {
        var input = new Input { type = 0, mi = new MouseInput { dx = dx, dy = dy, dwFlags = flags } };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) == 0)
            Console.WriteLine($"SendInput failed: {Marshal.GetLastWin32Error()}");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MouseInput { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    // INPUT is a union of MOUSEINPUT / KEYBDINPUT / HARDWAREINPUT; MOUSEINPUT is the largest, so this layout matches.
    [StructLayout(LayoutKind.Sequential)]
    struct Input { public uint type; public MouseInput mi; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint n, Input[] inputs, int size);
}
