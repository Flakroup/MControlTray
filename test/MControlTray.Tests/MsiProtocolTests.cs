using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using MControlTray;
using Shouldly;
using Xunit;

namespace MControlTray.Tests;

public sealed class MsiProtocolTests
{
    [Theory]
    [InlineData(0, 32683, 32683)]                                     // a sane registry value wins
    [InlineData(0, 65535, 65535)]
    [InlineData(0, 10240, MsiProtocol.DefaultServerPort)]             // at or below the floor
    [InlineData(0, 0, MsiProtocol.DefaultServerPort)]
    [InlineData(0, -1, MsiProtocol.DefaultServerPort)]
    [InlineData(0, 65536, MsiProtocol.DefaultServerPort)]             // beyond a TCP port
    [InlineData(2, 32683, MsiProtocol.DefaultServerPort)]             // registry read failed
    public void ChoosePort_only_accepts_a_usable_registry_value(int registryResult, int value, int expected)
    {
        MsiProtocol.ChoosePort(registryResult, value).ShouldBe(expected);
    }

    [Fact]
    public async Task Send_writes_the_frame_to_the_given_port()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            byte[] expected = MsiProtocol.BuildFrame(Scenarios.SilentIndex);
            Task<byte[]> served = Task.Run(async () =>
            {
                using TcpClient client = await listener.AcceptTcpClientAsync();
                using NetworkStream stream = client.GetStream();
                byte[] received = await ReadExactlyAsync(stream, expected.Length);
                await stream.WriteAsync(new byte[] { 1 }); // the service's ack
                return received;
            });

            MsiProtocol.Send(Scenarios.SilentIndex, ((IPEndPoint)listener.LocalEndpoint).Port);

            (await served).ShouldBe(expected);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Send_tolerates_a_server_that_closes_without_acking()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int length = MsiProtocol.BuildFrame(Scenarios.BalancedIndex).Length;
            Task served = Task.Run(async () =>
            {
                using TcpClient client = await listener.AcceptTcpClientAsync();
                using NetworkStream stream = client.GetStream();
                await ReadExactlyAsync(stream, length);
            });

            Should.NotThrow(() => MsiProtocol.Send(Scenarios.BalancedIndex, ((IPEndPoint)listener.LocalEndpoint).Port));

            await served;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
            if (read == 0)
                break;
            offset += read;
        }
        return buffer;
    }

    [Fact]
    public void BuildFrame_writes_the_destination_id_as_little_endian_int32()
    {
        byte[] frame = MsiProtocol.BuildFrame(Scenarios.BalancedIndex);

        BitConverter.ToInt32(frame, 0).ShouldBe(MsiProtocol.DestId);
        frame[0].ShouldBe((byte)104);
        frame[1].ShouldBe((byte)0);
        frame[2].ShouldBe((byte)0);
        frame[3].ShouldBe((byte)0);
    }

    [Fact]
    public void BuildFrame_writes_the_two_byte_header_before_the_payload()
    {
        byte[] frame = MsiProtocol.BuildFrame(Scenarios.ExtremeIndex);

        frame[4].ShouldBe((byte)0x00);
        frame[5].ShouldBe((byte)0x12);
    }

    [Theory]
    [InlineData(Scenarios.ExtremeIndex)]
    [InlineData(Scenarios.BalancedIndex)]
    [InlineData(Scenarios.SilentIndex)]
    public void BuildFrame_carries_the_scenario_index_in_the_json_payload(int index)
    {
        byte[] frame = MsiProtocol.BuildFrame(index);

        string json = Encoding.UTF8.GetString(frame, 6, frame.Length - 6);
        json.ShouldBe("{\"Index\":" + index + ",\"Performance\":2,\"Fan\":0,\"KB\":-1,\"PB\":-1,\"IsLoad\":true}");
        frame.Length.ShouldBe(6 + Encoding.UTF8.GetByteCount(json));
    }
}
