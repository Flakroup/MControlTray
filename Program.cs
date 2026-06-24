using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MControlTray;

internal static class Program
{
    // --- MSI Center local control protocol ---
    // Reverse-engineered + behaviourally verified on board 15P2 (i7-14700HX), MSI Center 2.0.70.
    // The UI plugins are thin shells; clicking a User Scenario tile sends a localhost TCP message
    // to the MSI background service, which performs the real power/fan switch. We replay that exact
    // message. No EC poke (the EC 0xD2 byte is only a status mirror), no admin, no DCv2.exe UI.
    //
    // Frame = [DestID:int32 LE][0x00][0x12][UTF8 JSON]
    //   JSON = {"Index":N,"Performance":2,"Fan":0,"KB":-1,"PB":-1,"IsLoad":true}
    //   N: 1 = Extreme Performance, 2 = Balanced, 4 = ECO/Silent
    private const string ServerPortRegPath = @"SOFTWARE\Wow6432Node\MSI\MSI Center\Component\SDK";
    private const int DefaultServerPort = 32682; // MSI's hard-coded fallback when the reg value is absent
    private const int DestId = 104;              // User Scenario receiver component id
    private static readonly byte[] CmdPrefix = { 0x00, 0x12 };

    private sealed record Scenario(string Name, int Index, Color Color, string Glyph);

    private static readonly Scenario[] Scenarios =
    {
        new Scenario("Extreme Performance", 1, Color.OrangeRed,      "E"),
        new Scenario("Balanced",            2, Color.DodgerBlue,     "B"),
        new Scenario("ECO / Silent",        4, Color.MediumSeaGreen, "S"),
    };

    [STAThread]
    private static int Main(string[] args)
    {
        // Headless mode for shortcuts / scripting: MControlTray.exe --extreme|--balanced|--silent
        if (args.Length > 0)
            return RunHeadless(args[0]);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using TrayApp app = new TrayApp();
        Application.Run();
        return 0;
    }

    private static int RunHeadless(string arg)
    {
        Scenario? s = MatchScenario(arg);
        if (s is null)
        {
            Console.Error.WriteLine("Usage: MControlTray.exe [--extreme | --balanced | --silent]");
            return 2;
        }
        try
        {
            SendScenario(s.Index);
            Console.WriteLine("Sent: " + s.Name);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Failed: " + ex.Message);
            return 1;
        }
    }

    private static Scenario? MatchScenario(string arg)
    {
        switch (arg.TrimStart('-', '/').ToLowerInvariant())
        {
            case "extreme":
            case "e":
            case "1":
                return Scenarios[0];
            case "balanced":
            case "b":
            case "2":
                return Scenarios[1];
            case "silent":
            case "eco":
            case "s":
            case "4":
                return Scenarios[2];
            default:
                return null;
        }
    }

    // Sends the scenario-switch command to the MSI service over 127.0.0.1.
    internal static void SendScenario(int index)
    {
        string json = "{\"Index\":" + index + ",\"Performance\":2,\"Fan\":0,\"KB\":-1,\"PB\":-1,\"IsLoad\":true}";
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        byte[] frame = new byte[4 + CmdPrefix.Length + jsonBytes.Length];
        BitConverter.GetBytes(DestId).CopyTo(frame, 0);
        CmdPrefix.CopyTo(frame, 4);
        jsonBytes.CopyTo(frame, 4 + CmdPrefix.Length);

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
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(ServerPortRegPath);
            if (key?.GetValue("Server Port") is int p && p > 10240)
                return p;
        }
        catch
        {
            // fall through to default
        }
        return DefaultServerPort;
    }

    // System-tray UI.
    private sealed class TrayApp : IDisposable
    {
        private static readonly string StatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MControlTray", "last.txt");

        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem[] _items;
        private IntPtr _iconHandle;

        public TrayApp()
        {
            _notifyIcon = new NotifyIcon { Visible = true, Text = "MControlTray" };

            ContextMenuStrip menu = new ContextMenuStrip();
            _items = new ToolStripMenuItem[Scenarios.Length];
            for (int i = 0; i < Scenarios.Length; i++)
            {
                Scenario s = Scenarios[i];
                ToolStripMenuItem item = new ToolStripMenuItem(s.Name) { Tag = s };
                item.Click += (_, _) => Switch(s);
                _items[i] = item;
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exit = new ToolStripMenuItem("Zakończ");
            exit.Click += (_, _) =>
            {
                _notifyIcon.Visible = false;
                Application.Exit();
            };
            menu.Items.Add(exit);

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.MouseClick += OnMouseClick;

            UpdateUi(LoadLast());
        }

        private void OnMouseClick(object? sender, MouseEventArgs e)
        {
            // Left click also opens the menu (right click is handled by ContextMenuStrip itself).
            if (e.Button != MouseButtons.Left)
                return;
            MethodInfo? show = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
            show?.Invoke(_notifyIcon, null);
        }

        private void Switch(Scenario s)
        {
            try
            {
                SendScenario(s.Index);
                SaveLast(s.Index);
                UpdateUi(s.Index);
                _notifyIcon.ShowBalloonTip(1500, "MControlTray", "Przełączono: " + s.Name, ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _notifyIcon.ShowBalloonTip(4000, "MControlTray - błąd",
                    "Nie udało się przełączyć. Czy działa usługa MSI Center?\n" + ex.Message,
                    ToolTipIcon.Error);
            }
        }

        private void UpdateUi(int index)
        {
            Scenario? active = Array.Find(Scenarios, x => x.Index == index);
            foreach (ToolStripMenuItem item in _items)
                item.Checked = ((Scenario)item.Tag!).Index == index;

            Icon icon = MakeIcon(active?.Color ?? Color.Gray, active?.Glyph ?? "?", out IntPtr newHandle);
            _notifyIcon.Icon = icon;
            if (_iconHandle != IntPtr.Zero)
                DestroyIcon(_iconHandle);
            _iconHandle = newHandle;

            _notifyIcon.Text = active is null ? "MControlTray" : "MControlTray: " + active.Name;
        }

        private static Icon MakeIcon(Color color, string glyph, out IntPtr handle)
        {
            using Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush brush = new SolidBrush(color))
                    g.FillEllipse(brush, 2, 2, 27, 27);
                using (Pen pen = new Pen(Color.FromArgb(180, 255, 255, 255), 1.5f))
                    g.DrawEllipse(pen, 2, 2, 27, 27);
                using (Font font = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                })
                    g.DrawString(glyph, font, Brushes.White, new RectangleF(0, 0, 32, 32), sf);
            }
            handle = bmp.GetHicon();
            return Icon.FromHandle(handle);
        }

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

        public void Dispose()
        {
            _notifyIcon.Dispose();
            if (_iconHandle != IntPtr.Zero)
                DestroyIcon(_iconHandle);
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
