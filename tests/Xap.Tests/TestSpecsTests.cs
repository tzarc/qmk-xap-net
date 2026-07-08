// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using Xunit;
namespace Xap.Tests;

public class TestSpecsTests
{
    [Fact]
    public void Load_ReturnsRealSpecText_ForEveryShippedVersion()
    {
        foreach (string? v in new[] { "0.0.1", "0.1.0", "0.2.0", "0.3.0" })
        {
            string json = TestSpecs.Load(v);
            Assert.Contains("\"routes\"", json);
            Assert.Contains("\"version\"", json);
        }
    }

    [Fact]
    public void Load_Throws_WhenSpecMissing() =>
        Assert.ThrowsAny<Exception>(() => TestSpecs.Load("9.9.9"));
}
