// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

namespace Xap.Tests;

public static class TestSpecs
{
    public static string RepoRoot { get; } = FindRepoRoot();
    public static string Load(string version)
    {
        string path = Path.Combine(RepoRoot, "spec", $"xap_{version}.json");
        return !File.Exists(path) ? throw new FileNotFoundException($"Spec not found: {path}") : File.ReadAllText(path);
    }
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Xap.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate Xap.slnx above test output.");
    }
}
