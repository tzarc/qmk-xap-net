// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Reflection;
using Xunit;
using static Xap.Tests.XapClientTestHelpers;

namespace Xap.Tests;

/// <summary>
/// Drives the real, generated <c>Xap.XapClient.DisposeAsync</c> by reflection against
/// <see cref="GeneratorTestHarness.RunAndCompileOrThrow"/>'s compiled assembly -- no
/// reimplementation of the fault-out logic here.
/// </summary>
public class LifecycleTests
{
    [Fact]
    public async Task DisposeAsync_FaultsInFlightRequest()
    {
        (Type? clientType, object? client, TestTransport _) = NewClient(TestSpecs.Load("0.3.0"));

        MethodInfo sendRequestAsync = clientType.GetMethod("SendRequestAsync")!;
        byte[] route = [0x00, 0x00];
        // No response is ever queued for this token. SendRequestAsync has no timeout of its
        // own (unlike ExecuteAsync's _requestTimeout), so without DisposeAsync's fault-out this
        // would hang forever rather than merely resolve slowly.
        var task = (Task<RawMessage>)sendRequestAsync.Invoke(client, [route, CancellationToken.None])!;

        MethodInfo disposeAsync = clientType.GetMethod("DisposeAsync")!;
        var disposeTask = (ValueTask)disposeAsync.Invoke(client, null)!;
        await disposeTask.AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        XapException ex = await Assert.ThrowsAsync<XapException>(() => task);
        Assert.Equal(typeof(XapException), ex.GetType()); // exactly XapException -- not ObjectDisposedException, not XapTimeoutException
    }

    [Fact]
    public async Task DisposeAsync_WithNoPendingRequests_CompletesWithoutThrowing()
    {
        (Type? clientType, object? client, TestTransport _) = NewClient(TestSpecs.Load("0.3.0"));

        MethodInfo disposeAsync = clientType.GetMethod("DisposeAsync")!;
        var disposeTask = (ValueTask)disposeAsync.Invoke(client, null)!;
        await disposeTask.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        (Type? clientType, object? client, TestTransport _) = NewClient(TestSpecs.Load("0.3.0"));

        MethodInfo disposeAsync = clientType.GetMethod("DisposeAsync")!;
        var first = (ValueTask)disposeAsync.Invoke(client, null)!;
        await first.AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        var second = (ValueTask)disposeAsync.Invoke(client, null)!;
        await second.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TransportEndingOnItsOwn_FaultsPendingRequest()
    {
        (Type? clientType, object? client, TestTransport transport) = NewClient(TestSpecs.Load("0.3.0"));

        MethodInfo sendRequestAsync = clientType.GetMethod("SendRequestAsync")!;
        byte[] route = [0x00, 0x00];
        var task = (Task<RawMessage>)sendRequestAsync.Invoke(client, [route, CancellationToken.None])!;

        transport.Complete();

        XapException ex = await Assert.ThrowsAnyAsync<XapException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(typeof(XapException), ex.GetType());

        await DisposeAsync(clientType, client);
    }

    [Fact]
    public async Task TransportFaulting_FaultsPendingRequest_AndDisposeStillRuns()
    {
        (Type? clientType, object? client, TestTransport transport) = NewClient(TestSpecs.Load("0.3.0"));

        MethodInfo sendRequestAsync = clientType.GetMethod("SendRequestAsync")!;
        byte[] route = [0x00, 0x00];
        var task = (Task<RawMessage>)sendRequestAsync.Invoke(client, [route, CancellationToken.None])!;

        // The receive loop ends by exception (not clean completion, not cancellation) --
        // pending requests must still be faulted immediately, not left to hang.
        transport.Fault(new InvalidOperationException("transport boom"));

        XapException ex = await Assert.ThrowsAnyAsync<XapException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(typeof(XapException), ex.GetType());

        // DisposeAsync deliberately rethrows a genuine (non-cancellation) receive-loop fault,
        // after completing its own cleanup.
        await Assert.ThrowsAsync<InvalidOperationException>(() => DisposeAsync(clientType, client));
    }
}
