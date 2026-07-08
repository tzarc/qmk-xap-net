// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Reflection;
using Xunit;
using static Xap.Tests.XapClientTestHelpers;

namespace Xap.Tests;

/// <summary>
/// Drives the real, generated <c>Xap.XapBroadcast</c> enum and <c>XapClient</c> broadcast
/// dispatch by reflection against <see cref="GeneratorTestHarness.RunAndCompileOrThrow"/>'s
/// compiled assembly -- same idiom as <see cref="ClientInfraTests"/>/<see cref="GatingTests"/>.
/// Nothing here reimplements the dispatch switch; every assertion drives generated code.
/// </summary>
public class BroadcastTests
{
    /// <summary>Builds a broadcast frame: token(2 LE, always 0xFFFF) + type(1) + length(1) + payload.</summary>
    private static byte[] BuildBroadcastFrame(byte type, byte[] payload)
    {
        byte[] frame = new byte[4 + payload.Length];
        frame[0] = 0xFF;
        frame[1] = 0xFF;
        frame[2] = type;
        frame[3] = (byte)payload.Length;
        payload.CopyTo(frame, 4);
        return frame;
    }

    [Fact]
    public async Task SecureStatusBroadcast_Status2_RaisesEvent_AndSetsUnlocked()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            EventInfo eventInfo = clientType.GetEvent("SecureStatusChanged")!;
            var tcs = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<byte, Task> handler = status => { tcs.TrySetResult(status); return Task.CompletedTask; };
            eventInfo.AddEventHandler(client, handler);

            // XapBroadcast.SecureStatus == 0x01; payload is a single status byte, 0x02 = "allowed".
            transport.QueueResponse(BuildBroadcastFrame(0x01, [0x02]));

            byte raisedStatus = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal((byte)0x02, raisedStatus);

            bool secureUnlocked = (bool)clientType.GetProperty("SecureUnlocked")!.GetValue(client)!;
            Assert.True(secureUnlocked);

            eventInfo.RemoveEventHandler(client, handler);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task LogMessageBroadcast_RaisesEvent_WithPayloadBytes()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            EventInfo eventInfo = clientType.GetEvent("LogMessageReceived")!;
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<ReadOnlyMemory<byte>, Task> handler = payload => { tcs.TrySetResult(payload.ToArray()); return Task.CompletedTask; };
            eventInfo.AddEventHandler(client, handler);

            byte[] text = System.Text.Encoding.UTF8.GetBytes("Hello QMK!");
            // XapBroadcast.LogMessage == 0x00.
            transport.QueueResponse(BuildBroadcastFrame(0x00, text));

            byte[] raised = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(text, raised);

