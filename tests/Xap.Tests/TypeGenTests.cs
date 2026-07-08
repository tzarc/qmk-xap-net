// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Reflection;
using Xunit;

namespace Xap.Tests;

/// <summary>
/// Runs the real <see cref="SourceGenerator.Generators.TypeGenerator"/> (via
/// <see cref="GeneratorTestHarness"/>) against the real 0.3.0 spec, compiles what it emits, and
/// exercises the actually-generated <c>XapVersion.FromBcd</c> and struct
/// <c>ReadFrom</c>/<c>SerializedSize</c> members by reflection. Nothing here reimplements BCD
/// decoding or little-endian field parsing -- every assertion is against a value produced by the
/// generated code itself.
/// </summary>
public class TypeGenTests
{
    // Reflection can't box a ReadOnlySpan<byte> into an `object[]` argument for
    // MethodInfo.Invoke (ref structs can't be boxed), so a direct `readFrom.Invoke(null, new
    // object[] { wire })` is not an option. Instead, bind a delegate of this shape -- matching
    // every generated `static T ReadFrom(ReadOnlySpan<byte>)` exactly -- to the concrete
    // generated MethodInfo via Delegate.CreateDelegate, then invoke that delegate directly (a
    // normal compiled call, so the Span parameter is fine). The delegate itself is constructed
    // through a small reflection-emitted generic helper since the struct type T is only known at
    // runtime.
    private delegate T ReadFromDelegate<T>(ReadOnlySpan<byte> src);

