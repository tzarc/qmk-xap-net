// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.RegularExpressions;

namespace Xap.SourceGenerator.Helpers;

/// <summary>
/// Shared naming helper for generated code: method names for XapClient command methods
/// (<see cref="ToMethodName"/>) and PascalCase class/enum/field names from SCREAMING_SNAKE_CASE
/// or free-text spec names (<see cref="ToClassName"/>). Consolidates what used to be five
/// byte-for-byte-identical private ToPascalCase copies (TypeGenerator, EnumGenerator,
/// ConstantsGenerator, ClientGenerator, BroadcastGenerator) into one place. No curated
/// per-define override table: irregular defines fall through to the Invoke*Async fallback
/// rather than requiring a hand-maintained rename list for every new spec version.
/// </summary>
public static class CSharpNaming
{
    /// <summary>
    /// Method-naming algorithm: leading-verb match (GET_/SET_/SAVE_), then *_QUERY, then a safe
    /// Invoke*Async fallback so any unrecognized define still compiles.
    /// <paramref name="returnsData"/> only matters for the *_QUERY rule: a *_QUERY define with no
    /// return value doesn't fit "Get...Async" semantics, so it falls through to the same
    /// Invoke*Async fallback as any other unrecognized define. No real route in the spec
    /// currently exercises a non-data-returning *_QUERY (every real *_QUERY does return data),
    /// so this is a defensive/completeness branch rather than one with observable behavior today.
    /// CAPABILITIES_QUERY/SUBSYSTEM_QUERY never reach here -- callers exclude them before
    /// generating command methods.
    /// </summary>
    public static string ToMethodName(string define, bool returnsData)
        => define.StartsWith("GET_", StringComparison.Ordinal)
            ? "Get" + ToClassName(define.Substring(4)) + "Async"
            : define.StartsWith("SET_", StringComparison.Ordinal)
            ? "Set" + ToClassName(define.Substring(4)) + "Async"
            : define.StartsWith("SAVE_", StringComparison.Ordinal)
            ? "Save" + ToClassName(define.Substring(5)) + "Async"
            : returnsData && define.EndsWith("_QUERY", StringComparison.Ordinal)
            ? "Get" + ToClassName(define.Substring(0, define.Length - "_QUERY".Length)) + "Async"
            : "Invoke" + ToClassName(define) + "Async";

    /// <summary>
    /// SCREAMING_SNAKE_CASE/free-text -> PascalCase: splits on any non-alphanumeric boundary and
    /// title-cases each token (e.g. "BOARD_IDENTIFIERS" -> "BoardIdentifiers", "Vendor ID" ->
    /// "VendorId", "enable" -> "Enable"). Same algorithm every generator's local ToPascalCase
    /// already used, expressed via Regex.Split instead of a hand-rolled boundary scan. Only
    /// invoked when the spec's AdditionalText content actually changes (Roslyn's incremental
    /// pipeline caches everything downstream of an unchanged input), not per keystroke.
    /// </summary>
    public static string ToClassName(string name) => string.Concat(
        Regex.Split(name, "[^a-zA-Z0-9]+")
            .Where(token => token.Length > 0)
            .Select(token => char.ToUpperInvariant(token[0]) + token.Substring(1).ToLowerInvariant()));

    /// <summary>Parses a spec hex id (e.g. "0x02") into its byte value. Shared across generators.</summary>
    public static byte ParseHexByte(string hex)
    {
        string s = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex.Substring(2) : hex;
        return byte.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}
