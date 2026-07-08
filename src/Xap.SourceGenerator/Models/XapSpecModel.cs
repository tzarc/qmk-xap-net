// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

namespace Xap.SourceGenerator.Models;

internal sealed class XapSpecModel
{
    public string Version { get; set; } = "";
    public bool HasResponseFlags { get; set; }
    public Dictionary<string, RouteNode> Routes { get; set; } = [];
    public BroadcastMessagesModel? BroadcastMessages { get; set; }
}

internal sealed class RouteNode
{
    public string Id { get; set; } = "";        // hex key, e.g. "0x02"
    public bool IsRouter { get; set; }
    public string Define { get; set; } = "";
    public string Permissions { get; set; } = "";   // "" or "secure"
    public string ReturnType { get; set; } = "";
    public string ReturnPurpose { get; set; } = "";
    public string RequestType { get; set; } = "";      // "", "u8", "u32", "u8[32]", "struct", "string"
    public List<StructMember> ReturnStructMembers { get; set; } = [];
    public List<StructMember> RequestStructMembers { get; set; } = [];
    public Dictionary<string, RouteNode> Routes { get; set; } = [];                   // children when IsRouter
}

internal sealed class StructMember
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
}

internal sealed class BroadcastMessagesModel
{
    public Dictionary<string, BroadcastMessage> Messages { get; set; } = [];
}

internal sealed class BroadcastMessage
{
    public string Id { get; set; } = "";
    public string Define { get; set; } = "";
}
