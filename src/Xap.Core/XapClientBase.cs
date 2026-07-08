// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

namespace Xap;

/// <summary>
/// Spec-invariant XapClient infrastructure: transport wiring, monotonic token allocation,
/// request framing, receive-loop correlation, and lifecycle. None of this depends on what a
/// particular XAP spec declares, so it's hand-written here instead of re-emitted as generator
/// string-templates for every spec version. The generated <c>XapClient</c> (one per spec)
/// derives from this; the generator only needs to emit the genuinely spec-dependent surface:
/// the subsystem property tree, <c>CreateAsync</c>/<c>InitializeCapabilitiesAsync</c>, and
/// per-route command methods.
/// </summary>
public abstract class XapClientBase : IAsyncDisposable
{
    public const ushort MinToken = 0x0100;
    public const ushort MaxToken = 0xFFFD;
    // 0xFFFE/0xFFFF are reserved (fire-and-forget / broadcast) and never allocated here.

    private readonly IXapTransport _transport;
    private readonly ConcurrentDictionary<ushort, TaskCompletionSource<RawMessage>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiveLoopTask;
    private readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(1);

    private readonly Lock _tokenLock = new();
    private int _nextToken = MinToken;

    private int _disposed;

    private volatile bool _secureUnlocked;

    /// Raised when a response arrives whose token matches no pending request.
    public event Action<RawMessage>? UnmatchedResponse;

    /// Raised when a broadcast handler throws (async handlers are observed here).
    public event Action<Exception>? BroadcastError;

    /// True when secure routes are allowed (SECURE_STATUS == 2).
    public bool SecureUnlocked => _secureUnlocked;

