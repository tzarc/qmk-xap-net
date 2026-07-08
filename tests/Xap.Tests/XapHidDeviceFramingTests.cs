// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Reflection;
using Xap.Hid;
using Xunit;

namespace Xap.Tests;

/// <summary>
/// Regression test for a real-hardware bug: <c>ReceiveAsync</c> used to strip a leading byte from
/// every inbound HID read, assuming hidapi synthesizes a report-id placeholder there the same way
/// it requires one on writes. It doesn't, for report-id-less devices -- confirmed against a real
/// GeebBoards Macropad v2, where this shifted a correct, on-time response by one byte and
/// corrupted its token, making every request appear to time out.
/// </summary>
public class XapHidDeviceFramingTests
{
    [Fact]
    public void ExtractFrame_DoesNotStripLeadingByte()
    {
        // Real capture from a GeebBoards Macropad v2 responding to a CAPABILITIES_QUERY for
        // token 0x0100: flags Success (0x01), length 4, payload 0x0000003F.
        byte[] buffer = new byte[64];
        byte[] captured = [0x00, 0x01, 0x01, 0x04, 0x3F, 0x00, 0x00, 0x00];
        captured.CopyTo(buffer, 0);

        byte[] frame = XapHidDevice.ExtractFrame(buffer, captured.Length);

        Assert.Equal(captured, frame);
    }

    [Fact]
    public void Constructor_IsInternal_NotPublic()
    {
        ConstructorInfo[] ctors = typeof(XapHidDevice).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(ctors);

        ConstructorInfo[] internalCtors = typeof(XapHidDevice).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.Single(internalCtors);
        Assert.True(internalCtors[0].IsAssembly);
    }
}
