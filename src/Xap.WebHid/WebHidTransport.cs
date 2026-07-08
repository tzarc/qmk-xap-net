// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Channels;

namespace Xap.WebHid;

/// <summary>
/// WebHID-backed <see cref="IXapTransport"/> for browser-wasm apps -- the WebHID counterpart to
/// Xap.Hid's XapHidDevice. WebHID keeps the report id out of the payload in both directions
/// (device.sendReport(reportId, data) on write, event.data on the 'inputreport' event), so unlike
/// XapHidDevice this never needs to add or strip a placeholder byte.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class WebHidTransport : IXapTransport, IAsyncDisposable
{
    // The device's declared output report size (confirmed via device.collections: reportCount=64,
    // reportSize=8 bits). BuildRequest()'s frame is variable-length (token+length+route+payload),
    // but sendReport() must always transfer the device's full, fixed-size report -- a short write
    // was confirmed via strace to reach the kernel as a 6-byte transfer instead of 64, which the
    // firmware's fixed-size USB endpoint silently drops, made every request look like a timeout.
    private const int ReportSize = 64;

    private readonly Channel<RawMessage> _incoming = Channel.CreateUnbounded<RawMessage>();

    private WebHidTransport()
    {
        if (Interlocked.CompareExchange(ref _current, this, null) is not null)
        {
            // RequestAsync already opened (and replaced) the JS module's single device slot for
            // this request; close it rather than leak the open handle.
            // ponytail: the prior transport's JS device reference is gone either way -- its
            // writes fail loudly from here on. Per-instance JS device tracking if concurrent
            // transports ever become a real use case.
            CloseJs();
            throw new InvalidOperationException("Another WebHidTransport is already active. Dispose it before creating a new one.");
        }
    }

    // JSExport methods must be static, so the one active transport is tracked here for
    // OnInputReport to route into -- fine for this minimal example's one-device-at-a-time use,
    // since WebHID's device picker is inherently a one-at-a-time user gesture anyway.
    private static WebHidTransport? _current;

    /// <summary>
    /// Prompts the browser's device picker for a device matching usagePage/usage (must be called
    /// from a user gesture, e.g. a button click handler -- WebHID refuses requestDevice()
    /// otherwise), then opens it. Returns null if the user picked nothing.
    /// </summary>
    public static async Task<WebHidTransport?> RequestAsync(int usagePage, int usage)
        => await RequestDeviceJs(usagePage, usage).ConfigureAwait(false) ? new WebHidTransport() : null;

    public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        byte[] buffer = new byte[ReportSize];
        data.Span.CopyTo(buffer);
        WriteJs(buffer);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<RawMessage> ReceiveAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        while (await _incoming.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_incoming.Reader.TryRead(out RawMessage message))
                yield return message;
        }
    }

    /// <summary>Called from webhid.js's 'inputreport' listener with the raw report bytes.</summary>
    [JSExport]
    internal static void OnInputReport(byte[] data)
        => _current?._incoming.Writer.TryWrite(new RawMessage(data));

    public ValueTask DisposeAsync()
    {
        Interlocked.CompareExchange(ref _current, null, this);
        CloseJs();
        _incoming.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    [JSImport("requestDevice", "webhid")]
    private static partial Task<bool> RequestDeviceJs(int usagePage, int usage);

    [JSImport("write", "webhid")]
    private static partial void WriteJs(byte[] data);

    [JSImport("close", "webhid")]
    private static partial void CloseJs();
}
