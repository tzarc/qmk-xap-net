// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Xap.Tests;

/// <summary>
/// In-memory <see cref="IXapTransport"/> fake for exercising the generated
/// <c>XapClient</c>'s framing and receive-loop correlation without any real HID transport.
/// Records every frame handed to <see cref="SendAsync"/> verbatim (so tests can assert on
/// exactly what <c>BuildRequest</c>-produced bytes look like) and lets a test queue up canned
/// response frames to be yielded from <see cref="ReceiveAsync"/>.
/// </summary>
public sealed class TestTransport : IXapTransport
{
    private readonly Channel<RawMessage> _incoming = Channel.CreateUnbounded<RawMessage>();

    public List<byte[]> SentFrames { get; } = [];

    public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        SentFrames.Add(data.ToArray());
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<RawMessage> ReceiveAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (RawMessage msg in _incoming.Reader.ReadAllAsync(ct))
            yield return msg;
    }

    /// <summary>Queues a raw response frame to be yielded from <see cref="ReceiveAsync"/>.</summary>
    public void QueueResponse(byte[] frame) => _incoming.Writer.TryWrite(new RawMessage(frame));

    /// <summary>Completes the incoming channel, ending <see cref="ReceiveAsync"/>'s enumeration.</summary>
    public void Complete() => _incoming.Writer.TryComplete();

    /// <summary>Faults the incoming channel: <see cref="ReceiveAsync"/> throws <paramref name="ex"/>.</summary>
    public void Fault(Exception ex) => _incoming.Writer.TryComplete(ex);

    /// <summary>
    /// Builds a response frame as token(2 LE) + flags(1) + length(1) + payload -- the wire shape
    /// the generated XapClient's receive loop is expected to parse. This is a distinct byte
    /// layout from BuildRequest's (length comes after flags here, not right after the token), so
    /// it can't be cross-checked against BuildRequest directly; its own correctness is asserted
    /// directly in <see cref="ClientInfraTests.BuildResponseFrame_ProducesExpectedBytes"/>.
    /// </summary>
    public static byte[] BuildResponseFrame(ushort token, byte flags, byte[] payload)
    {
        byte[] frame = new byte[4 + payload.Length];
        frame[0] = (byte)(token & 0xFF);
        frame[1] = (byte)((token >> 8) & 0xFF);
        frame[2] = flags;
        frame[3] = (byte)payload.Length;
        payload.CopyTo(frame, 4);
        return frame;
    }
}
