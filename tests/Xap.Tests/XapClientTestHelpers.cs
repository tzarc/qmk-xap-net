// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Reflection;

namespace Xap.Tests;

/// <summary>
/// Shared construction/teardown/decode/reflection helpers for driving the real, generated
/// <c>Xap.XapClient</c> by reflection against <see cref="GeneratorTestHarness"/> and
/// <see cref="TestTransport"/>. These were byte-for-byte (or near-identical, differently-named)
/// duplicates across seven test files; consolidated here per the ponytail-review pass.
/// </summary>
internal static class XapClientTestHelpers
{
    public static (Type ClientType, object Client, TestTransport Transport) NewClient(string specJson)
    {
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(specJson);
        Type? clientType = asm.GetType("Xap.XapClient") ?? throw new InvalidOperationException("Generated assembly has no Xap.XapClient type.");

        var transport = new TestTransport();
        object client = Activator.CreateInstance(clientType, transport)
            ?? throw new InvalidOperationException("Activator.CreateInstance(XapClient) returned null.");
        return (clientType, client, transport);
    }

    public static async Task DisposeAsync(Type clientType, object client)
    {
        MethodInfo method = clientType.GetMethod("DisposeAsync")!;
        var task = (ValueTask)method.Invoke(client, null)!;
        await task;
    }

    /// <summary>Decodes the little-endian token from the first two bytes of a sent/queued frame.</summary>
    public static ushort ReadToken(byte[] frame) => (ushort)(frame[0] | (frame[1] << 8));

    /// <summary>
    /// Directly sets a generated subsystem's private `_capabilities` cache via reflection,
    /// bypassing the CAPABILITIES_QUERY round trip so a test can focus on a single command
    /// method's request/response behavior. The capability round trip itself (and CreateAsync's
    /// enabled-subsystem gating) is covered separately in GatingTests.cs.
    /// </summary>
    public static void SetCapabilities(object subsystem, uint value)
    {
        FieldInfo field = subsystem.GetType().GetField("_capabilities", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(subsystem, value);
    }

    public static uint GetCapabilities(object subsystem)
    {
        FieldInfo field = subsystem.GetType().GetField("_capabilities", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (uint)field.GetValue(subsystem)!;
    }

    /// <summary>Returns a Task that completes with the first <c>BroadcastError</c> exception.</summary>
    public static Task<Exception> SubscribeToBroadcastError(Type clientType, object client)
    {
        var tcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventInfo errorEvent = clientType.GetEvent("BroadcastError")!;
        Action<Exception> handler = ex => tcs.TrySetResult(ex);
        errorEvent.AddEventHandler(client, handler);
        return tcs.Task;
    }
}
