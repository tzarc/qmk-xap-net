// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

namespace Xap;

[Flags]
public enum ResponseFlags : byte
{
    None = 0,
    Success = 1 << 0,
    SecureFailure = 1 << 1,
}
