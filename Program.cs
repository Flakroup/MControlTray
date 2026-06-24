using System;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace MControlTray;

// Pure Win32 system-tray app (no WinForms/WPF) so it can publish as a tiny,
// dependency-free NativeAOT binary.
//
// --- MSI Center local control protocol ---
// Reverse-engineered + verified on board 15P2 (i7-14700HX), MSI Center 2.0.70.
// Clicking a User Scenario tile in MSI Center sends a localhost TCP message to the
// MSI background service, which performs the real power/fan switch. We replay it.
//   Frame = [DestID:int32 LE = 104][0x00][0x12][UTF8 JSON]
//   JSON  = {"Index":N,"Performance":2,"Fan":0,"KB":-1,"PB":-1,"IsLoad":true}
//   N: 1 = Extreme Performance, 2 = Balanced, 4 = ECO/Silent
internal static unsafe class Program
{
    private const int DestId = 104;
    private const int DefaultServerPort = 32682;

    private readonly struct Scenario
    {
        public readonly string Name;
        public readonly int Index;
        public readonly string Icon;
        public Scenario(string name, int index, string icon)
        {
            Name = name;
            Index = index;
            Icon = icon;
        }
    }

    private static readonly Scenario[] Scenarios =
    {
        new Scenario("Extreme Performance", 1, "e.ico"),
        new Scenario("Balanced",            2, "b.ico"),
        new Scenario("ECO / Silent",        4, "s.ico"),
    };

    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MControlTray", "last.txt");

    private static IntPtr _hwnd;
    private static int _current = -1;
    private static IntPtr _iconHandle;

    [STAThread]
    private static int Main(string[] args)
        => args.Length > 0 ? RunHeadless(args[0]) : RunTray();

