using System.Buffers;
using HeatingCameraSystem.Core.Models;
using NATS.Client.Serializers.Json;

namespace HeatingCameraSystem.Tests;

/// <summary>
/// Guards the additive Master↔Agent camera-control wire: <see cref="CameraControlMessage"/> and
/// <see cref="CameraControlAckMessage"/> must survive the EXACT serializer
/// <see cref="NatsCommunicationService"/> uses (<see cref="NatsJsonSerializerRegistry.Default"/>).
/// Because the on-wire transport is a lossless byte copy, a self round-trip through this serializer
/// is complete proof of control-message integrity — no live NATS server required.
/// </summary>
public class CameraControlProtocolTests
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

    [Fact]
    public void CameraControlMessage_RoundTrips_AllEightOpValuesPreserved()
    {
        string[] allOps =
        {
            CameraControlOps.Run,
            CameraControlOps.Stop,
            CameraControlOps.ShutterOpen,
            CameraControlOps.ShutterClose,
            CameraControlOps.Capture,
            CameraControlOps.Nuc,
            CameraControlOps.SaveConfig,
            CameraControlOps.RefreshInfo,
        };

        Assert.Equal(8, allOps.Length);

        foreach (string op in allOps)
        {
            var original = new CameraControlMessage
            {
                AgentId = "Agent_3",
                CameraIndex = 2,
                Op = op,
                Timestamp = new DateTime(2026, 7, 29, 8, 15, 30, DateTimeKind.Utc)
            };

            CameraControlMessage round = RoundTrip(original);

            Assert.Equal(original.AgentId, round.AgentId);
            Assert.Equal(original.CameraIndex, round.CameraIndex);
            Assert.Equal(original.Op, round.Op);
            Assert.Equal(original.Timestamp, round.Timestamp);
        }
    }

    [Fact]
    public void CameraControlAckMessage_RoundTrips_SuccessAndMessagePreserved()
    {
        var success = new CameraControlAckMessage
        {
            AgentId = "Agent_4",
            CameraIndex = 1,
            Op = CameraControlOps.Capture,
            IsSuccess = true,
            Message = "캡처 완료",
            Timestamp = new DateTime(2026, 7, 29, 9, 0, 0, DateTimeKind.Utc)
        };
        var failure = new CameraControlAckMessage
        {
            AgentId = "Agent_5",
            CameraIndex = 0,
            Op = CameraControlOps.Nuc,
            IsSuccess = false,
            Message = "NUC 실패: 셔터 응답 없음",
            Timestamp = new DateTime(2026, 7, 29, 9, 5, 0, DateTimeKind.Utc)
        };

        CameraControlAckMessage roundSuccess = RoundTrip(success);
        CameraControlAckMessage roundFailure = RoundTrip(failure);

        Assert.True(roundSuccess.IsSuccess);
        Assert.Equal(success.Message, roundSuccess.Message);
        Assert.Equal(success.Op, roundSuccess.Op);
        Assert.Equal(success.CameraIndex, roundSuccess.CameraIndex);

        Assert.False(roundFailure.IsSuccess);
        Assert.Equal(failure.Message, roundFailure.Message);
        Assert.Equal(failure.Op, roundFailure.Op);
    }
}
