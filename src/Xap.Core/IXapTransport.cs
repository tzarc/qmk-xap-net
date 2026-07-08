// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

namespace Xap;

public interface IXapTransport
{
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
    IAsyncEnumerable<RawMessage> ReceiveAsync(CancellationToken ct = default);
}

public readonly struct RawMessage(ReadOnlyMemory<byte> data)
{
    public ReadOnlyMemory<byte> Data { get; } = data;
}
