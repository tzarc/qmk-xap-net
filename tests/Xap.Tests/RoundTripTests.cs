// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Reflection;
using Xunit;
using static Xap.Tests.XapClientTestHelpers;

namespace Xap.Tests;

/// <summary>
/// Drives the real, generated command methods (VERSION_QUERY, HARDWARE_ID) by reflection against
/// <see cref="GeneratorTestHarness.RunAndCompileOrThrow"/>'s compiled assembly and
/// <see cref="TestTransport"/>. Replaces the old placeholder RoundTripTests.cs, which asserted
/// hand-rolled BCD/array-decoding logic against itself and never touched generated code.
/// </summary>
public class RoundTripTests
{

    [Fact]
    public async Task VersionQuery_RoundTrips_ThroughGeneratedClient()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            object xap = clientType.GetProperty("Xap")!.GetValue(client)!;
            SetCapabilities(xap, 0xFFFFFFFFu);

            MethodInfo method = xap.GetType().GetMethod("GetVersionAsync")!;
            var task = (Task)method.Invoke(xap, [CancellationToken.None])!;

            byte[] sent = Assert.Single(transport.SentFrames);
            ushort token = ReadToken(sent);

            // BCD for 3.2.115: patch 0x0115 little-endian => {0x15,0x01}; minor 0x02 BCD; major 0x03 BCD.
            byte[] payload = [0x15, 0x01, 0x02, 0x03];
            transport.QueueResponse(TestTransport.BuildResponseFrame(token, flags: 0x01, payload));

            await task.WaitAsync(TimeSpan.FromSeconds(5));

            object version = task.GetType().GetProperty("Result")!.GetValue(task)!;
            Type versionType = version.GetType();
            Assert.Equal((byte)3, versionType.GetField("Major")!.GetValue(version));
            Assert.Equal((byte)2, versionType.GetField("Minor")!.GetValue(version));
            Assert.Equal((ushort)115, versionType.GetField("Patch")!.GetValue(version));
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task HardwareId_RoundTrips_U32x4()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            object qmk = clientType.GetProperty("Qmk")!.GetValue(client)!;
            SetCapabilities(qmk, 0xFFFFFFFFu);

            MethodInfo method = qmk.GetType().GetMethod("GetHardwareIdAsync")!;
            var task = (Task)method.Invoke(qmk, [CancellationToken.None])!;

            byte[] sent = Assert.Single(transport.SentFrames);
            ushort token = ReadToken(sent);

            byte[] payload = new byte[16];
            for (int i = 0; i < 4; i++)
                payload[i * 4] = (byte)(i + 1);
            transport.QueueResponse(TestTransport.BuildResponseFrame(token, flags: 0x01, payload));

            await task.WaitAsync(TimeSpan.FromSeconds(5));

            uint[] hardwareId = (uint[])task.GetType().GetProperty("Result")!.GetValue(task)!;
            Assert.Equal(4, hardwareId.Length);
            Assert.Equal(1u, hardwareId[0]);
            Assert.Equal(2u, hardwareId[1]);
            Assert.Equal(3u, hardwareId[2]);
            Assert.Equal(4u, hardwareId[3]);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task StructRequestCommand_SetConfigAsync_SendsSerializedPayload()
    {
        // Exercises the struct-request path (WriteTo into a payload byte[]) end to end via a
        // real generated method: Lighting.Backlight.SetConfigAsync(BacklightSetConfigRequest).
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            object lighting = clientType.GetProperty("Lighting")!.GetValue(client)!;
            object backlight = lighting.GetType().GetProperty("Backlight")!.GetValue(lighting)!;
            SetCapabilities(backlight, 0xFFFFFFFFu);

            Assembly asm = clientType.Assembly;
            Type requestType = asm.GetType("Xap.BacklightSetConfigRequest")!;
            object request = Activator.CreateInstance(requestType, (byte)1, (byte)2, (byte)0x50)!;

            MethodInfo method = backlight.GetType().GetMethod("SetConfigAsync")!;
            var task = (Task)method.Invoke(backlight, [request, CancellationToken.None])!;

            byte[] sent = Assert.Single(transport.SentFrames);
            // token(2) + length(1) + route(0x06,0x02,0x04) + payload(3 bytes) = 9 bytes.
            Assert.Equal(9, sent.Length);
            Assert.Equal(new byte[] { 0x06, 0x02, 0x04 }, new[] { sent[3], sent[4], sent[5] });
            Assert.Equal(new byte[] { 1, 2, 0x50 }, new[] { sent[6], sent[7], sent[8] });

            ushort token = ReadToken(sent);
            transport.QueueResponse(TestTransport.BuildResponseFrame(token, flags: 0x01, []));

            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }
}
