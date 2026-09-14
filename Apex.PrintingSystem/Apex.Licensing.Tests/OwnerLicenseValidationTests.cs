using System.IO;
using System.Text.Json;
using Xunit;

namespace Apex.Licensing.Tests;

/// <summary>Verifies the locally-signed owner license validates for THIS device.</summary>
public class OwnerLicenseValidationTests
{
    [Fact]
    public void GeneratedOwnerLicense_VerifiesAndBindsToThisDevice()
    {
        const string path = @"D:\Apex\publish\Apex-License-OwnerDevice.apex";
        Assert.True(File.Exists(path), "license file not found");

        var json = File.ReadAllText(path);
        var signed = JsonSerializer.Deserialize<SignedLicense>(json);
        Assert.NotNull(signed);

        var (isValid, payload) = LicenseCrypto.VerifyLicense(signed!);
        Assert.True(isValid, "signature failed to verify against embedded public key");
        Assert.NotNull(payload);
        // The rule LicenseManager applies: any id this hardware produces (2.8.3 disk choice + legacy).
        Assert.Contains(payload!.DeviceId, MachineIdentity.GetCandidateDeviceIds());
    }
}
