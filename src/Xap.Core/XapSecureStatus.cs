// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

namespace Xap;

/// <summary>
/// Secure-route status, as returned by GET_SECURE_STATUS and carried by the SECURE_STATUS
/// broadcast. Per the XAP spec, any value other than the members below must be treated as
/// <see cref="Locked"/> -- so check for <see cref="Unlocked"/> equality, never "not Locked".
/// </summary>
public enum XapSecureStatus : byte
{
    /// <summary>Secure routes are disabled.</summary>
    Locked = 0,

    /// <summary>The unlock sequence has been initiated but is not yet complete.</summary>
    Unlocking = 1,

    /// <summary>Secure routes are allowed.</summary>
    Unlocked = 2,
}
