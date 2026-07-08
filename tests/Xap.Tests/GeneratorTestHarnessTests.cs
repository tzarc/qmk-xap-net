// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Reflection;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Xap.Tests;

/// <summary>
/// Proves the harness's plumbing: it really invokes the real
/// <see cref="SourceGenerator.XapSpecGenerator"/> through <see cref="CSharpGeneratorDriver"/>,
/// really forwards generator diagnostics separately from compile diagnostics, and really
/// attempts to compile and load whatever the generator emits.
/// </summary>
public class GeneratorTestHarnessTests
{
    [Fact]
    public void Run_OnRealSpec_EmitsSourcesWithNoGeneratorDiagnostics()
    {
        GenResult result = GeneratorTestHarness.Run(TestSpecs.Load("0.3.0"));

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.NotEmpty(result.Sources);
        Assert.Contains(result.Sources, s => s.HintName == "Xap.XapClient.g.cs");
        Assert.Contains(result.Sources, s => s.HintName == "Xap.Subsystems.g.cs");
        Assert.Contains(result.Sources, s => s.HintName == "Xap.Constants.g.cs");
        // The real 0.3.0 spec declares response_flags, routes with struct members, and
        // broadcast_messages, so the conditional sources are emitted too.
        Assert.Contains(result.Sources, s => s.HintName == "Xap.ResponseFlags.g.cs");
        Assert.Contains(result.Sources, s => s.HintName == "Xap.Types.g.cs");
        Assert.Contains(result.Sources, s => s.HintName == "Xap.Broadcasts.g.cs");

        Assert.Empty(result.CompileDiagnostics);
        Assert.NotNull(result.CompiledAssembly);
        Assert.NotNull(result.CompiledAssembly.GetType("Xap.XapClient"));
    }

    [Fact]
    public void Run_OnMalformedSpec_ReportsVisibleXap0001AndEmitsNoSources()
    {
        // Proves the try/catch around JsonSpecLoader.Read in XapSpecGenerator.Emit is really
        // wired up: malformed JSON still throws, and the generator must turn that into a visible
        // XAP0001 diagnostic rather than swallowing it or emitting partial/garbage sources.
        GenResult result = GeneratorTestHarness.Run("not json");

        Assert.Empty(result.Sources);
        Diagnostic diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("XAP0001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void RunAndCompileOrThrow_OnRealSpec_ReturnsCompiledAssemblyWithXapClientType()
    {
        Assembly assembly = GeneratorTestHarness.RunAndCompileOrThrow(TestSpecs.Load("0.3.0"));

        Assert.NotNull(assembly);
        Assert.NotNull(assembly.GetType("Xap.XapClient"));
    }

    [Fact]
    public void Run_OnMinimalValidSpec_EmitsOnlyTheUnconditionalSourcesAndCompiles()
    {
        // A minimal "{}" document has no routes, response_flags, or broadcast_messages, so
        // JsonSpecLoader.Read succeeds with an empty model and the generator should still emit
        // exactly the three unconditional sources (Subsystems, Constants, XapClient) while
        // skipping the three conditional ones (ResponseFlags, Types, Broadcasts).
        GenResult result = GeneratorTestHarness.Run("{}");

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Equal(3, result.Sources.Count);
        Assert.Contains(result.Sources, s => s.HintName == "Xap.XapClient.g.cs");
        Assert.Contains(result.Sources, s => s.HintName == "Xap.Subsystems.g.cs");
        Assert.Contains(result.Sources, s => s.HintName == "Xap.Constants.g.cs");
        Assert.DoesNotContain(result.Sources, s => s.HintName is "Xap.ResponseFlags.g.cs" or "Xap.Types.g.cs" or "Xap.Broadcasts.g.cs");

        Assert.Empty(result.CompileDiagnostics);
        Assert.NotNull(result.CompiledAssembly);
    }

    [Fact]
    public void RunAndCompileOrThrow_OnMinimalValidSpec_ReturnsCompiledAssembly()
    {
        Assembly assembly = GeneratorTestHarness.RunAndCompileOrThrow("{}");

        Assert.NotNull(assembly.GetType("Xap.XapClient"));
    }

    [Fact]
    public void Run_OnSpecWithOnlyPrimitiveRoutes_SkipsTypesSource()
    {
        // Discriminates HasAnyStructDefinitions's actual predicate: a spec whose only route has
        // a primitive return_type ("u32") and no "struct" anywhere must NOT trigger Xap.Types.g.cs.
        // A too-broad "any non-empty return/request type" check (the bug this test would have
        // caught) would wrongly emit it here, even though there's nothing to generate.
        const string spec = /*lang=json,strict*/ """
            {
              "routes": {
                "0x00": {
                  "type": "command",
                  "name": "Version Query",
                  "define": "VERSION_QUERY",
                  "return_type": "u32"
                }
              }
            }
            """;

        GenResult result = GeneratorTestHarness.Run(spec);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.DoesNotContain(result.Sources, s => s.HintName == "Xap.Types.g.cs");
        Assert.Empty(result.CompileDiagnostics);
        Assert.NotNull(result.CompiledAssembly);
    }
}
