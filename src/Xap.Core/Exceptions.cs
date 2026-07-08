// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

namespace Xap;

public class XapException(string message) : Exception(message)
{
}

public class XapResponseException : XapException
{
    public ResponseFlags Flags { get; }
    public ushort Token { get; }
    public XapResponseException(ushort token, ResponseFlags flags)
        : base($"XAP request 0x{token:X4} failed (flags: {flags}).")
    {
        (Token, Flags) = (token, flags);
    }
}

public class XapTimeoutException(ushort token) : XapException($"XAP request 0x{token:X4} timed out.")
{
    public ushort Token { get; } = token;
}

public class XapSecureFailureException : XapResponseException
{
    public bool WasPreSendGuard { get; }

    public XapSecureFailureException(ushort token, ResponseFlags flags) : base(token, flags) { }

    private XapSecureFailureException()
        : base(0x0000, 0)
    {
        WasPreSendGuard = true;
    }

    public static XapSecureFailureException PreSendGuard() => new();
}

public class XapRouteUnavailableException(string routeName) : XapException($"Route '{routeName}' is not available in the connected firmware.")
{
    public string RouteName { get; } = routeName;
}

public class XapParseException(string message) : XapException(message)
{
}
