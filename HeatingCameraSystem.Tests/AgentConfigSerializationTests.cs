using System.Buffers;
using HeatingCameraSystem.Core.Models;
using NATS.Client.Serializers.Json;

namespace HeatingCameraSystem.Tests;

/// <summary>
/// Guards the Phase 2 Master↔AgentUI remote-config wire against the flagged risk that
/// <see cref="CameraDescriptor"/> (a positional record) fails to survive NATS JSON serialization.
/// These tests drive the EXACT serializer <see cref="NatsCommunicationService"/> uses
/// (<see cref="NatsJsonSerializerRegistry.Default"/>). Because the on-wire transport is a lossless
/// byte copy, a self round-trip through this serializer is complete proof of config data integrity —
/// no live NATS server required.
/// </summary>
public class AgentConfigSerializationTests
{
    private static T RoundTrip<T>(T value)
    {
        var reg = NatsJsonSerializerRegistry.Default;
        var writer = new ArrayBufferWriter<byte>();
        reg.GetSerializer<T>().Serialize(writer, value);
        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);
        T? result = reg.GetDeserializer<T>().Deserialize(buffer);
        Assert.NotNull(result);
        return result!;
    }

    private static AgentConfigSnapshot SampleSnapshot() => new()
    {
        SimulationMode = true,
        NatsUrl = "nats://10.0.0.42:4222",
        StoragePath = @"D:\Captures\Bay7",
        HeartbeatSeconds = 9,
        CaptureImageFormat = CaptureImageFormat.Tiff16, // non-default enum value
        CaptureBurstCount = 5,
        Cameras =
        {
            // all nullable fields populated
            new CameraDescriptor("Agent_1", 3, "좌측 흑체", "COM7", "FLIR-A"),
            // nullable fields left null (default) — proves positional-record defaults survive
            new CameraDescriptor("Agent_2", 4, "우측 흑체"),
        }
    };

    private static void AssertSnapshotEqual(AgentConfigSnapshot expected, AgentConfigSnapshot actual)
    {
        Assert.Equal(expected.SimulationMode, actual.SimulationMode);
        Assert.Equal(expected.NatsUrl, actual.NatsUrl);
        Assert.Equal(expected.StoragePath, actual.StoragePath);
        Assert.Equal(expected.HeartbeatSeconds, actual.HeartbeatSeconds);
        Assert.Equal(expected.CaptureImageFormat, actual.CaptureImageFormat);
        Assert.Equal(expected.CaptureBurstCount, actual.CaptureBurstCount);

        Assert.Equal(expected.Cameras.Count, actual.Cameras.Count);
        for (int i = 0; i < expected.Cameras.Count; i++)
        {
            // record value-equality proves every field (incl. nullable) round-tripped intact
            Assert.Equal(expected.Cameras[i], actual.Cameras[i]);
        }
    }

    [Fact]
    public void AgentConfigApplyMessage_RoundTrips_WithCameraDescriptors()
    {
        var original = new AgentConfigApplyMessage
        {
            AgentId = "Agent_1",
            Config = SampleSnapshot(),
            Timestamp = new DateTime(2026, 7, 28, 12, 34, 56, DateTimeKind.Utc)
        };

        AgentConfigApplyMessage round = RoundTrip(original);

        Assert.Equal(original.AgentId, round.AgentId);
        Assert.Equal(original.Timestamp, round.Timestamp);
        AssertSnapshotEqual(original.Config, round.Config);
    }

    [Fact]
    public void AgentConfigSnapshotMessage_RoundTrips_WithCameraDescriptors()
    {
        var original = new AgentConfigSnapshotMessage
        {
            AgentId = "Agent_2",
            Config = SampleSnapshot(),
            Timestamp = new DateTime(2026, 7, 28, 1, 2, 3, DateTimeKind.Utc)
        };

        AgentConfigSnapshotMessage round = RoundTrip(original);

        Assert.Equal(original.AgentId, round.AgentId);
        Assert.Equal(original.Timestamp, round.Timestamp);
        AssertSnapshotEqual(original.Config, round.Config);
    }

    [Fact]
    public void CameraDescriptor_NullableFields_SurviveRoundTrip()
    {
        var withNulls = new CameraDescriptor("Agent_9", 0, "no-serial-no-name");
        var withValues = new CameraDescriptor("Agent_8", 2, "full", "COM12", "Device-XYZ");

        Assert.Equal(withNulls, RoundTrip(withNulls));
        Assert.Equal(withValues, RoundTrip(withValues));

        // explicit: null stays null, value stays value
        Assert.Null(RoundTrip(withNulls).SerialPortName);
        Assert.Null(RoundTrip(withNulls).DeviceName);
        Assert.Equal("COM12", RoundTrip(withValues).SerialPortName);
        Assert.Equal("Device-XYZ", RoundTrip(withValues).DeviceName);
    }
}