    // ---------- headless (shortcuts / hotkeys) ----------
    private static int RunHeadless(string arg)
    {
        int idx = MatchIndex(arg);
        if (idx < 0)
            return 2;
        try
        {
            SendScenario(idx);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static int MatchIndex(string arg)
    {
        switch (arg.TrimStart('-', '/').ToLowerInvariant())
        {
            case "extreme":
            case "e":
            case "1":
                return 1;
            case "balanced":
            case "b":
            case "2":
                return 2;
            case "silent":
            case "eco":
            case "s":
            case "4":
                return 4;
            default:
                return -1;
        }
    }

    // ---------- core: replay MSI Center's switch command ----------
    internal static void SendScenario(int index)
    {
        string json = "{\"Index\":" + index + ",\"Performance\":2,\"Fan\":0,\"KB\":-1,\"PB\":-1,\"IsLoad\":true}";
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        byte[] frame = new byte[4 + 2 + jsonBytes.Length];
        BitConverter.GetBytes(DestId).CopyTo(frame, 0);
        frame[4] = 0x00;
        frame[5] = 0x12;
        jsonBytes.CopyTo(frame, 6);

        using TcpClient client = new TcpClient { SendTimeout = 3000, ReceiveTimeout = 3000 };
        client.Connect("127.0.0.1", GetServerPort());
        using NetworkStream ns = client.GetStream();
        ns.Write(frame, 0, frame.Length);
        ns.Flush();
        try
        {
            ns.ReadByte(); // drain the service's "1" ack (best-effort)
        }
        catch
        {
            // ack is optional
        }
    }

    private static int GetServerPort()
    {
        try
        {
            int data = 0;
            uint cb = 4;
            int rc = RegGetValueW(HKEY_LOCAL_MACHINE,
                @"SOFTWARE\Wow6432Node\MSI\MSI Center\Component\SDK", "Server Port",
                RRF_RT_REG_DWORD, out _, ref data, ref cb);
            if (rc == 0 && data > 10240)
                return data;
        }
        catch
        {
            // fall through
        }
        return DefaultServerPort;
    }

    // ---------- tray UI (Win32) ----------
    private static int RunTray()
    {
        IntPtr hInstance = GetModuleHandleW(null);
        fixed (char* clsName = "MControlTrayWnd")
        {
            WNDCLASSEXW wc = default;
            wc.cbSize = (uint)sizeof(WNDCLASSEXW);
            wc.lpfnWndProc = &WndProc;
            wc.hInstance = hInstance;
            wc.lpszClassName = clsName;
            RegisterClassExW(&wc);
            _hwnd = CreateWindowExW(0, clsName, clsName, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        }

        _current = LoadLast();
        AddTrayIcon();

        MSG msg;
        while (GetMessageW(&msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_TRAYICON:
                int mouse = (int)((long)lParam & 0xFFFF);
                if (mouse is WM_LBUTTONUP or WM_RBUTTONUP or WM_CONTEXTMENU)
                    ShowMenu();
                return IntPtr.Zero;
            case WM_DESTROY:
                RemoveTrayIcon();
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        for (int i = 0; i < Scenarios.Length; i++)
        {
            uint flags = MF_STRING | (Scenarios[i].Index == _current ? MF_CHECKED : 0u);
            fixed (char* t = Scenarios[i].Name)
                AppendMenuW(menu, flags, (UIntPtr)(uint)(CMD_BASE + i), t);
        }
        AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
        fixed (char* ex = "Zakończ")
            AppendMenuW(menu, MF_STRING, (UIntPtr)CMD_EXIT, ex);

        POINT pt;
        GetCursorPos(&pt);
        SetForegroundWindow(_hwnd); // required so the menu closes on focus loss
        uint cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);

        if (cmd == CMD_EXIT)
        {
            DestroyWindow(_hwnd);
            return;
        }
        if (cmd >= CMD_BASE && cmd < CMD_BASE + Scenarios.Length)
            Apply(Scenarios[(int)cmd - CMD_BASE]);
    }

    private static void Apply(Scenario s)
    {
        try
        {
            SendScenario(s.Index);
            _current = s.Index;
            SaveLast(s.Index);
            UpdateTrayIcon(s.Icon, "MControlTray: " + s.Name);
            ShowBalloon("MControlTray", "Przełączono: " + s.Name, error: false);
        }
        catch
        {
            ShowBalloon("MControlTray - błąd", "Nie udało się przełączyć. Czy działa usługa MSI Center?", error: true);
        }
    }

    // ---------- tray icon plumbing ----------
    private static NOTIFYICONDATAW NewNid()
    {
        NOTIFYICONDATAW nid = default;
        nid.cbSize = (uint)sizeof(NOTIFYICONDATAW);
        nid.hWnd = _hwnd;
        nid.uID = TRAY_ID;
        return nid;
    }

    private static void AddTrayIcon()
    {
        Scenario? active = FindByIndex(_current);
        _iconHandle = LoadIconResource(active?.Icon ?? "q.ico");
        NOTIFYICONDATAW nid = NewNid();
        nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        nid.uCallbackMessage = WM_TRAYICON;
        nid.hIcon = _iconHandle;
        CopyStr(nid.szTip, 128, active is null ? "MControlTray" : "MControlTray: " + active.Value.Name);
        Shell_NotifyIconW(NIM_ADD, &nid);
    }

    private static void UpdateTrayIcon(string iconRes, string tip)
    {
        IntPtr newIcon = LoadIconResource(iconRes);
        NOTIFYICONDATAW nid = NewNid();
        nid.uFlags = NIF_ICON | NIF_TIP;
        nid.hIcon = newIcon;
        CopyStr(nid.szTip, 128, tip);
        Shell_NotifyIconW(NIM_MODIFY, &nid);
        if (_iconHandle != IntPtr.Zero)
            DestroyIcon(_iconHandle);
        _iconHandle = newIcon;
    }

    private static void RemoveTrayIcon()
    {
        NOTIFYICONDATAW nid = NewNid();
        Shell_NotifyIconW(NIM_DELETE, &nid);
        if (_iconHandle != IntPtr.Zero)
            DestroyIcon(_iconHandle);
    }

    private static void ShowBalloon(string title, string text, bool error)
    {
        NOTIFYICONDATAW nid = NewNid();
        nid.uFlags = NIF_INFO;
        nid.dwInfoFlags = error ? NIIF_ERROR : NIIF_INFO;
        CopyStr(nid.szInfoTitle, 64, title);
        CopyStr(nid.szInfo, 256, text);
        Shell_NotifyIconW(NIM_MODIFY, &nid);
    }

    private static IntPtr LoadIconResource(string name)
    {
        try
        {
            using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (s != null)
            {
                byte[] ico = new byte[s.Length];
                s.ReadExactly(ico);
                // single-image .ico: ICONDIR(6) + ICONDIRENTRY(16); image size @14, offset @18
                int imageSize = BitConverter.ToInt32(ico, 14);
                int imageOffset = BitConverter.ToInt32(ico, 18);
                fixed (byte* p = ico)
                {
                    IntPtr h = CreateIconFromResourceEx((IntPtr)(p + imageOffset), (uint)imageSize, true, 0x00030000, 0, 0, 0);
                    if (h != IntPtr.Zero)
                        return h;
                }
            }
        }
        catch
        {
            // fall through to system icon
        }
        return LoadIconW(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
    }

    private static Scenario? FindByIndex(int index)
    {
        foreach (Scenario s in Scenarios)
            if (s.Index == index)
                return s;
        return null;
    }

    private static void CopyStr(char* dest, int cap, string s)
    {
        int n = Math.Min(s.Length, cap - 1);
        for (int i = 0; i < n; i++)
            dest[i] = s[i];
        dest[n] = '\0';
    }

    // ---------- state ----------
    private static int LoadLast()
    {
        try
        {
            if (File.Exists(StatePath) && int.TryParse(File.ReadAllText(StatePath).Trim(), out int v))
                return v;
        }
        catch
        {
            // ignore
        }
        return -1;
    }

    private static void SaveLast(int index)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, index.ToString());
        }
        catch
        {
            // ignore
        }
    }

    // ============================ Win32 interop ============================
    private const int WM_NULL = 0x0000;
    private const int WM_DESTROY = 0x0002;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int WM_APP = 0x8000;
    private const int WM_TRAYICON = WM_APP + 1;

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_INFO = 0x10;
    private const uint NIIF_INFO = 0x01, NIIF_ERROR = 0x03;

    private const uint MF_STRING = 0x0000, MF_CHECKED = 0x0008, MF_SEPARATOR = 0x0800;
    private const uint TPM_RIGHTBUTTON = 0x0002, TPM_NONOTIFY = 0x0080, TPM_RETURNCMD = 0x0100;

    private const uint TRAY_ID = 1;
    private const int CMD_BASE = 100;
    private const int CMD_EXIT = 200;

    private static readonly IntPtr HKEY_LOCAL_MACHINE = unchecked((IntPtr)0x80000002L);
    private const uint RRF_RT_REG_DWORD = 0x00000010;
    private const int IDI_APPLICATION = 32512;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uVersionOrTimeout;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(char* lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(WNDCLASSEXW* wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, char* className, char* windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(MSG* msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern int TranslateMessage(MSG* msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(MSG* msg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int code);

    [DllImport("user32.dll")]
    private static extern int DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int AppendMenuW(IntPtr menu, uint flags, UIntPtr id, char* item);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern int DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetCursorPos(POINT* pt);

    [DllImport("user32.dll")]
    private static extern int DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(IntPtr presbits, uint resSize, bool fIcon, uint ver,
        int cxDesired, int cyDesired, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegGetValueW(IntPtr hkey, string subKey, string value, uint flags,
        out uint type, ref int data, ref uint cbData);
}
