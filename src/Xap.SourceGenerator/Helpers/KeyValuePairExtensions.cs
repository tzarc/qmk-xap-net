// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

namespace Xap.SourceGenerator.Helpers;

internal static class KeyValuePairExtensions
{
    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> kvp, out TKey key, out TValue value)
    {
        key = kvp.Key;
        value = kvp.Value;
    }
}
