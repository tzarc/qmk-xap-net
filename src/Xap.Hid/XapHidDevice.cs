// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using HidApi;

[assembly: InternalsVisibleTo("Xap.Tests")]

namespace Xap.Hid;

/// <summary>
/// Represents a QMK XAP HID device (usage page 0xFF51, usage 0x0058). Opens the device on
/// construction and implements <see cref="IXapTransport"/> directly, so it can be handed straight
/// to <c>XapClient.CreateAsync</c>. Mirrors the shape of qmk_toolbox's own
/// <c>BaseHidDevice</c>/<c>HidConsoleDevice</c> (same property set, same <see cref="Match"/>/
/// <see cref="TryCreate"/> pattern) so this can be swapped in there as a package reference.
/// </summary>
public sealed class XapHidDevice : IXapTransport, IDisposable
{
    public const ushort TargetUsagePage = 0xFF51;
    public const ushort TargetUsage = 0x0058;

    private const int ReportSize = 64;

    public string ManufacturerString { get; }
    public string ProductString { get; }
    public ushort VendorId { get; }
    public ushort ProductId { get; }
    public ushort RevisionBcd { get; }

    private readonly Device _device;

    /// <summary>Internal so callers must go through <see cref="Match"/>/<see cref="TryCreate"/>.</summary>
    internal XapHidDevice(DeviceInfo deviceInfo)
    {
        ManufacturerString = deviceInfo.ManufacturerString ?? "";
        ProductString = deviceInfo.ProductString ?? "";
        VendorId = deviceInfo.VendorId;
        ProductId = deviceInfo.ProductId;
        RevisionBcd = deviceInfo.ReleaseNumber;
        _device = new Device(deviceInfo.Path);
    }

    public static bool Match(DeviceInfo d) => d.UsagePage == TargetUsagePage && d.Usage == TargetUsage;

    public static XapHidDevice? TryCreate(DeviceInfo d) => Match(d) ? new XapHidDevice(d) : null;

    public override string ToString() =>
        $"{ManufacturerString} {ProductString} ({VendorId:X4}:{ProductId:X4}:{RevisionBcd:X4})";

    /// <summary>
    /// Sends a 64-byte XAP frame. hidapi's write convention prepends a report-id byte to every
    /// buffer (0x00 here, since XAP -- like QMK's console -- doesn't use numbered reports); that's
    /// a hidapi-library convention, not part of the XAP wire format, so it's handled here rather
    /// than in Xap.Core/the generated client.
    /// </summary>
    public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        byte[] buffer = new byte[1 + ReportSize];
        data.Span.CopyTo(buffer.AsSpan(1));
        _device.Write(buffer);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Polls for incoming reports (hidapi has no hotplug-style read callback). Unlike
    /// <see cref="SendAsync"/>, this does NOT strip a leading byte: hidapi only requires (and
    /// synthesizes) a report-id placeholder on writes, never on reads, for report-id-less devices
    /// like this one. Confirmed against a real GeebBoards Macropad v2: an earlier version of this
    /// method stripped byte 0 here too, which shifted a byte-for-byte-correct, on-time response by
    /// one position and corrupted its token, making every request appear to time out even though
    /// the firmware always answered immediately.
    /// </summary>
    public async IAsyncEnumerable<RawMessage> ReceiveAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        byte[] buffer = new byte[ReportSize];
        while (!ct.IsCancellationRequested)
        {
            int bytesRead;
            try
            {
                bytesRead = _device.ReadTimeout(buffer, 100);
            }
            catch (HidException)
            {
                yield break; // device disconnected
            }

            if (bytesRead <= 0)
                continue; // timeout

            yield return new RawMessage(ExtractFrame(buffer, bytesRead));

            await Task.Yield();
        }
    }

    /// <summary>Pulled out of <see cref="ReceiveAsync"/> so the framing rule above is unit-testable
    /// without a real HID device.</summary>
    internal static byte[] ExtractFrame(byte[] buffer, int bytesRead) => buffer.AsSpan(0, bytesRead).ToArray();

    public void Dispose() => _device.Dispose();
}