    private static object InvokeReadFrom(Type structType, MethodInfo readFrom, byte[] wire)
    {
        MethodInfo invoker = typeof(TypeGenTests)
            .GetMethod(nameof(InvokeReadFromGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(structType);

        try
        {
            return invoker.Invoke(null, [readFrom, wire])!;
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            // Unwrap so callers see the real exception thrown by the generated ReadFrom (e.g.
            // XapParseException), not the reflection-invocation wrapper.
            throw tie.InnerException;
        }
    }

    private static T InvokeReadFromGeneric<T>(MethodInfo readFrom, byte[] wire)
    {
        var del = (ReadFromDelegate<T>)Delegate.CreateDelegate(typeof(ReadFromDelegate<T>), readFrom);
        return del(wire); // byte[] -> ReadOnlySpan<byte> is an implicit conversion at this normal call site.
    }

    [Fact]
    public void XapVersion_FromBcd_DecodesBothProtocolExamples()
    {
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type? versionType = asm.GetType("Xap.XapVersion");
        Assert.NotNull(versionType);
        MethodInfo fromBcd = versionType!.GetMethod("FromBcd")!;

        // 3.2.115 => 0x03020115 (bytes {0x15,0x01,0x02,0x03} little-endian), per VERSION_QUERY's
        // route description in xap_0.3.0.json.
        object v1 = fromBcd.Invoke(null, [0x03020115u])!;
        Assert.Equal((byte)3, versionType.GetField("Major")!.GetValue(v1));
        Assert.Equal((byte)2, versionType.GetField("Minor")!.GetValue(v1));
        Assert.Equal((ushort)115, versionType.GetField("Patch")!.GetValue(v1));

        // 3.17.192 => 0x03170192 (bytes {0x92,0x01,0x17,0x03}), SOURCEGENERATOR_SPEC.md's second
        // worked BCD example.
        object v2 = fromBcd.Invoke(null, [0x03170192u])!;
        Assert.Equal((byte)3, versionType.GetField("Major")!.GetValue(v2));
        Assert.Equal((byte)17, versionType.GetField("Minor")!.GetValue(v2));
        Assert.Equal((ushort)192, versionType.GetField("Patch")!.GetValue(v2));
    }

    [Fact]
    public void BoardIdentifiers_ReadFrom_DecodesLittleEndianFieldsOfEachWidth()
    {
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type? structType = asm.GetType("Xap.GetBoardIdentifiers"); // name derived from the QMK route's define
        Assert.NotNull(structType);
        MethodInfo readFrom = structType!.GetMethod("ReadFrom")!;

        Assert.Equal(10, (int)structType.GetProperty("SerializedSize")!.GetValue(null)!);

        // Vendor 0x1234, Product 0x5678, Version 0x9ABC, UID 0x11223344 -- each little-endian.
        byte[] wire = [0x34, 0x12, 0x78, 0x56, 0xBC, 0x9A, 0x44, 0x33, 0x22, 0x11];
        object result = InvokeReadFrom(structType, readFrom, wire);

        Assert.Equal((ushort)0x1234, structType.GetField("VendorId")!.GetValue(result));
        Assert.Equal((ushort)0x5678, structType.GetField("ProductId")!.GetValue(result));
        Assert.Equal((ushort)0x9ABC, structType.GetField("ProductVersion")!.GetValue(result));
        Assert.Equal((uint)0x11223344, structType.GetField("QmkUniqueIdentifier")!.GetValue(result));
    }

    [Fact]
    public void BoardIdentifiers_ReadFrom_ThrowsXapParseExceptionWhenSpanTooShort()
    {
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type structType = asm.GetType("Xap.GetBoardIdentifiers")!;
        MethodInfo readFrom = structType.GetMethod("ReadFrom")!;

        byte[] tooShort = [0x01, 0x02, 0x03]; // BoardIdentifiers.SerializedSize is 10.

        Assert.Throws<XapParseException>(() => InvokeReadFrom(structType, readFrom, tooShort));
    }

    [Fact]
    public void GetKeymapKeycodeRequest_ReadFrom_DecodesThreeByteFields()
    {
        // GET_KEYMAP_KEYCODE's request struct -- name gets a "Request" suffix since it's the
        // request-side (not return-side) struct for this route.
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type? structType = asm.GetType("Xap.GetKeymapKeycodeRequest");
        Assert.NotNull(structType);
        MethodInfo readFrom = structType!.GetMethod("ReadFrom")!;

        byte[] wire = [2, 3, 4]; // Layer=2, Row=3, Column=4
        object result = InvokeReadFrom(structType, readFrom, wire);

        Assert.Equal((byte)2, structType.GetField("Layer")!.GetValue(result));
        Assert.Equal((byte)3, structType.GetField("Row")!.GetValue(result));
        Assert.Equal((byte)4, structType.GetField("Column")!.GetValue(result));
    }

    [Fact]
    public void BacklightGetConfig_ReadFrom_DecodesThreeByteFields()
    {
        // Backlight's GET_CONFIG return struct. The real spec reuses GET_CONFIG/SET_CONFIG
        // across Backlight, Rgblight, and RgbMatrix (all nested under the Lighting subsystem), so
        // this struct's name is disambiguated with its immediate parent router's define
        // ("Backlight"), unlike BoardIdentifiers/GetKeymapKeycodeRequest above which sit directly
        // under a top-level subsystem and get no prefix.
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type? structType = asm.GetType("Xap.BacklightGetConfig");
        Assert.NotNull(structType);
        MethodInfo readFrom = structType!.GetMethod("ReadFrom")!;

        byte[] wire = [1, 2, 3]; // enable=1, mode=2, val=3
        object result = InvokeReadFrom(structType, readFrom, wire);

        Assert.Equal((byte)1, structType.GetField("Enable")!.GetValue(result));
        Assert.Equal((byte)2, structType.GetField("Mode")!.GetValue(result));
        Assert.Equal((byte)3, structType.GetField("Val")!.GetValue(result));
    }

    [Fact]
    public void U16ArrayField_ReadFromAndWriteTo_RoundTripLittleEndianElements()
    {
        // None of the real 0.3.0 spec's struct members happen to be arrays, but the generator
        // must still honor element width for array fields (u16[n] -> ushort[], not byte[]), so
        // this drives that path through the same real-generator-and-reflection harness against a
        // small synthetic spec, rather than asserting on a hand-rolled reimplementation.
        const string spec = /*lang=json,strict*/ """
            {
              "routes": {
                "0x01": {
                  "type": "router",
                  "name": "Test",
                  "define": "TEST",
                  "routes": {
                    "0x01": {
                      "type": "command",
                      "name": "Get Array Thing",
                      "define": "GET_ARRAY_THING",
                      "return_type": "struct",
                      "return_struct_members": [
                        { "type": "u16[2]", "name": "Values" }
                      ]
                    }
                  }
                }
              }
            }
            """;

        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(spec);
        Type? structType = asm.GetType("Xap.GetArrayThing");
        Assert.NotNull(structType);
        MethodInfo readFrom = structType!.GetMethod("ReadFrom")!;

        Assert.Equal(4, (int)structType.GetProperty("SerializedSize")!.GetValue(null)!);

        byte[] wire = [0x34, 0x12, 0x78, 0x56]; // {0x1234, 0x5678} little-endian
        object result = InvokeReadFrom(structType, readFrom, wire);

        ushort[] values = (ushort[])structType.GetField("Values")!.GetValue(result)!;
        Assert.Equal(new ushort[] { 0x1234, 0x5678 }, values);

        // Round-trip through WriteTo too. WriteTo is an instance method taking Span<byte>, so
        // (unlike the static, T-returning ReadFrom above) we don't need a generic delegate at
        // all here: CreateDelegate can bind a closed delegate directly to the boxed struct
        // instance we already have from ReadFrom, regardless of its runtime type.
        MethodInfo writeTo = structType.GetMethod("WriteTo")!;
        byte[] dest = new byte[4];
        var del = (WriteToDelegate)Delegate.CreateDelegate(typeof(WriteToDelegate), result, writeTo);
        del(dest);

        Assert.Equal(wire, dest);
    }

    private delegate void WriteToDelegate(Span<byte> dest);

    [Fact]
    public void WriteTo_ThrowsXapParseExceptionWhenSpanTooShort()
    {
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type structType = asm.GetType("Xap.GetBoardIdentifiers")!;
        MethodInfo readFrom = structType.GetMethod("ReadFrom")!;

        byte[] wire = [0x34, 0x12, 0x78, 0x56, 0xBC, 0x9A, 0x44, 0x33, 0x22, 0x11];
        object result = InvokeReadFrom(structType, readFrom, wire);

        MethodInfo writeTo = structType.GetMethod("WriteTo")!;
        byte[] tooSmall = new byte[3]; // SerializedSize is 10.
        var del = (WriteToDelegate)Delegate.CreateDelegate(typeof(WriteToDelegate), result, writeTo);

        Assert.Throws<XapParseException>(() => del(tooSmall));
    }

    [Fact]
    public void XapVersion_WriteTo_ThrowsXapParseExceptionWhenSpanTooShort()
    {
        Assembly asm = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));
        Type versionType = asm.GetType("Xap.XapVersion")!;
        object version = Activator.CreateInstance(versionType, (byte)3, (byte)2, (ushort)115)!;

        MethodInfo writeTo = versionType.GetMethod("WriteTo")!;
        byte[] tooSmall = new byte[1]; // SerializedSize is 4.
        var del = (WriteToDelegate)Delegate.CreateDelegate(typeof(WriteToDelegate), version, writeTo);

        Assert.Throws<XapParseException>(() => del(tooSmall));
    }
}
