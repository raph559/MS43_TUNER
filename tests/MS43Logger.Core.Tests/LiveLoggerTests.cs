using MS43Logger.Core;

namespace MS43Logger.Core.Tests;

public class LiveLoggerTests
{
    private sealed class FakeSource(params Func<byte[]>[] steps) : IPayloadSource
    {
        private int _next;

        public byte[] PollPayload() => _next < steps.Length ? steps[_next++]() : throw new OperationCanceledException();
    }

    private static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");

    private static byte[] Payload(byte rpmHigh)
    {
        var payload = new byte[73];
        payload[0x04] = rpmHigh;
        return payload;
    }

    private static Func<byte[]> Timeout() => () => throw new TimeoutException("no reply");

    [Theory]
    [InlineData(0, -225)]
    [InlineData(8000, 45)]
    [InlineData(4000, -90)]
    [InlineData(9000, 45)]
    [InlineData(-50, -225)]
    public void GaugeAngleClampsAndSweeps(double value, double angle)
    {
        Assert.Equal(angle, GaugeMath.Angle(0, 8000, value), 9);
    }
}
