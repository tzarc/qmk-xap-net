// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Reflection;
using Xunit;
using static Xap.Tests.XapClientTestHelpers;

namespace Xap.Tests;

/// <summary>
/// Drives the real, generated capability-gating logic -- both the per-subsystem `Has*`/
/// `InitializeAsync` recursion and <c>XapClient.CreateAsync</c>'s enabled-subsystem-bitmask gate
/// -- by reflection against <see cref="GeneratorTestHarness.RunAndCompileOrThrow"/>'s compiled
/// assembly and <see cref="TestTransport"/>. New file (no old placeholder existed for gating).
/// </summary>
public class GatingTests
{
    private static byte[] LeBytes(uint value) =>
    [
        (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF), (byte)((value >> 24) & 0xFF),
    ];

    private static async Task WaitForSentCountAsync(TestTransport transport, int count, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (transport.SentFrames.Count < count)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Expected {count} sent frame(s), saw {transport.SentFrames.Count}.");
            await Task.Delay(5);
        }
    }

    // ---- Command-level gating (Has* false => XapRouteUnavailableException) ------------------

    [Fact]
    public async Task CommandWithoutCapabilityBitSet_ThrowsRouteUnavailable_NothingSent()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            object qmk = clientType.GetProperty("Qmk")!.GetValue(client)!;
            // _capabilities defaults to 0 -- no bits set, so every Has* is false.
            Assert.Equal(0u, GetCapabilities(qmk));

            MethodInfo method = qmk.GetType().GetMethod("GetVersionAsync")!;
            var task = (Task)method.Invoke(qmk, [CancellationToken.None])!;

            XapRouteUnavailableException ex = await Assert.ThrowsAsync<XapRouteUnavailableException>(() => task);
            Assert.Equal("Qmk.VersionQuery", ex.RouteName);
            Assert.Empty(transport.SentFrames);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    // ---- KB/USER: ungated, no capabilities plumbing at all -----------------------------------

    [Fact]
    public async Task KbAndUserSubsystems_HaveNoCapabilitiesFieldOrInitializeAsync()
    {
        (Type? clientType, object? client, _) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            object kb = clientType.GetProperty("Kb")!.GetValue(client)!;
            object user = clientType.GetProperty("User")!.GetValue(client)!;

            Assert.Null(kb.GetType().GetField("_capabilities", BindingFlags.NonPublic | BindingFlags.Instance));
            Assert.Null(kb.GetType().GetMethod("InitializeAsync", BindingFlags.NonPublic | BindingFlags.Instance));
            Assert.Null(user.GetType().GetField("_capabilities", BindingFlags.NonPublic | BindingFlags.Instance));
            Assert.Null(user.GetType().GetMethod("InitializeAsync", BindingFlags.NonPublic | BindingFlags.Instance));
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    // ---- Recursive nested-router gating (Lighting -> Backlight/Rgblight/RgbMatrix) -----------

    [Fact]
    public async Task NestedRouterGating_OnlyEnabledChildGetsInitialized()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            object lighting = clientType.GetProperty("Lighting")!.GetValue(client)!;
            MethodInfo initializeAsync = lighting.GetType().GetMethod("InitializeAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

            // Only the Backlight bit (route id 0x02) is set -- Rgblight (0x03) and RgbMatrix
            // (0x04) must stay uninitialized.
            var task = (Task)initializeAsync.Invoke(lighting, [CancellationToken.None])!;

            await WaitForSentCountAsync(transport, 1, TimeSpan.FromSeconds(2));
            ushort token = ReadToken(transport.SentFrames[0]);
            transport.QueueResponse(TestTransport.BuildResponseFrame(token, flags: 0x01, LeBytes(1u << 0x02)));

            await WaitForSentCountAsync(transport, 2, TimeSpan.FromSeconds(2));
            ushort backlightToken = ReadToken(transport.SentFrames[1]);
            transport.QueueResponse(TestTransport.BuildResponseFrame(backlightToken, flags: 0x01, LeBytes(0xFFFFFFFFu)));

            await task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, transport.SentFrames.Count); // Lighting's own query + Backlight's only
            Assert.DoesNotContain(transport.SentFrames, f => f.Length >= 5 && f[3] == 0x06 && f[4] == 0x03 && f[5] == 0x01); // Rgblight caps
            Assert.DoesNotContain(transport.SentFrames, f => f.Length >= 5 && f[3] == 0x06 && f[4] == 0x04 && f[5] == 0x01); // RgbMatrix caps

            object backlight = lighting.GetType().GetProperty("Backlight")!.GetValue(lighting)!;
            object rgblight = lighting.GetType().GetProperty("Rgblight")!.GetValue(lighting)!;
            Assert.NotEqual(0u, GetCapabilities(backlight)); // was initialized (its own query ran)
            Assert.Equal(0u, GetCapabilities(rgblight));      // never initialized -- stays default
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    // ---- CreateAsync: top-level enabled-subsystem-bitmask gating -----------------------------

    [Fact]
    public async Task EnabledSubsystemBitmask_OmittedSubsystem_HasPropertyFalse()
    {
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type clientType = asm.GetType("Xap.XapClient")!;
        var transport = new TestTransport();
        MethodInfo createAsync = clientType.GetMethod("CreateAsync", BindingFlags.Public | BindingFlags.Static)!;
        var createTask = (Task)createAsync.Invoke(null, [transport, CancellationToken.None])!;

        object? createdClient = null;
        try
        {
            // 1st request: Xap's own CAPABILITIES_QUERY (route 0x00,0x01).
            await WaitForSentCountAsync(transport, 1, TimeSpan.FromSeconds(2));
            ushort xapCapsToken = ReadToken(transport.SentFrames[0]);
            transport.QueueResponse(TestTransport.BuildResponseFrame(xapCapsToken, flags: 0x01, LeBytes(0xFFFFFFFFu)));

            // 2nd request: XAP's SUBSYSTEM_QUERY (route 0x00,0x02) -- the enabled-subsystem bitmask.
            // Enable nothing: every other top-level subsystem (including LIGHTING) is omitted.
            await WaitForSentCountAsync(transport, 2, TimeSpan.FromSeconds(2));
            ushort enabledToken = ReadToken(transport.SentFrames[1]);
            transport.QueueResponse(TestTransport.BuildResponseFrame(enabledToken, flags: 0x01, LeBytes(0u)));

            await createTask.WaitAsync(TimeSpan.FromSeconds(5));
            createdClient = createTask.GetType().GetProperty("Result")!.GetValue(createTask)!;

            // No other top-level subsystem's own CAPABILITIES_QUERY was ever sent.
            Assert.Equal(2, transport.SentFrames.Count);
            Assert.DoesNotContain(transport.SentFrames, f => f.Length >= 5 && f[3] == 0x06 && f[4] == 0x01); // Lighting caps
            Assert.DoesNotContain(transport.SentFrames, f => f.Length >= 5 && f[3] == 0x01 && f[4] == 0x01); // Qmk caps

            object lighting = clientType.GetProperty("Lighting")!.GetValue(createdClient)!;
            Assert.Equal(0u, GetCapabilities(lighting)); // never initialized -- stays default

            object qmk = clientType.GetProperty("Qmk")!.GetValue(createdClient)!;
            Assert.Equal(0u, GetCapabilities(qmk));
        }
        finally
        {
            if (createdClient is not null)
                await DisposeAsync(clientType, createdClient);
        }
    }

    [Fact]
    public async Task EnabledSubsystemBitmask_IncludedSubsystem_GetsInitialized()
    {
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type clientType = asm.GetType("Xap.XapClient")!;
        var transport = new TestTransport();
        MethodInfo createAsync = clientType.GetMethod("CreateAsync", BindingFlags.Public | BindingFlags.Static)!;
        var createTask = (Task)createAsync.Invoke(null, [transport, CancellationToken.None])!;

        object? createdClient = null;
        try
        {
            await WaitForSentCountAsync(transport, 1, TimeSpan.FromSeconds(2));
            ushort xapCapsToken = ReadToken(transport.SentFrames[0]);
            transport.QueueResponse(TestTransport.BuildResponseFrame(xapCapsToken, flags: 0x01, LeBytes(0xFFFFFFFFu)));

            await WaitForSentCountAsync(transport, 2, TimeSpan.FromSeconds(2));
            ushort enabledToken = ReadToken(transport.SentFrames[1]);
            // Enable only QMK (route id 0x01).
            transport.QueueResponse(TestTransport.BuildResponseFrame(enabledToken, flags: 0x01, LeBytes(1u << 0x01)));

            // 3rd request: Qmk's own CAPABILITIES_QUERY (route 0x01,0x01), since it's enabled.
            await WaitForSentCountAsync(transport, 3, TimeSpan.FromSeconds(2));
            ushort qmkCapsToken = ReadToken(transport.SentFrames[2]);
            transport.QueueResponse(TestTransport.BuildResponseFrame(qmkCapsToken, flags: 0x01, LeBytes(0xFFFFFFFFu)));

            await createTask.WaitAsync(TimeSpan.FromSeconds(5));
            createdClient = createTask.GetType().GetProperty("Result")!.GetValue(createTask)!;

            object qmk = clientType.GetProperty("Qmk")!.GetValue(createdClient)!;
            Assert.NotEqual(0u, GetCapabilities(qmk));

            object lighting = clientType.GetProperty("Lighting")!.GetValue(createdClient)!;
            Assert.Equal(0u, GetCapabilities(lighting)); // not enabled -- never initialized
        }
        finally
        {
            if (createdClient is not null)
                await DisposeAsync(clientType, createdClient);
        }
    }
}
