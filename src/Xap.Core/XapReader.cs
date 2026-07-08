// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;

namespace Xap;

public delegate T XapReader<out T>(ReadOnlySpan<byte> payload);

public static partial class XapReaders
{
    public static byte U8(ReadOnlySpan<byte> src) => ReadU8(src, "XapReaders.U8");
    public static ushort U16(ReadOnlySpan<byte> src) => ReadU16(src, "XapReaders.U16");
    public static uint U32(ReadOnlySpan<byte> src) => ReadU32(src, "XapReaders.U32");
    public static ulong U64(ReadOnlySpan<byte> src) => ReadU64(src, "XapReaders.U64");

    private static byte ReadU8(ReadOnlySpan<byte> src, string name) => src.Length < 1 ? throw new XapParseException($"{name} needs 1 byte, got {src.Length}.") : src[0];

    private static ushort ReadU16(ReadOnlySpan<byte> src, string name)
    {
        return src.Length < 2
            ? throw new XapParseException($"{name} needs 2 bytes, got {src.Length}.")
            : BinaryPrimitives.ReadUInt16LittleEndian(src);
    }

    private static uint ReadU32(ReadOnlySpan<byte> src, string name)
    {
        return src.Length < 4
            ? throw new XapParseException($"{name} needs 4 bytes, got {src.Length}.")
            : BinaryPrimitives.ReadUInt32LittleEndian(src);
    }

    private static ulong ReadU64(ReadOnlySpan<byte> src, string name)
    {
        return src.Length < 8
            ? throw new XapParseException($"{name} needs 8 bytes, got {src.Length}.")
            : BinaryPrimitives.ReadUInt64LittleEndian(src);
    }

    public static byte[] Bytes(ReadOnlySpan<byte> src) => src.ToArray();
    public static ushort[] U16Array(ReadOnlySpan<byte> src, int count) => ReadArray(src, count, 2, U16, "XapReaders.U16Array");
    public static uint[] U32Array(ReadOnlySpan<byte> src, int count) => ReadArray(src, count, 4, U32, "XapReaders.U32Array");
    public static ulong[] U64Array(ReadOnlySpan<byte> src, int count) => ReadArray(src, count, 8, U64, "XapReaders.U64Array");

    private static T[] ReadArray<T>(ReadOnlySpan<byte> src, int count, int width, XapReader<T> readElement, string name)
    {
        if (src.Length < count * width)
            throw new XapParseException($"{name} needs {count * width} bytes, got {src.Length}.");
        var arr = new T[count];
        for (int i = 0; i < count; i++)
            arr[i] = readElement(src.Slice(i * width, width));
        return arr;
    }

    public static string String(ReadOnlySpan<byte> src) => System.Text.Encoding.UTF8.GetString(src);
}
