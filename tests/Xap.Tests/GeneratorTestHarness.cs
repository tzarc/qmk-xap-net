// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Xap.Tests;

/// <summary>
/// Runs the real <see cref="SourceGenerator.XapSpecGenerator"/> through
/// <see cref="CSharpGeneratorDriver"/> against a spec JSON payload, compiles whatever it
/// emits, and (if compilation succeeds) loads the resulting assembly. This is what makes
/// later tests able to exercise generated code by reflection instead of re-implementing the
/// generator's logic and asserting on a copy of it.
/// </summary>
public readonly record struct GenResult(
    IReadOnlyList<(string HintName, string Source)> Sources,
    IReadOnlyList<Diagnostic> GeneratorDiagnostics,
    IReadOnlyList<Diagnostic> CompileDiagnostics,
    Assembly? CompiledAssembly);

public static class GeneratorTestHarness
{
    private sealed class SpecText(string content) : AdditionalText
    {
        private readonly SourceText _text = SourceText.From(content);

        public override string Path { get; } = "xap_spec.json"; public override SourceText GetText(CancellationToken ct = default) => _text;
    }

    private sealed class SpecOptions : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (key == "build_metadata.AdditionalFiles.SourceItemType")
            { value = "XapSpec"; return true; }
            value = null!;
            return false;
        }
    }

    private sealed class SpecOptionsProvider : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions => new SpecOptions();
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new SpecOptions();
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new SpecOptions();
    }

    // Generated code is designed to live inside a net8.0 project with <ImplicitUsings>enable</ImplicitUsings>
    // (see Xap.Core.csproj / Xap.Tests.csproj) -- the SDK auto-generates exactly this
    // GlobalUsings.g.cs for such a project. The harness's stand-in compilation needs the same
    // ambient usings so it faithfully represents the real consuming environment; without it,
    // otherwise-valid generated code would spuriously fail to compile on bare type names like
    // `TimeSpan` or `Exception` that implicit usings normally resolve.
    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    /// <summary>
    /// Feeds <paramref name="specJson"/> to the real generator as an <see cref="AdditionalText"/>
    /// carrying build_metadata.AdditionalFiles.SourceItemType = "XapSpec" (the metadata filter
    /// the generator's incremental pipeline is expected to use), runs it via
    /// <see cref="CSharpGeneratorDriver"/>, and attempts to compile and load whatever it emits.
    /// </summary>
    public static GenResult Run(string specJson)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var compilation = CSharpCompilation.Create(
            "HarnessAsm",
            [CSharpSyntaxTree.ParseText("// consumer"), CSharpSyntaxTree.ParseText(ImplicitUsings)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new SourceGenerator.XapSpecGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [new SpecText(specJson)],
            parseOptions: CSharpParseOptions.Default,
            optionsProvider: new SpecOptionsProvider());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation? output, out ImmutableArray<Diagnostic> genDiags);

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        var sources = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => (s.HintName, s.SourceText.ToString()))
            .ToList();

        var compileDiags = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assembly? asm = null;
        if (compileDiags.Count == 0)
        {
            using var ms = new MemoryStream();
            EmitResult emit = output.Emit(ms);
            if (emit.Success)
            {
                ms.Position = 0;
                asm = Assembly.Load(ms.ToArray());
            }
            else
            {
                compileDiags = [.. emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)];
            }
        }

        return new GenResult(sources, genDiags, compileDiags, asm);
    }

    /// <summary>
    /// Runs the generator, asserts zero generator diagnostics and zero compile errors, and
    /// returns the loaded assembly. Intended for tests that want to exercise generated types by
    /// reflection once the generator is producing clean output (Phase 2 onward).
    /// </summary>
    public static Assembly RunAndCompileOrThrow(string specJson)
    {
        GenResult r = Run(specJson);
        Assert.Empty(r.GeneratorDiagnostics);
        Assert.True(r.CompileDiagnostics.Count == 0,
            "Generated code failed to compile:\n" + string.Join("\n", r.CompileDiagnostics.Select(d => d.ToString())));
        Assert.NotNull(r.CompiledAssembly);
        return r.CompiledAssembly;
    }
}
