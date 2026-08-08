using System;
using Apex.UI.Services;
using Xunit;

namespace Apex.UI.Tests;

public class UpdateCheckServiceTests
{
    [Theory]
    [InlineData("2.3.0", "2.2.1", 1)]   // newer
    [InlineData("2.2.0", "2.2.0", 0)]   // equal
    [InlineData("2.2.0", "2.3.0", -1)]  // older
    [InlineData("2.2.10", "2.2.9", 1)]  // numeric, not lexical
    [InlineData("2.2.0-beta", "2.2.0", 0)] // suffix stripped
    [InlineData("3.0", "2.9.9", 1)]     // uneven lengths
    public void CompareVersions_ComparesNumericallyAndIgnoresSuffix(string a, string b, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(UpdateCheckService.CompareVersions(a, b)));
    }
}
