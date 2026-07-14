// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Reflection;
using Xunit;
using static Xap.Tests.XapClientTestHelpers;

namespace Xap.Tests;

/// <summary>
/// Drives the real, generated secure-guard and secure-state-cache logic (XapCoreSubsystem's
/// SECURE_UNLOCK/SECURE_STATUS, QmkSubsystem's secure-gated BOOTLOADER_JUMP) by reflection
/// against <see cref="GeneratorTestHarness.RunAndCompileOrThrow"/>'s compiled assembly and
/// <see cref="TestTransport"/>. Replaces the old placeholder SecureFlowTests.cs, which asserted
/// hand-rolled booleans against themselves and never touched generated code.
/// </summary>
public class SecureFlowTests
{
    private static bool GetSecureUnlocked(Type clientType, object client)
        => (bool)clientType.GetProperty("SecureUnlocked")!.GetValue(client)!;

    [Fact]
    public async Task SecureCommandWhileLocked_ThrowsPreSendGuard_NothingSent()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            object qmk = clientType.GetProperty("Qmk")!.GetValue(client)!;
            SetCapabilities(qmk, 0xFFFFFFFFu); // HasBootloaderJump = true

            Assert.False(GetSecureUnlocked(clientType, client));

            MethodInfo method = qmk.GetType().GetMethod("InvokeBootloaderJumpAsync")!;
            var task = (Task)method.Invoke(qmk, [CancellationToken.None])!;

            // The pre-send guard throws before the first await (token allocation, BuildRequest,
            // and SendAsync never run), so it surfaces as a faulted Task, not a synchronous
            // reflection-invoke exception -- awaiting it rethrows the real exception type.
            XapSecureFailureException ex = await Assert.ThrowsAsync<XapSecureFailureException>(() => task);

            Assert.True(ex.WasPreSendGuard);
            Assert.Empty(transport.SentFrames);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task InvokeSecureUnlockAsync_SuccessResponse_DoesNotFlipCache()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            object xap = clientType.GetProperty("Xap")!.GetValue(client)!;
            SetCapabilities(xap, 0xFFFFFFFFu);

            MethodInfo method = xap.GetType().GetMethod("InvokeSecureUnlockAsync")!;
            var task = (Task)method.Invoke(xap, [CancellationToken.None])!;

            byte[] sent = Assert.Single(transport.SentFrames);
            ushort token = ReadToken(sent);
            transport.QueueResponse(TestTransport.BuildResponseFrame(token, flags: 0x01, []));

            await task.WaitAsync(TimeSpan.FromSeconds(5));

            // Per protocol: SECURE_UNLOCK only *initiates* the unlock sequence. A SUCCESS
            // response must not flip the cache -- only SECURE_STATUS == 2 does that.
            Assert.False(GetSecureUnlocked(clientType, client));
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task GetSecureStatusAsync_Returns2_SetsUnlockedTrue()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            object xap = clientType.GetProperty("Xap")!.GetValue(client)!;
            SetCapabilities(xap, 0xFFFFFFFFu);

            MethodInfo method = xap.GetType().GetMethod("GetSecureStatusAsync")!;
            var task = (Task)method.Invoke(xap, [CancellationToken.None])!;

            byte[] sent = Assert.Single(transport.SentFrames);
            ushort token = ReadToken(sent);
            transport.QueueResponse(TestTransport.BuildResponseFrame(token, flags: 0x01, [0x02]));

            await task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(XapSecureStatus.Unlocked, task.GetType().GetProperty("Result")!.GetValue(task));
            Assert.True(GetSecureUnlocked(clientType, client));
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task GetSecureStatusAsync_ReturnsNotTwo_LeavesUnlockedFalse()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            object xap = clientType.GetProperty("Xap")!.GetValue(client)!;
            SetCapabilities(xap, 0xFFFFFFFFu);

            MethodInfo method = xap.GetType().GetMethod("GetSecureStatusAsync")!;
            var task = (Task)method.Invoke(xap, [CancellationToken.None])!;

            byte[] sent = Assert.Single(transport.SentFrames);
            ushort token = ReadToken(sent);
            // Status 1 == "initiated but incomplete" -- must not be treated as unlocked.
            transport.QueueResponse(TestTransport.BuildResponseFrame(token, flags: 0x01, [0x01]));

            await task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(GetSecureUnlocked(clientType, client));
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task GetSecureStatusAsync_Timeout_RemovesWatchedToken()
    {
        (Type? clientType, object? client, TestTransport _) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            object xap = clientType.GetProperty("Xap")!.GetValue(client)!;
            SetCapabilities(xap, 0xFFFFFFFFu);

            MethodInfo method = xap.GetType().GetMethod("GetSecureStatusAsync")!;
            var task = (Task)method.Invoke(xap, [CancellationToken.None])!;

            // No response is ever queued, so the request times out. The watch entry must not
            // outlive the request: tokens recycle, so a stale entry would misread a later,
            // unrelated response for the recycled token as a secure status.
            await Assert.ThrowsAsync<XapTimeoutException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));

            FieldInfo watched = clientType.GetField("_secureStatusTokens", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.Empty((System.Collections.IEnumerable)watched.GetValue(client)!);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task SecureFailureResponse_ResetsUnlockedCacheToFalse()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            object xap = clientType.GetProperty("Xap")!.GetValue(client)!;
            SetCapabilities(xap, 0xFFFFFFFFu);
            object qmk = clientType.GetProperty("Qmk")!.GetValue(client)!;
            SetCapabilities(qmk, 0xFFFFFFFFu);

            // First, get the cache into the "unlocked" state via a real GetSecureStatusAsync round trip.
            MethodInfo statusMethod = xap.GetType().GetMethod("GetSecureStatusAsync")!;
            var statusTask = (Task)statusMethod.Invoke(xap, [CancellationToken.None])!;
            ushort statusToken = ReadToken(Assert.Single(transport.SentFrames));
            transport.QueueResponse(TestTransport.BuildResponseFrame(statusToken, flags: 0x01, [0x02]));
            await statusTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(GetSecureUnlocked(clientType, client));

            // Now call a secure command; the pre-send guard passes (cache says unlocked), but the
            // firmware responds SECURE_FAILURE -- ExecuteAsync must reset the cache to false.
            MethodInfo jumpMethod = qmk.GetType().GetMethod("InvokeBootloaderJumpAsync")!;
            var jumpTask = (Task)jumpMethod.Invoke(qmk, [CancellationToken.None])!;
            ushort jumpToken = ReadToken(transport.SentFrames[1]);
            transport.QueueResponse(TestTransport.BuildResponseFrame(jumpToken, flags: 0x02, [])); // SecureFailure bit

            XapSecureFailureException ex = await Assert.ThrowsAsync<XapSecureFailureException>(() => jumpTask);
            Assert.False(ex.WasPreSendGuard);
            Assert.False(GetSecureUnlocked(clientType, client));
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }
}
