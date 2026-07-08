// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Xap.Tests;

/// <summary>
/// <see cref="GeneratorTestHarness.Run"/> builds its compiler references from whatever's
/// currently loaded in the AppDomain (<c>AppDomain.CurrentDomain.GetAssemblies()</c>). .NET only
/// loads a referenced assembly on first real use, so whether Xap.Core.dll -- which generated
/// code now references directly (<c>XapParseException</c>, and eventually
/// <c>XapResponseException</c>/<c>XapTimeoutException</c>/etc.) -- is already loaded by the time a
/// given test's harness run happens would otherwise depend on test execution order (observed in
/// practice: whichever test happened to run first got a compile error, "XapParseException could
/// not be found", because the harness's reference list snapshot didn't include it yet).
///
/// A module initializer runs once, before any type in this assembly is used (i.e. before any test
/// method), so touching Xap.Core here guarantees it's always loaded in time regardless of
/// which test runs first. Note: merely evaluating `typeof(XapParseException)` is not enough --
/// that alone did not reliably materialize a `System.Reflection.Assembly` entry in
/// `AppDomain.CurrentDomain.GetAssemblies()` in testing. Actually dereferencing `.Assembly` forces
/// full assembly resolution.
/// </summary>
internal static class ModuleInitializer
{
    [ModuleInitializer]
    public static void EnsureXapBindingsIsLoaded() => _ = typeof(XapParseException).Assembly;
}