    protected XapClientBase(IXapTransport transport)
    {
        _transport = transport;
        _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    /// <summary>
    /// Monotonic token allocator over [MinToken, MaxToken], wrapping back to MinToken
    /// once MaxToken is handed out.
    /// </summary>
    public ushort AllocateToken()
    {
        lock (_tokenLock)
        {
            ushort token = (ushort)_nextToken;
            _nextToken++;
            if (_nextToken > MaxToken)
                _nextToken = MinToken;
            return token;
        }
    }

    /// <summary>Zero-payload convenience overload.</summary>
    public byte[] BuildRequest(ushort token, params byte[] route)
        => BuildRequest(token, route, []);

    /// <summary>
    /// Frames a request as token(2 LE) + length(1) + route + payload. Throws
    /// <see cref="XapParseException"/> when the whole frame would exceed the 64-byte
    /// HID report size limit.
    /// </summary>
    public byte[] BuildRequest(ushort token, byte[] route, ReadOnlySpan<byte> payload)
    {
        int total = 2 + 1 + route.Length + payload.Length;
        if (total > 64)
            throw new XapParseException($"Request frame of {total} bytes exceeds the 64-byte HID report limit.");

        byte[] frame = new byte[total];
        frame[0] = (byte)(token & 0xFF);
        frame[1] = (byte)((token >> 8) & 0xFF);
        frame[2] = (byte)(route.Length + payload.Length);
        route.CopyTo(frame, 3);
        payload.CopyTo(frame.AsSpan(3 + route.Length));
        return frame;
    }

    /// <summary>
    /// Generic (non-route-specific) send-and-await primitive: allocates a token,
    /// registers a pending completion, frames and sends the request over the
    /// transport, and returns the response once the receive loop correlates it by
    /// token. Superseded for route-specific work by ExecuteAsync (below), which adds
    /// flag-checking and payload decoding; kept for direct callers that want the raw
    /// correlated response.
    /// </summary>
    public async Task<RawMessage> SendRequestAsync(byte[] route, CancellationToken ct = default)
    {
        ushort token = AllocateToken();
        var tcs = new TaskCompletionSource<RawMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[token] = tcs;

        using CancellationTokenRegistration transportRegistration = _cts.Token.Register(
            static s => ((TaskCompletionSource<RawMessage>)s!).TrySetException(
                new XapException("Transport closed before a response arrived.")),
            tcs);

        try
        {
            byte[] frame = BuildRequest(token, route);
            await _transport.SendAsync(frame, ct).ConfigureAwait(false);

            using CancellationTokenRegistration registration = ct.Register(static s => ((TaskCompletionSource<RawMessage>)s!).TrySetCanceled(), tcs);
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(token, out _);
        }
    }

    /// <summary>
    /// Send-and-await primitive for generated command methods: registers a pending
    /// completion for <paramref name="token"/> BEFORE sending (avoids a response-loss
    /// race against an already-in-flight receive loop), sends <paramref name="request"/>,
    /// waits up to the request timeout for the correlated response, then checks
    /// response flags and decodes the payload with <paramref name="read"/>. The
    /// response header is token(2)+flags(1)+length(1) = 4 bytes, so the payload
    /// begins at offset 4.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(ushort token, byte[] request, XapReader<T> read, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<RawMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[token] = tcs;

        using CancellationTokenRegistration transportRegistration = _cts.Token.Register(
            static s => ((TaskCompletionSource<RawMessage>)s!).TrySetException(
                new XapException("Transport closed before a response arrived.")),
            tcs);

        try
        {
            await _transport.SendAsync(request, ct).ConfigureAwait(false);

            RawMessage response;
            try
            {
                response = await tcs.Task.WaitAsync(_requestTimeout, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                response = tcs.Task.IsCompletedSuccessfully ? await tcs.Task.ConfigureAwait(false) : throw new XapTimeoutException(token);
            }

            // Parsing is pulled into a non-async helper: a ReadOnlySpan<byte> local
            // (a ref struct) cannot be declared inside an async method body, even
            // when -- as here -- it never crosses an await point.
            return ParseResponse(token, response, read);
        }
        finally
        {
            _pending.TryRemove(token, out _);
        }
    }

    private T ParseResponse<T>(ushort token, RawMessage response, XapReader<T> read)
    {
        ReadOnlySpan<byte> span = response.Data.Span;
        var flags = (ResponseFlags)span[2];
        byte length = span[3];

        return (flags & ResponseFlags.SecureFailure) != 0
            ? throw new XapSecureFailureException(token, flags)
            : (flags & ResponseFlags.Success) == 0
            ? throw new XapResponseException(token, flags)
            : span.Length < 4 + length
            ? throw new XapParseException($"Response 0x{token:X4} payload shorter than declared length.")
            : read(span.Slice(4, length));
    }

    /// <summary>Non-generic (void-result) overload: same correlation and flag checking, payload discarded.</summary>
    public async Task ExecuteAsync(ushort token, byte[] request, CancellationToken ct)
        => await ExecuteAsync(token, request, static _ => true, ct).ConfigureAwait(false);

    /// <summary>
    /// Lets a generated subsystem command method (GetSecureStatusAsync) and the SECURE_STATUS
    /// broadcast handler reconcile the secure-state cache. Called by generated code; not
    /// intended for direct external use.
    /// </summary>
    public void SetSecureUnlocked(bool value) => _secureUnlocked = value;

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (RawMessage message in _transport.ReceiveAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    HandleIncoming(message);
                }
                catch
                {
                    // A single malformed frame must not kill the receive loop.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on DisposeAsync cancelling _cts.
        }
        finally
        {
            // Runs however the loop ends -- clean transport completion, cancellation, or a
            // genuine transport fault -- so awaiting callers never hang out their full timeout.
            FaultAllPending(new XapException("Transport closed before a response arrived."));
        }
    }

