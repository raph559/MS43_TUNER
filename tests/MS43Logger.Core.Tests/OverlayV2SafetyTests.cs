using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

public sealed class OverlayV2SafetyTests
{

    [Fact]
    public void AnUnknownRefusalReason_IsNotAcceptedAsAValidResponse()
    {
        Assert.Throws<InvalidDataException>(() => OverlayContract.Refusal(OverlayContract.Frame(new byte[] { 0xB1, 0xFF })));
    }

    private sealed class ChangeWrite(IDs2Line inner, bool permutation) : IDs2Line
    {
        public byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400)
        {
            var bytes = payload.ToArray();
            if (bytes is [0x6D, 0x02, ..] && bytes.Length >= 7)
            {
                if (permutation)
                {
                    (bytes[5], bytes[6]) = (bytes[6], bytes[5]);
                }
                else
                {
                    bytes[5] ^= 1;
                }
            }

            return inner.Request(bytes, listenMilliseconds);
        }
    }

    private sealed class ChangeReply(IDs2Line inner, Func<byte[], byte[], byte[]> change) : IDs2Line
    {
        public byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400) =>
            change(payload.ToArray(), inner.Request(payload, listenMilliseconds));
    }
}
