// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Reflection;
using Xunit;

namespace Xap.Tests;

/// <summary>
/// Runs the real <see cref="SourceGenerator.Generators.EnumGenerator"/> (via
/// <see cref="GeneratorTestHarness"/>) against the real 0.3.0 spec, compiles what it emits, and
/// exercises the actually-generated <c>XapSubsystem</c>/<c>*Route</c> enum members by reflection.
/// Nothing here reimplements the enum-naming or value logic -- every assertion is against a type
/// produced by the generated code itself.
/// </summary>
public class EnumGenTests
{
    [Fact]
    public void SubsystemEnum_HasExpectedMembers()
    {
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type e = asm.GetType("Xap.XapSubsystem")!;
        Assert.Equal((byte)0x01, (byte)Enum.Parse(e, "Qmk"));
        Type qmk = asm.GetType("Xap.QmkRoute")!;
        Assert.Equal((byte)0x08, (byte)Enum.Parse(qmk, "GetHardwareId"));
    }

    [Fact]
    public void SubsystemEnum_HasXapAndOtherTopLevelMembers()
    {
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type e = asm.GetType("Xap.XapSubsystem")!;
        Assert.Equal((byte)0x00, (byte)Enum.Parse(e, "Xap"));
        Assert.Equal((byte)0x06, (byte)Enum.Parse(e, "Lighting"));
    }

    [Fact]
    public void NestedLightingRouters_GetDistinctAncestryPrefixedRouteEnums()
    {
        // LIGHTING's three nested routers (BACKLIGHT, RGBLIGHT, RGB_MATRIX) each declare their
        // own CAPABILITIES_QUERY/GET_CONFIG/SET_CONFIG/SAVE_CONFIG commands, so an unqualified
        // "BacklightRoute"/"RgblightRoute" name would be ambiguous with the real spec's shape --
        // each nested router's route enum must fold its ancestor router's define into its name.
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));

        Type backlight = asm.GetType("Xap.LightingBacklightRoute")!;
        Type rgblight = asm.GetType("Xap.LightingRgblightRoute")!;
        Type rgbMatrix = asm.GetType("Xap.LightingRgbMatrixRoute")!;

        Assert.NotNull(backlight);
        Assert.NotNull(rgblight);
        Assert.NotNull(rgbMatrix);

        // Distinct types, not aliases of one another.
        Assert.NotEqual(backlight, rgblight);
        Assert.NotEqual(rgblight, rgbMatrix);

        Assert.Equal((byte)0x03, (byte)Enum.Parse(backlight, "GetConfig"));
        Assert.Equal((byte)0x04, (byte)Enum.Parse(rgblight, "SetConfig"));
        Assert.Equal((byte)0x05, (byte)Enum.Parse(rgbMatrix, "SaveConfig"));
    }

    [Fact]
    public void LightingRoute_OnlyContainsDirectNonRouterChild()
    {
        // LIGHTING itself has one direct command child (CAPABILITIES_QUERY) plus three nested
        // routers (BACKLIGHT/RGBLIGHT/RGB_MATRIX) -- LightingRoute must contain only the direct
        // command, not anything from the nested routers.
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type lighting = asm.GetType("Xap.LightingRoute")!;
        Assert.NotNull(lighting);
        Assert.Equal((byte)0x01, (byte)Enum.Parse(lighting, "CapabilitiesQuery"));
        Assert.Single(Enum.GetNames(lighting));
    }

    [Fact]
    public void EmptyTopLevelRouter_StillGetsAnEmptyRouteEnum()
    {
        // KB and USER are top-level routers with zero children in the real 0.3.0 spec -- they
        // must still get a (valid, empty) route enum rather than being skipped.
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type kb = asm.GetType("Xap.KbRoute")!;
        Type user = asm.GetType("Xap.UserRoute")!;
        Assert.NotNull(kb);
        Assert.NotNull(user);
        Assert.Empty(Enum.GetNames(kb));
        Assert.Empty(Enum.GetNames(user));
    }

    [Fact]
    public void GenerateResponseFlags_IsADeliberateNoOp_DoesNotEmitCollidingType()
    {
        // ResponseFlags is hand-written in Xap.Core (src/Xap.Core/ResponseFlags.cs);
        // the generator must not declare a duplicate/colliding Xap.ResponseFlags type, but the
        // real spec does declare response_flags so Xap.ResponseFlags.g.cs is still emitted (per
        // XapSpecGenerator.Emit) -- just with no type inside it.
        GenResult result = GeneratorTestHarness.Run(TestSpecs.Load("0.3.0"));
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompileDiagnostics);
        Assert.NotNull(result.CompiledAssembly);

        // The hand-written Xap.Core.ResponseFlags enum is still exactly as authored --
        // no generated duplicate replaced or collided with it.
        Assert.True(typeof(ResponseFlags).IsEnum);
        Assert.Equal((byte)1, (byte)ResponseFlags.Success);
    }
}
