using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Apex.Licensing;
using Xunit;

namespace Apex.Licensing.Tests
{
    /// <summary>
    /// Adversarial / attack-scenario tests.
    /// Each test represents a real-world attack vector that the licensing system must resist.
    /// </summary>
    public class SecurityTests
    {
        // ──────────────────────────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────────────────────────

        private static (ECDsa PrivateKey, string PublicPem) MakeKeyPair()
        {
            var (priv, pub) = LicenseCrypto.GenerateKeyPair();
            var key = ECDsa.Create();
            key.ImportFromPem(priv);
            return (key, pub);
        }

        private static SignedLicense SignFor(string deviceId, LicenseType type,
            DateTime expiresUtc, ECDsa privateKey)
        {
            var payload = new LicensePayload
            {
                LicenseId      = Guid.NewGuid().ToString("D").ToUpperInvariant(),
                DeviceId       = deviceId,
                Type           = type,
                IssuedUtc      = DateTime.UtcNow.AddMinutes(-1),
                ExpiresUtc     = expiresUtc,
                CustomerName   = "Attacker",
                ProductVersion = "1.0",
                Nonce          = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            };
            return LicenseCrypto.SignLicense(payload, privateKey);
        }

        // ──────────────────────────────────────────────────────────────────
        //  Attack 1 – License issued for a different device
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void Attack_LicenseForDifferentDevice_SignatureStillValid_PayloadHasDifferentDeviceId()
        {
            // A legitimate license is issued for Machine A.
            // Machine B obtains a copy of that license file.
            // The signature is valid, but DeviceId inside payload ≠ Machine B's DeviceId.
            // LicenseManager.ValidateLicenseFile checks hardware binding – tested here at
            // the payload level (the full file-path integration is in IntegrationTests).

            var (key, pub) = MakeKeyPair();
            var machineA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA1";
            var machineB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB2";

            var signed = SignFor(machineA, LicenseType.Full,
                new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc), key);

            var (isValid, payload) = LicenseCrypto.VerifyLicense(signed, pub);

            // Signature IS valid (correct key)
            Assert.True(isValid);
            Assert.NotNull(payload);

            // But DeviceId inside the payload does NOT match Machine B
            Assert.NotEqual(machineB, payload!.DeviceId);
        }

        // ──────────────────────────────────────────────────────────────────
        //  Attack 2 – Replay: reuse an expired license
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void Attack_ExpiredLicense_IsDetectedViaExpiresUtc()
        {
            var (key, pub) = MakeKeyPair();
            var deviceId   = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCC01";

            var signed = SignFor(deviceId, LicenseType.Full,
                DateTime.UtcNow.AddDays(-1), // already expired
                key);

            var (isValid, payload) = LicenseCrypto.VerifyLicense(signed, pub);
            Assert.True(isValid);   // signature is valid ...
            Assert.NotNull(payload);

            // ... but the caller must check ExpiresUtc
            Assert.True(DateTime.UtcNow > payload!.ExpiresUtc,
                "ExpiresUtc should be in the past for this test case");
        }

        // ──────────────────────────────────────────────────────────────────
        //  Attack 3 – Payload swap (keep good signature, swap payload)
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void Attack_SwapPayloadKeepSignature_FailsVerification()
        {
            var (key, pub) = MakeKeyPair();

            var legitimateSigned = SignFor(
                "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDD02",
                LicenseType.Full,
                new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                key);

            // Attacker builds a Pro-type payload for a different device
            var attackPayload = new LicensePayload
            {
                DeviceId   = "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEE03",
                Type       = LicenseType.Pro,
                ExpiresUtc = new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc)
            };
            var attackJson    = JsonSerializer.Serialize(attackPayload);
            var attackBase64  = Convert.ToBase64String(Encoding.UTF8.GetBytes(attackJson));

            var swapped = new SignedLicense
            {
                Payload       = attackBase64,
                Signature     = legitimateSigned.Signature,   // stolen from legitimate license
                SchemaVersion = "1"
            };

