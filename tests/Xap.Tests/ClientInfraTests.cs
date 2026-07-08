// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Reflection;
using Xunit;
using static Xap.Tests.XapClientTestHelpers;

namespace Xap.Tests;

/// <summary>
/// Drives the real, generated <c>Xap.XapClient</c> infrastructure (transport wiring, token
/// allocation, request framing, receive-loop correlation) by reflection against
/// <see cref="GeneratorTestHarness.RunAndCompileOrThrow"/>'s compiled assembly. Nothing here
/// reimplements framing or correlation logic to assert against itself; every assertion drives
/// the actually-generated members.
/// </summary>
public class ClientInfraTests
{
    // ---- BuildRequest framing -----------------------------------------------------------

    [Fact]
    public async Task ZeroPayloadVersionQuery_SerializesExactBytes()
    {
        (Type? clientType, object? client, TestTransport _) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            // Zero-payload convenience overload: BuildRequest(ushort token, params byte[] route).
            MethodInfo buildRequest = clientType.GetMethods()
                .Single(m => m.Name == "BuildRequest" && m.GetParameters().Length == 2);

            byte[] bytes = (byte[])buildRequest.Invoke(client, [(ushort)0x0100, new byte[] { 0x00, 0x00 }])!;

            // token 0x0100 little-endian => 0x00,0x01; length 0x02 (route-only); route 0x00,0x00.
            Assert.Equal(new byte[] { 0x00, 0x01, 0x02, 0x00, 0x00 }, bytes);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task OversizeRequest_ThrowsXapParseException()
    {
        (Type? clientType, object? client, TestTransport _) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            MethodInfo buildRequest = clientType.GetMethods()
                .Single(m => m.Name == "BuildRequest" && m.GetParameters().Length == 2);

            // 2 (token) + 1 (length) + 62 (route) = 65 > 64 -> must throw.
            byte[] oversizeRoute = new byte[62];
            TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() =>
                buildRequest.Invoke(client, [(ushort)0x0100, oversizeRoute]));
            Assert.IsType<XapParseException>(ex.InnerException);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task ExactSixtyFourByteRequest_DoesNotThrow()
    {
        (Type? clientType, object? client, TestTransport _) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            MethodInfo buildRequest = clientType.GetMethods()
                .Single(m => m.Name == "BuildRequest" && m.GetParameters().Length == 2);

            // 2 (token) + 1 (length) + 61 (route) = 64 exactly -> must NOT throw.
            byte[] boundaryRoute = new byte[61];
            byte[] bytes = (byte[])buildRequest.Invoke(client, [(ushort)0x0100, boundaryRoute])!;
            Assert.Equal(64, bytes.Length);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    // ---- Token allocation -----------------------------------------------------------------

    [Fact]
    public async Task AllocateToken_WrapsAtMaxAndSkipsReservedValues()
    {
        (Type? clientType, object? client, TestTransport _) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            MethodInfo allocateToken = clientType.GetMethod("AllocateToken")!;
            var allocate = (Func<ushort>)Delegate.CreateDelegate(typeof(Func<ushort>), client, allocateToken);

            const ushort minToken = 0x0100;
            const ushort maxToken = 0xFFFD;

            Assert.Equal(minToken, allocate());

            ushort last = minToken;
            for (int i = 0; i < maxToken - minToken; i++)
            {
                ushort next = allocate();
                Assert.Equal((ushort)(last + 1), next);
                Assert.NotEqual(0xFFFE, next);
                Assert.NotEqual(0xFFFF, next);
                last = next;
            }

            Assert.Equal(maxToken, last); // consumed the whole range up to MaxToken

            // One more call must wrap back to MinToken, never handing out 0xFFFE/0xFFFF.
            Assert.Equal(minToken, allocate());
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    // ---- Receive-loop correlation -----------------------------------------------------------

    [Fact]
    public async Task PendingRequest_CorrelatesWithMatchingResponseToken()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            MethodInfo sendRequestAsync = clientType.GetMethod("SendRequestAsync")!;
            byte[] route = [0x00, 0x00];

            var task = (Task<RawMessage>)sendRequestAsync.Invoke(client, [route, CancellationToken.None])!;

            // The frame must already be recorded: everything up to the first await on the
            // pending TCS's Task runs synchronously within Invoke.
            byte[] sent = Assert.Single(transport.SentFrames);
            ushort token = ReadToken(sent);

            byte[] payload = [0xAA, 0xBB];
            byte[] responseFrame = TestTransport.BuildResponseFrame(token, flags: 0x01, payload);
            transport.QueueResponse(responseFrame);

            RawMessage completed = await task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(responseFrame, completed.Data.ToArray());
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task UnmatchedResponse_RaisesEventWhenNoPendingTokenMatches()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            EventInfo eventInfo = clientType.GetEvent("UnmatchedResponse")!;
            var tcs = new TaskCompletionSource<RawMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<RawMessage> handler = msg => tcs.TrySetResult(msg);
            eventInfo.AddEventHandler(client, handler);

            byte[] responseFrame = TestTransport.BuildResponseFrame(token: 0x0123, flags: 0x01, payload: [0x42]);
            transport.QueueResponse(responseFrame);

            RawMessage raised = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(responseFrame, raised.Data.ToArray());

            eventInfo.RemoveEventHandler(client, handler);
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    // ---- TestTransport's own framing helper (validated independently of BuildRequest, since
    // the response frame layout -- token+flags+length+payload -- differs from the request frame
    // layout BuildRequest produces -- token+length+route+payload) ----------------------------

    [Fact]
    public void BuildResponseFrame_ProducesExpectedBytes()
    {
        byte[] frame = TestTransport.BuildResponseFrame(0x0100, 0x01, [0xAA, 0xBB]);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x01, 0x02, 0xAA, 0xBB }, frame);
    }
}