            eventInfo.RemoveEventHandler(client, handler);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task SpecWithNoBroadcastMessages_CompilesAndDropsBroadcastFrameWithoutCrashing()
    {
        // A minimal "{}" spec has no routes, response_flags, or broadcast_messages -- so
        // BroadcastGenerator never runs and Xap.Broadcasts.g.cs is never emitted. XapClientBase's
        // OnBroadcastReceived is a plain virtual method with a no-op default body, so a spec with
        // no broadcasts still compiles and just drops broadcast-shaped frames.
        (Type? clientType, object? client, TestTransport? transport) = NewClient("{}");
        try
        {
            Assert.Null(clientType.Assembly.GetType("Xap.XapBroadcast"));

            // A broadcast-shaped frame (token 0xFFFF) must not crash the receive loop.
            transport.QueueResponse(BuildBroadcastFrame(0x01, [0x02]));

            // Give the receive loop a moment to process the frame, then prove the client is
            // still alive and responsive (still able to service a normal request/response).
            MethodInfo sendRequestAsync = clientType.GetMethod("SendRequestAsync")!;
            var task = (Task<RawMessage>)sendRequestAsync.Invoke(client, [new byte[] { 0x00, 0x00 }, CancellationToken.None])!;
            byte[] sent = Assert.Single(transport.SentFrames);
            ushort token = ReadToken(sent);
            transport.QueueResponse(TestTransport.BuildResponseFrame(token, flags: 0x01, [0xAA]));

            RawMessage completed = await task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEmpty(completed.Data.ToArray());
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task MalformedBroadcast_OverlongLength_RaisesBroadcastError()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            Task<Exception> errorTask = SubscribeToBroadcastError(clientType, client);

            // token(2) + type(LogMessage=0x00) + length(0xFF) but no payload bytes follow.
            byte[] overlong = [0xFF, 0xFF, 0x00, 0xFF];
            transport.QueueResponse(overlong);

            Exception ex = await errorTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsType<XapParseException>(ex);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task SecureStatusBroadcast_ZeroLengthPayload_RaisesBroadcastError()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            Task<Exception> errorTask = SubscribeToBroadcastError(clientType, client);

            // token(2) + type(SecureStatus=0x01) + length(0).
            byte[] zeroLen = [0xFF, 0xFF, 0x01, 0x00];
            transport.QueueResponse(zeroLen);

            Exception ex = await errorTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsType<XapParseException>(ex);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task SecureStatusBroadcast_ZeroDeclaredLengthWithPadding_DoesNotTouchCache()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            Task<Exception> errorTask = SubscribeToBroadcastError(clientType, client);

            // Declared length 0 with a trailing 0x02 byte: real HID reports are a fixed 64
            // bytes, so bytes past the declared payload are padding and must never be read
            // as a secure status (0x02 would flip the cache to "unlocked").
            byte[] padded = [0xFF, 0xFF, 0x01, 0x00, 0x02];
            transport.QueueResponse(padded);

            Exception ex = await errorTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsType<XapParseException>(ex);
            Assert.False((bool)clientType.GetProperty("SecureUnlocked")!.GetValue(client)!);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task LogMessageBroadcast_MultipleHandlers_AllInvoked()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            EventInfo eventInfo = clientType.GetEvent("LogMessageReceived")!;
            var tcs1 = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tcs2 = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<ReadOnlyMemory<byte>, Task> h1 = async payload => { await Task.Yield(); tcs1.TrySetResult(payload.ToArray()); };
            Func<ReadOnlyMemory<byte>, Task> h2 = async payload => { await Task.Yield(); tcs2.TrySetResult(payload.ToArray()); };
            eventInfo.AddEventHandler(client, h1);
            eventInfo.AddEventHandler(client, h2);

            byte[] text = System.Text.Encoding.UTF8.GetBytes("Hello QMK!");
            transport.QueueResponse(BuildBroadcastFrame(0x00, text));

            byte[] r1 = await tcs1.Task.WaitAsync(TimeSpan.FromSeconds(5));
            byte[] r2 = await tcs2.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(text, r1);
            Assert.Equal(text, r2);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task MulticastBroadcast_FirstHandlerThrows_ExceptionObservedViaBroadcastError()
    {
        // A plain `await handler(payload)` on a multicast delegate only awaits the last handler's
        // Task; the throwing handler is registered first so AwaitAllAsync must observe it.
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            Task<Exception> errorTask = SubscribeToBroadcastError(clientType, client);

            EventInfo eventInfo = clientType.GetEvent("LogMessageReceived")!;
            Func<ReadOnlyMemory<byte>, Task> throwingHandler = async _ =>
            {
                await Task.Yield();
                throw new InvalidOperationException("handler boom");
            };
            Func<ReadOnlyMemory<byte>, Task> okHandler = _ => Task.CompletedTask;
            eventInfo.AddEventHandler(client, throwingHandler);
            eventInfo.AddEventHandler(client, okHandler);

            transport.QueueResponse(BuildBroadcastFrame(0x00, [0x41]));

            Exception ex = await errorTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsType<InvalidOperationException>(ex);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public void XapBroadcast_IsNotAFlagsEnum()
    {
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type? broadcastEnumType = asm.GetType("Xap.XapBroadcast");
        Assert.NotNull(broadcastEnumType);
        Assert.True(broadcastEnumType.IsEnum);
        Assert.Null(broadcastEnumType.GetCustomAttribute<FlagsAttribute>());
    }
}
