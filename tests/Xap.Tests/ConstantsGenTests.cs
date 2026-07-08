// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Reflection;
using Xunit;

namespace Xap.Tests;

/// <summary>
/// Runs the real <see cref="SourceGenerator.Generators.ConstantsGenerator"/> (via
/// <see cref="GeneratorTestHarness"/>) against the real 0.3.0 spec, compiles what it emits, and
/// exercises the actually-generated <c>XapConstants</c> fields by reflection. Nothing here
/// reimplements the bit-math or naming logic -- every assertion is against a value produced by
/// the generated code itself.
/// </summary>
public class ConstantsGenTests
{
    [Fact]
    public void CapabilityBit_UsesRouteId_NotOrdinal()
    {
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type c = asm.GetType("Xap.XapConstants")!;
        uint bit = (uint)c.GetField("QmkGetHardwareIdBit")!.GetValue(null)!;
        Assert.Equal(1u << 0x08, bit);   // route id 0x08, not its position in the list
    }

    [Fact]
    public void SubsystemRouteId_MatchesTopLevelHexKey()
    {
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type c = asm.GetType("Xap.XapConstants")!;
        byte route = (byte)c.GetField("RouteQmk")!.GetValue(null)!;
        Assert.Equal((byte)0x01, route);
    }

    [Fact]
    public void CapabilityBit_NotFirstOrLastInParent_UsesOwnId_NotLocalPosition()
    {
        // AUDIO's own routes dictionary is {0x01: CAPABILITIES_QUERY, 0x03: GET_CONFIG,
        // 0x04: SET_CONFIG, 0x05: SAVE_CONFIG} -- note there is no 0x02, so GET_CONFIG's local
        // ordinal within its parent (index 1, second of four) does NOT match its own hex id
        // (0x03). GET_CONFIG is neither first nor last in this dictionary. A naive "count
        // position within parent" implementation would compute 1u << 1 here; the correct
        // implementation must use the route's own id, 1u << 0x03.
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type c = asm.GetType("Xap.XapConstants")!;
        uint bit = (uint)c.GetField("AudioGetConfigBit")!.GetValue(null)!;
        Assert.Equal(1u << 0x03, bit);
        Assert.NotEqual(1u << 1, bit);
    }

    [Fact]
    public void NestedLightingBacklight_GetConfigBit_IsDisambiguatedByFullAncestorChain()
    {
        // LIGHTING's three nested routers (BACKLIGHT/RGBLIGHT/RGB_MATRIX) all declare their own
        // GET_CONFIG command, so the bit constant name must fold the full ancestor chain
        // (LIGHTING, BACKLIGHT) into its name to stay unique -- matching EnumGenerator's naming
        // scheme for route enums.
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type c = asm.GetType("Xap.XapConstants")!;
        uint bit = (uint)c.GetField("LightingBacklightGetConfigBit")!.GetValue(null)!;
        Assert.Equal(1u << 0x03, bit);
    }
}
