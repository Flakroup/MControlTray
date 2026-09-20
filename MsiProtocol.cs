using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace MControlTray;

// --- MSI Center local control protocol ---
// Reverse-engineered + verified on board 15P2 (i7-14700HX), MSI Center 2.0.70.
// Clicking a User Scenario tile in MSI Center sends a localhost TCP message to the
// MSI background service, which performs the real power/fan switch. We replay it.
//   Frame = [DestID:int32 LE = 104][0x00][0x12][UTF8 JSON]
//   JSON  = {"Index":N,"Performance":2,"Fan":0,"KB":-1,"PB":-1,"IsLoad":true}
//   N: 1 = Extreme Performance, 2 = Balanced, 4 = ECO/Silent
internal static class MsiProtocol
{
    public const int DestId = 104;
    public const int DefaultServerPort = 32682;

    public static byte[] BuildFrame(int index)
    {
        string json = "{\"Index\":" + index + ",\"Performance\":2,\"Fan\":0,\"KB\":-1,\"PB\":-1,\"IsLoad\":true}";
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        byte[] frame = new byte[4 + 2 + jsonBytes.Length];
        BitConverter.GetBytes(DestId).CopyTo(frame, 0);
        frame[4] = 0x00;
        frame[5] = 0x12;
        jsonBytes.CopyTo(frame, 6);
        return frame;
    }

    public static void Send(int index)
    {
        byte[] frame = BuildFrame(index);
        using TcpClient client = new TcpClient { SendTimeout = 3000, ReceiveTimeout = 3000 };
        client.Connect("127.0.0.1", GetServerPort());
        using NetworkStream ns = client.GetStream();
        ns.Write(frame, 0, frame.Length);
        ns.Flush();
        try
        {
            ns.ReadByte(); // drain the service's "1" ack (best-effort)
        }
        catch (Exception)
        {
            // ack is optional
        }
    }

    public static int GetServerPort()
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
        catch (Exception)
        {
            // fall through
        }
        return DefaultServerPort;
    }

    private static readonly IntPtr HKEY_LOCAL_MACHINE = unchecked((IntPtr)0x80000002L);
    private const uint RRF_RT_REG_DWORD = 0x00000010;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegGetValueW(IntPtr hkey, string subKey, string value, uint flags,
        out uint type, ref int data, ref uint cbData);
}