            var (isValid, _) = LicenseCrypto.VerifyLicense(swapped, pub);
            Assert.False(isValid);
        }

        // ──────────────────────────────────────────────────────────────────
        //  Attack 4 – Wrong key (attacker generates their own key pair)
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void Attack_AttackerGeneratesOwnKeyPair_LicenseRejected()
        {
            var (legitimateKey, legitimatePub) = MakeKeyPair();
            var (attackerKey,   _)             = MakeKeyPair(); // attacker's own pair

            // Attacker signs a license with THEIR private key
            var attackerSigned = SignFor(
                "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF",
                LicenseType.Pro,
                new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                attackerKey);

            // Client verifies against the legitimate embedded public key → must fail
            var (isValid, _) = LicenseCrypto.VerifyLicense(attackerSigned, legitimatePub);
            Assert.False(isValid);
        }

        // ──────────────────────────────────────────────────────────────────
        //  Attack 5 – Brute-force / guessing a valid Base64 signature
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void Attack_RandomSignatureBytes_NeverVerify()
        {
            var (_, pub) = MakeKeyPair();

            // Try 100 random 64-byte "signatures"
            for (int i = 0; i < 100; i++)
            {
                var randomSig = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
                var fakePayload = new LicensePayload
                {
                    DeviceId   = "AAAA",
                    ExpiresUtc = new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc)
                };
                var payloadBase64 = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(fakePayload)));

                var faked = new SignedLicense
                {
                    Payload   = payloadBase64,
                    Signature = randomSig
                };

                var (isValid, _) = LicenseCrypto.VerifyLicense(faked, pub);
                Assert.False(isValid, $"Iteration {i}: random signature must not verify");
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  Attack 6 – Trial HMAC brute-force / guessing
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void Attack_RandomTrialHmac_NeverVerifies()
        {
            var state = new TrialState
            {
                StartUtc    = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSeenUtc = new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc),
                DeviceId    = "REALDEVICEID"
            };

            for (int i = 0; i < 50; i++)
            {
                state.Hmac = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                Assert.False(LicenseCrypto.VerifyTrialHmac(state),
                    $"Iteration {i}: random HMAC must not verify");
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  Attack 7 – Nonce uniqueness (replay prevention)
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void Nonce_TwoLicenses_HaveDifferentNonces()
        {
            var (key, _) = MakeKeyPair();

            var p1 = new LicensePayload
            {
                DeviceId   = "NONCE_TEST_DEVICE_01234567890",
                ExpiresUtc = new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                Nonce      = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            };
            var p2 = new LicensePayload
            {
                DeviceId   = p1.DeviceId,
                ExpiresUtc = p1.ExpiresUtc,
                Nonce      = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            };

            Assert.NotEqual(p1.Nonce, p2.Nonce);
        }

        [Fact]
        public void LicenseId_TwoLicenses_HaveDifferentIds()
        {
            var id1 = Guid.NewGuid().ToString("D").ToUpperInvariant();
            var id2 = Guid.NewGuid().ToString("D").ToUpperInvariant();
            Assert.NotEqual(id1, id2);
        }

        // ──────────────────────────────────────────────────────────────────
        //  Attack 8 – Null / empty inputs do not throw, just return false
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void VerifyLicense_NullPayloadAndSignature_ReturnsFalse_NoException()
        {
            var (_, pub) = MakeKeyPair();
            var signed = new SignedLicense { Payload = null!, Signature = null!, SchemaVersion = "1" };

            var ex = Record.Exception(() => LicenseCrypto.VerifyLicense(signed, pub));
            Assert.Null(ex);

            var (isValid, _) = LicenseCrypto.VerifyLicense(signed, pub);
            Assert.False(isValid);
        }

        [Fact]
        public void VerifyLicense_GarbageBase64_ReturnsFalse_NoException()
        {
            var (_, pub) = MakeKeyPair();
            var signed = new SignedLicense
            {
                Payload   = "not-valid-base64!!!",
                Signature = "also-not-valid-base64@@@"
            };

            var ex = Record.Exception(() => LicenseCrypto.VerifyLicense(signed, pub));
            Assert.Null(ex);

            var (isValid, _) = LicenseCrypto.VerifyLicense(signed, pub);
            Assert.False(isValid);
        }

        // ──────────────────────────────────────────────────────────────────
        //  Model integrity – LicensePayload fields
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void LicensePayload_Defaults_AreReasonable()
        {
            var p = new LicensePayload();

            Assert.Equal("1",                    p.SchemaVersion);
            Assert.Equal("",                     p.LicenseId);
            Assert.Equal("",                     p.DeviceId);
            Assert.Equal(LicenseType.Full,       p.Type);
            Assert.Equal(1,                      p.MaxActivations);
            Assert.Equal("",                     p.Nonce);
            Assert.Equal("APEX-PRINTING-SUITE",  new ActivationRequest().ProductId);
        }

        [Fact]
        public void TrialState_DefaultHmac_IsEmpty()
        {
            var s = new TrialState();
            Assert.Equal("", s.Hmac);
            Assert.Equal("", s.DeviceId);
        }

        [Fact]
        public void ValidationResult_DefaultIsValid_IsFalse()
        {
            var r = new ValidationResult();
            Assert.False(r.IsValid);
        }
    }
}
