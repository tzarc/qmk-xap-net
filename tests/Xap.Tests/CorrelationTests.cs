// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Reflection;
using Xunit;
using static Xap.Tests.XapClientTestHelpers;

namespace Xap.Tests;

/// <summary>
/// Drives the real, generated <c>Xap.XapClient</c>'s token-based correlation (the <c>_pending</c>
/// dictionary, <c>AllocateToken</c>, and the receive loop's <c>HandleIncoming</c>) under
/// overlapping, out-of-order, and duplicate responses. Every assertion goes through
/// <see cref="GeneratorTestHarness.RunAndCompileOrThrow"/>'s compiled <c>SendRequestAsync</c> and
/// a real <see cref="TestTransport"/> -- nothing here reimplements correlation locally and
/// asserts against a copy.
/// </summary>
public class CorrelationTests
{
    [Fact]
    public async Task OverlappingRequests_CompleteTheirOwnResponse()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            MethodInfo sendRequestAsync = clientType.GetMethod("SendRequestAsync")!;
            byte[] route = [0x00, 0x00];

            // Issue both requests without awaiting either: each Invoke runs synchronously up to
            // its first genuine suspension (awaiting the still-unset TCS), so both tokens land
            // in _pending before either resolves.
            var task1 = (Task<RawMessage>)sendRequestAsync.Invoke(client, [route, CancellationToken.None])!;
            var task2 = (Task<RawMessage>)sendRequestAsync.Invoke(client, [route, CancellationToken.None])!;

            Assert.Equal(2, transport.SentFrames.Count);
            ushort token1 = ReadToken(transport.SentFrames[0]);
            ushort token2 = ReadToken(transport.SentFrames[1]);
            Assert.NotEqual(token1, token2);

            byte[] payloadFor1 = [0x11, 0x11];
            byte[] payloadFor2 = "\"\""u8.ToArray();
            byte[] responseFor1 = TestTransport.BuildResponseFrame(token1, flags: 0x01, payloadFor1);
            byte[] responseFor2 = TestTransport.BuildResponseFrame(token2, flags: 0x01, payloadFor2);

            // Queue out of order: token2's response arrives before token1's.
            transport.QueueResponse(responseFor2);
            transport.QueueResponse(responseFor1);

            RawMessage completed1 = await task1.WaitAsync(TimeSpan.FromSeconds(5));
            RawMessage completed2 = await task2.WaitAsync(TimeSpan.FromSeconds(5));

            // Each caller must get the payload tagged for ITS OWN token, not whichever arrived
            // first -- this is exactly what breaks if correlation matched by arrival order.
            Assert.Equal(responseFor1, completed1.Data.ToArray());
            Assert.Equal(responseFor2, completed2.Data.ToArray());
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task ThreeOverlappingRequests_ScrambledResponses_EachGetsOwnPayload()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            MethodInfo sendRequestAsync = clientType.GetMethod("SendRequestAsync")!;
            byte[] route = [0x00, 0x00];

            var taskA = (Task<RawMessage>)sendRequestAsync.Invoke(client, [route, CancellationToken.None])!;
            var taskB = (Task<RawMessage>)sendRequestAsync.Invoke(client, [route, CancellationToken.None])!;
            var taskC = (Task<RawMessage>)sendRequestAsync.Invoke(client, [route, CancellationToken.None])!;

            Assert.Equal(3, transport.SentFrames.Count);
            ushort tokenA = ReadToken(transport.SentFrames[0]);
            ushort tokenB = ReadToken(transport.SentFrames[1]);
            ushort tokenC = ReadToken(transport.SentFrames[2]);
            Assert.Equal(3, new[] { tokenA, tokenB, tokenC }.Distinct().Count());

            byte[] responseA = TestTransport.BuildResponseFrame(tokenA, flags: 0x01, [0xA1, 0xA2, 0xA3]);
            byte[] responseB = TestTransport.BuildResponseFrame(tokenB, flags: 0x01, [0xB1]);
            byte[] responseC = TestTransport.BuildResponseFrame(tokenC, flags: 0x01, [0xC1, 0xC2]);

            // Fully scrambled arrival order: C, A, B -- not simply reversed.
            transport.QueueResponse(responseC);
            transport.QueueResponse(responseA);
            transport.QueueResponse(responseB);

            RawMessage completedA = await taskA.WaitAsync(TimeSpan.FromSeconds(5));
            RawMessage completedB = await taskB.WaitAsync(TimeSpan.FromSeconds(5));
            RawMessage completedC = await taskC.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(responseA, completedA.Data.ToArray());
            Assert.Equal(responseB, completedB.Data.ToArray());
            Assert.Equal(responseC, completedC.Data.ToArray());
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }

    [Fact]
    public async Task DuplicateResponseForAlreadyCompletedToken_RaisesUnmatchedWithoutCorruptingOtherPending()
    {
        (Type? clientType, object? client, TestTransport? transport) = NewClient(TestSpecs.Load("0.3.0"));
        try
        {
            MethodInfo sendRequestAsync = clientType.GetMethod("SendRequestAsync")!;
            byte[] route = [0x00, 0x00];

            var task1 = (Task<RawMessage>)sendRequestAsync.Invoke(client, [route, CancellationToken.None])!;
            var task2 = (Task<RawMessage>)sendRequestAsync.Invoke(client, [route, CancellationToken.None])!;

            Assert.Equal(2, transport.SentFrames.Count);
            ushort token1 = ReadToken(transport.SentFrames[0]);
            ushort token2 = ReadToken(transport.SentFrames[1]);

            EventInfo eventInfo = clientType.GetEvent("UnmatchedResponse")!;
            var unmatchedTcs = new TaskCompletionSource<RawMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<RawMessage> handler = msg => unmatchedTcs.TrySetResult(msg);
            eventInfo.AddEventHandler(client, handler);
            try
            {
                byte[] responseFor1 = TestTransport.BuildResponseFrame(token1, flags: 0x01, [0x11]);
                transport.QueueResponse(responseFor1);

                RawMessage completed1 = await task1.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(responseFor1, completed1.Data.ToArray());

                // token1 is now removed from _pending. A second (duplicate/late) response for the
                // same token must be reported as unmatched, not silently dropped, and -- crucially
                // -- must not touch token2's still-pending request.
                byte[] duplicateFor1 = TestTransport.BuildResponseFrame(token1, flags: 0x01, [0x99]);
                transport.QueueResponse(duplicateFor1);

                RawMessage unmatched = await unmatchedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(duplicateFor1, unmatched.Data.ToArray());

                // task2 must still be untouched by the duplicate delivery.
                await Task.Delay(50);
                Assert.False(task2.IsCompleted, "Duplicate response for a different token must not resolve task2's pending request.");

                byte[] responseFor2 = TestTransport.BuildResponseFrame(token2, flags: 0x01, [0x22]);
                transport.QueueResponse(responseFor2);
                RawMessage completed2 = await task2.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(responseFor2, completed2.Data.ToArray());
            }
            finally
            {
                eventInfo.RemoveEventHandler(client, handler);
            }
        }
        finally
        {
            await DisposeAsync(clientType, client);
        }
    }
}
