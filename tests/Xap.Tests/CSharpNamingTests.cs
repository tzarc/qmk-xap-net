// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using Xap.SourceGenerator.Helpers;
using Xunit;

namespace Xap.Tests;

/// <summary>
/// The shared naming helper that replaces every generator's private ToPascalCase copy. No
/// curated override table: unrecognized defines fall through to Invoke*Async (see
/// ToMethodName_Unrecognized_FallsBackToInvokeAsync).
/// </summary>
public class CSharpNamingTests
{
    [Theory]
    [InlineData("SECURE_UNLOCK", "InvokeSecureUnlockAsync")]
    [InlineData("SECURE_LOCK", "InvokeSecureLockAsync")]
    [InlineData("GET_LAYER_COUNT", "GetLayerCountAsync")]
    [InlineData("SET_CONFIG", "SetConfigAsync")]
    [InlineData("SAVE_CONFIG", "SaveConfigAsync")]
    public void ToMethodName_MapsCorrectly(string define, string expected) =>
        Assert.Equal(expected, CSharpNaming.ToMethodName(define, returnsData: true));

    // ---- *_QUERY suffix rule ----------------------------------------------------------------

    [Fact]
    public void ToMethodName_QuerySuffix_ReturnsDataTrue_DropsSuffixAndPrefixesGet() =>
        Assert.Equal("GetVersionAsync", CSharpNaming.ToMethodName("VERSION_QUERY", returnsData: true));

    [Fact]
    public void ToMethodName_QuerySuffix_ReturnsDataFalse_FallsThroughToInvokeFallback() =>
        // No real route in the spec exercises this (every real *_QUERY command returns data);
        // documented choice: without a return value, "Get...Async" semantics don't fit, so a
        // void _QUERY define falls through to the same Invoke*Async fallback as any other
        // unrecognized define, rather than emitting a nonsensical GetXAsync with no return type.
        Assert.Equal("InvokeVersionQueryAsync", CSharpNaming.ToMethodName("VERSION_QUERY", returnsData: false));

    // ---- Fallback rule ----------------------------------------------------------------------

    [Fact]
    public void ToMethodName_Unrecognized_FallsBackToInvokeAsync() =>
        Assert.Equal("InvokeFooBarAsync", CSharpNaming.ToMethodName("FOO_BAR", returnsData: true));

    // ---- ToClassName --------------------------------------------------------------------------

    [Theory]
    [InlineData("BOARD_IDENTIFIERS", "BoardIdentifiers")]
    [InlineData("Vendor ID", "VendorId")]
    [InlineData("enable", "Enable")]
    public void ToClassName_ConvertsToPascalCase(string input, string expected) =>
        Assert.Equal(expected, CSharpNaming.ToClassName(input));
}
