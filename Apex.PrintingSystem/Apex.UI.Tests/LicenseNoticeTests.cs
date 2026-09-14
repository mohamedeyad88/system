using System;
using Apex.Licensing;
using Apex.UI.ViewModels;
using Xunit;

namespace Apex.UI.Tests;

public class LicenseNoticeTests
{
    private static ValidationResult Licence(LicenseType type, int daysLeft, bool perpetual = false) => new()
    {
        IsValid = true,
        Status = LicenseStatus.Valid,
        Type = type,
        DaysRemaining = daysLeft,
        ExpiresUtc = perpetual ? new DateTime(9999, 12, 31) : DateTime.UtcNow.AddDays(daysLeft),
    };

    [Theory]
    [InlineData(31, false)]
    [InlineData(30, true)]
    [InlineData(6, true)]
    public void Subscription_WarnsInItsLastThirtyDays(int daysLeft, bool expected) =>
        Assert.Equal(expected, MainViewModel.LicenseNoticeFor(Licence(LicenseType.Full, daysLeft)).Show);

    [Theory]
    [InlineData(3, false)]
    [InlineData(2, true)]
    [InlineData(0, true)]
    public void Trial_WarnsInItsLastTwoDays(int daysLeft, bool expected)
    {
        var notice = MainViewModel.LicenseNoticeFor(Licence(LicenseType.Trial, daysLeft));
        Assert.Equal(expected, notice.Show);
        Assert.True(notice.IsTrial);
    }

    [Fact]
    public void LastDay_SaysOneDay_NotZero() =>
        Assert.Equal(1, MainViewModel.LicenseNoticeFor(Licence(LicenseType.Trial, 0)).Days);

    [Fact]
    public void Perpetual_NeverWarns() =>
        Assert.False(MainViewModel.LicenseNoticeFor(Licence(LicenseType.Full, 2912187, perpetual: true)).Show);

    [Fact]
    public void AnInvalidLicence_IsTheActivationScreensJob_NotABanner() =>
        Assert.False(MainViewModel.LicenseNoticeFor(new ValidationResult { IsValid = false }).Show);
}
