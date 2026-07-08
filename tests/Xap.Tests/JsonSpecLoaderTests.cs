// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using Xap.SourceGenerator.Helpers;
using Xap.SourceGenerator.Models;
using Xunit;
namespace Xap.Tests;

public class JsonSpecLoaderTests
{
    [Fact]
    public void Read_HandlesWhitespaceFormattedEmptyArray()
    {
        // The exact shape the old parser threw on: an array whose ']' follows a newline.
        XapSpecModel model = JsonSpecLoader.Read(/*lang=json,strict*/ "{ \"version\": \"0.3.0\", \"empty\": [\n    ], \"routes\": {} }");
        Assert.Equal("0.3.0", model.Version);
    }

    [Fact]
    public void Read_ParsesRealSpec_TopLevel()
    {
        XapSpecModel m = JsonSpecLoader.Read(TestSpecs.Load("0.3.0"));
        Assert.Equal("0.3.0", m.Version);
        Assert.True(m.HasResponseFlags);
        Assert.NotNull(m.BroadcastMessages);
        Assert.True(m.Routes.ContainsKey("0x00"));   // XAP
        Assert.True(m.Routes.ContainsKey("0x01"));   // QMK
    }

    [Fact]
    public void Read_ParsesNestedRouters_AndCommands()
    {
        XapSpecModel m = JsonSpecLoader.Read(TestSpecs.Load("0.3.0"));
        RouteNode qmk = m.Routes["0x01"];
        Assert.True(qmk.IsRouter);
        RouteNode hardwareId = qmk.Routes["0x08"];
        Assert.False(hardwareId.IsRouter);
        Assert.Equal("GET_HARDWARE_ID", hardwareId.Define);
        Assert.Equal("u32[4]", hardwareId.ReturnType);
        // Lighting → Backlight is a second-level router
        Assert.True(m.Routes["0x06"].Routes["0x02"].IsRouter);
    }

    [Fact]
    public void Read_PopulatesStructMembers()   // the field the old loader never read
    {
        XapSpecModel m = JsonSpecLoader.Read(TestSpecs.Load("0.3.0"));
        RouteNode boardIds = m.Routes["0x01"].Routes["0x02"];       // BOARD_IDENTIFIERS
        Assert.Equal("struct", boardIds.ReturnType);
        Assert.Collection(boardIds.ReturnStructMembers,
            x => Assert.Equal("u16", x.Type),   // Vendor ID
            x => Assert.Equal("u16", x.Type),   // Product ID
            x => Assert.Equal("u16", x.Type),   // Product Version
            x => Assert.Equal("u32", x.Type));  // QMK Unique Identifier

        RouteNode getKeycode = m.Routes["0x04"].Routes["0x03"];     // GET_KEYMAP_KEYCODE
        Assert.Equal(3, getKeycode.RequestStructMembers.Count);
    }

    [Fact]
    public void Read_ParsesSecurePermission()
    {
        XapSpecModel m = JsonSpecLoader.Read(TestSpecs.Load("0.3.0"));
        Assert.Equal("secure", m.Routes["0x01"].Routes["0x07"].Permissions); // BOOTLOADER_JUMP
    }

    [Theory]
    [InlineData("0.0.1")]
    [InlineData("0.1.0")]
    [InlineData("0.2.0")]
    [InlineData("0.3.0")]
    public void Read_DoesNotThrow_OnAnyShippedSpec(string version) =>
        _ = JsonSpecLoader.Read(TestSpecs.Load(version));
}
