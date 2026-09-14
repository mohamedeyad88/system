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

    [Theory]
    // What the live server returned on 2026-09-14: a site-relative path.
    [InlineData("/api/download/ApexPrintOS-v2.2.0-Trial.zip", "https://apexprint.me/api/download/ApexPrintOS-v2.2.0-Trial.zip")]
    [InlineData("https://apexprint.me/downloads/ApexPrintOS-Setup-2.8.3.exe", "https://apexprint.me/downloads/ApexPrintOS-Setup-2.8.3.exe")]
    [InlineData("", "")]
    [InlineData("javascript:alert(1)", "")]
    [InlineData("file:///C:/Windows/System32/calc.exe", "")]
    public void DownloadUrl_IsResolvedAgainstTheServer_AndOnlyWebLinksSurvive(string given, string expected)
    {
        Assert.Equal(expected, UpdateCheckService.AbsoluteDownloadUrl(given, UpdateCheckService.DefaultEndpoint));
    }

    [Fact]
    public void PricingLink_CarriesNoDeviceOrCustomer()
    {
        var url = WebLinks.PricingUrl(arabic: true, reason: "trial-ended");
        Assert.Equal("https://apexprint.me/ar/pricing?src=app&reason=trial-ended", url);
    }
}
