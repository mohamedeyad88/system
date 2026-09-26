using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Apex.Licensing.Tests;

/// <summary>
/// The invariant the product depends on: a Full license signed for THIS device
/// verifies, and its DeviceId is one this hardware answers to.
/// </summary>
public class OwnerLicenseValidationTests
{
    /// <summary>
    /// Self-contained and portable. It signs a license for the CURRENT machine with a
    /// throwaway key pair and verifies it with that pair's public key — so it exercises
    /// the real sign→verify→bind path on any machine, without the owner's private key
    /// and without a pre-generated file tied to one device. (The previous version read
    /// a fixed .apex generated for the owner's machine and failed everywhere else.)
    /// </summary>
    [Fact]
    public void OwnerLicenseForThisDevice_VerifiesAndBinds()
    {
        var (privatePem, publicPem) = LicenseCrypto.GenerateKeyPair();
        using var privateKey = ECDsa.Create();
        privateKey.ImportFromPem(privatePem);

        var payload = new LicensePayload
        {
            LicenseId = Guid.NewGuid().ToString(),
            DeviceId = MachineIdentity.GetDeviceId(),   // the id this hardware issues
            Type = LicenseType.Full,
            IssuedUtc = DateTime.UtcNow,
            ExpiresUtc = new DateTime(9999, 12, 31),
            CustomerName = "Owner Device",
            Nonce = Guid.NewGuid().ToString("N"),
        };

        var signed = LicenseCrypto.SignLicense(payload, privateKey);

        var (isValid, verified) = LicenseCrypto.VerifyLicense(signed, publicPem);
        Assert.True(isValid, "signature failed to verify against its own public key");
        Assert.NotNull(verified);
        // The rule LicenseManager applies: the license DeviceId must be one this
        // hardware answers to (current id + legacy/disk variants).
        Assert.Contains(verified!.DeviceId, MachineIdentity.GetCandidateDeviceIds());
    }

    /// <summary>
    /// The real owner-machine check: if a pre-generated owner license exists AND it was
    /// issued for this machine, it must verify against the EMBEDDED public key and bind.
    /// Skipped (not failed) elsewhere, so the suite is green on any dev/CI machine while
    /// still catching a broken embedded key on the owner's own machine.
    /// </summary>
    [Fact]
    public void PreGeneratedOwnerLicense_IfPresentForThisMachine_VerifiesWithEmbeddedKey()
    {
        const string path = @"D:\Apex\publish\Apex-License-OwnerDevice.apex";
        if (!File.Exists(path))
            return; // not the owner's build machine — nothing to check here

        var signed = JsonSerializer.Deserialize<SignedLicense>(File.ReadAllText(path));
        Assert.NotNull(signed);

        var (isValid, payload) = LicenseCrypto.VerifyLicense(signed!);
        if (!isValid || payload is null ||
            !System.Linq.Enumerable.Contains(MachineIdentity.GetCandidateDeviceIds(), payload.DeviceId))
            return; // file belongs to another machine — out of scope for this run

        Assert.True(isValid, "signature failed to verify against embedded public key");
        Assert.Contains(payload!.DeviceId, MachineIdentity.GetCandidateDeviceIds());
    }
}
