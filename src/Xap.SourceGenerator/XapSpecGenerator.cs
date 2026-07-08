// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xap.SourceGenerator.Helpers;
using Xap.SourceGenerator.Models;

namespace Xap.SourceGenerator;

[Generator]
public class XapSpecGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Filter AdditionalFiles down to those the build declares as XAP specs via
        // build_metadata.AdditionalFiles.SourceItemType="XapSpec" (see the .targets file
        // that wires this up in the consuming project).
        IncrementalValuesProvider<AdditionalText> xapSpecs = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(pair => pair.Right.GetOptions(pair.Left)
                .TryGetValue("build_metadata.AdditionalFiles.SourceItemType", out string? v) && v == "XapSpec")
            .Select((pair, _) => pair.Left);

        context.RegisterSourceOutput(xapSpecs, static (spc, text) => Emit(spc, text));
    }

    private static void Emit(SourceProductionContext context, AdditionalText text)
    {
        try
        {
            SourceText sourceText = text.GetText() ?? throw new InvalidOperationException("AdditionalText has no text.");
            XapSpecModel model = JsonSpecLoader.Read(sourceText.ToString());

            // Generate ResponseFlags enum from spec bits, when the spec declares any.
            if (model.HasResponseFlags)
                context.AddSource("Xap.ResponseFlags.g.cs", Generators.EnumGenerator.GenerateResponseFlags());

            // Generate types/structs from return/request type definitions, when any route
            // actually declares one.
            if (HasAnyStructDefinitions(model.Routes))
                context.AddSource("Xap.Types.g.cs", Generators.TypeGenerator.GenerateTypes(model));

            // Subsystem enums, route/capability constants, and the client are always emitted --
            // even a spec with no routes still gets a (structurally trivial) client.
            context.AddSource("Xap.Subsystems.g.cs", Generators.EnumGenerator.GenerateSubsystemEnums(model.Routes));
            context.AddSource("Xap.Constants.g.cs", Generators.ConstantsGenerator.GenerateRouteIds(model.Routes));
            context.AddSource("Xap.XapClient.g.cs", Generators.ClientGenerator.GenerateClient(model));

            // Generate broadcast enum and events, when the spec declares any.
            if (model.BroadcastMessages != null)
                context.AddSource("Xap.Broadcasts.g.cs", Generators.BroadcastGenerator.GenerateBroadcasts(model.BroadcastMessages));
        }
        catch (Exception ex) when (!IsOutOfMemory(ex))
        {
            // Always a visible diagnostic -- never swallowed, never hidden severity. This is
            // what lets a broken/edge-case spec fail loudly at build time instead of silently
            // producing no generated code.
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("XAP0001", "XAP spec parse error",
                    "Failed to parse XAP spec '{0}': {1}", "XapGenerator",
                    DiagnosticSeverity.Warning, isEnabledByDefault: true),
                Location.None, text.Path, ex.Message));
        }
    }

    private static bool HasAnyStructDefinitions(Dictionary<string, RouteNode> routes)
    {
        foreach (RouteNode route in routes.Values)
        {
            if (route.ReturnType == "struct" || route.RequestType == "struct")
                return true;

            if (route.IsRouter && HasAnyStructDefinitions(route.Routes))
                return true;
        }

        return false;
    }

    private static bool IsOutOfMemory(Exception ex) => ex is OutOfMemoryException;
}
