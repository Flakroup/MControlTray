using System;
using System.Text;
using MControlTray;
using Shouldly;
using Xunit;

namespace MControlTray.Tests;

public sealed class MsiProtocolTests
{
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