    /// <summary>
    /// Parses an incoming frame as token(2 LE) + flags(1) + length(1) + payload,
    /// correlates it against pending requests by token, and completes the matching
    /// TaskCompletionSource with the whole raw message (header included) --
    /// ExecuteAsync re-parses flags/payload from it. Raises <see cref="UnmatchedResponse"/>
    /// when no pending entry matches the token. The reserved broadcast token 0xFFFF is
    /// routed to <see cref="OnBroadcastReceived"/> instead of the pending-request lookup --
    /// it never correlates to a caller's request.
    /// </summary>
    private void HandleIncoming(RawMessage message)
    {
        ReadOnlySpan<byte> data = message.Data.Span;
        if (data.Length < 4)
            throw new XapParseException($"Response frame needs at least 4 bytes, got {data.Length}.");

        ushort token = (ushort)(data[0] | (data[1] << 8));

        if (token == 0xFFFF)
        {
            OnBroadcastReceived(message);
            return;
        }

        if (_pending.TryRemove(token, out TaskCompletionSource<RawMessage>? tcs))
        {
            // Reconcile secure state BEFORE completing the awaiter: the generated command
            // method unwatches its token as soon as ExecuteAsync returns, so reconciliation
            // must already have happened by then.
            if (((ResponseFlags)data[2] & ResponseFlags.SecureFailure) != 0)
                _secureUnlocked = false;

            OnResponseCorrelated(token, message);

            tcs.TrySetResult(message);
        }
        else
        {
            UnmatchedResponse?.Invoke(message);
        }
    }

    /// <summary>
    /// Overridden by generated code when the spec declares broadcast_messages; the default
    /// no-op here means a spec with none still compiles and simply drops token 0xFFFF frames.
    /// </summary>
    protected virtual void OnBroadcastReceived(RawMessage message) { }

    /// <summary>
    /// Overridden by generated code when the spec declares GET_SECURE_STATUS, to reconcile the
    /// secure-state cache synchronously on the receive-loop thread. Not intended for direct
    /// external use.
    /// </summary>
    protected virtual void OnResponseCorrelated(ushort token, RawMessage message) { }

    /// <summary>Raises <see cref="BroadcastError"/> from generated broadcast-dispatch code
    /// (a field-like event can only be raised from its declaring class, so generated code
    /// can't invoke it directly). Not intended for direct external use.</summary>
    protected void OnBroadcastError(Exception ex) => BroadcastError?.Invoke(ex);

    /// <summary>
    /// Awaits every handler in a multicast <see cref="Func{T, Task}"/> event (a plain
    /// <c>await handler(arg)</c> only awaits the last one). Not intended for direct external use.
    /// </summary>
    protected static async Task AwaitAllAsync<T>(Func<T, Task>? handler, T arg)
    {
        if (handler is null)
            return;

        Delegate[] invocationList = handler.GetInvocationList();
        if (invocationList.Length == 1)
        {
            await ((Func<T, Task>)invocationList[0])(arg).ConfigureAwait(false);
            return;
        }

        var tasks = new Task[invocationList.Length];
        for (int i = 0; i < invocationList.Length; i++)
            tasks[i] = ((Func<T, Task>)invocationList[i])(arg);
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Shutdown: stops the receive loop, faults every still-pending request (so a
    /// caller awaiting ExecuteAsync/SendRequestAsync gets an immediate, clear
    /// exception instead of hanging or waiting out the request timeout), then disposes
    /// the transport if it supports it.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _cts.Cancel();
        try
        {
            await _receiveLoopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: _cts.Cancel() above surfaces as OperationCanceledException from
            // the receive loop's own cancellation-aware await. Any other exception (e.g.
            // a real transport faulting mid-enumeration) is a genuine bug and is allowed
            // to propagate rather than being silently swallowed here.
        }
        finally
        {
            // Runs even when the receive loop faulted with a genuine transport exception
            // (which deliberately propagates out of this method): pending requests, the
            // transport, and _cts must still be cleaned up.
            FaultAllPending(new XapException("Transport closed before a response arrived."));

            switch (_transport)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
                default:
                    break;
            }

            _cts.Dispose();
        }
    }

    /// <summary>
    /// Completes every still-registered pending request with <paramref name="ex"/>.
    /// Snapshots the dictionary's keys before removing from it -- ConcurrentDictionary
    /// doesn't throw on concurrent mutation the way a plain Dictionary would, but
    /// iterating .Keys directly while removing from the same dictionary is still the
    /// kind of thing worth avoiding outright rather than relying on implementation
    /// details of TryRemove-while-enumerating.
    /// </summary>
    private void FaultAllPending(Exception ex)
    {
        foreach (ushort token in _pending.Keys.ToArray())
        {
            if (_pending.TryRemove(token, out TaskCompletionSource<RawMessage>? tcs))
                tcs.TrySetException(ex);
        }
    }
}
