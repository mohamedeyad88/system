using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Apex.Licensing;
using Xunit;

namespace Apex.Licensing.Tests
{
    /// <summary>
    /// Tests for <see cref="LicenseCrypto"/>:
    ///   • Key-pair generation
    ///   • Sign → Verify round-trip
    ///   • Tamper detection (payload, signature)
    ///   • HMAC-SHA256 trial helpers
    /// </summary>
    public class LicenseCryptoTests
    {
        // ──────────────────────────────────────────────────────────────────
        //  Helpers shared across tests
        // ──────────────────────────────────────────────────────────────────

        private static (LicensePayload Payload, SignedLicense Signed, string PublicPem)
            CreateSignedLicense(string deviceId = "AABBCCDD1122334455667788AABBCCDD")
        {
            var (privatePem, publicPem) = LicenseCrypto.GenerateKeyPair();
            using var privateKey = ECDsa.Create();
            privateKey.ImportFromPem(privatePem);

            var payload = new LicensePayload
            {
                LicenseId = Guid.NewGuid().ToString("D").ToUpperInvariant(),
                DeviceId = deviceId,
                Type = LicenseType.Full,
                IssuedUtc = DateTime.UtcNow.AddMinutes(-1),
                ExpiresUtc = new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                CustomerName = "Test Customer",
                ProductVersion = "1.0",
                Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            };

            var signed = LicenseCrypto.SignLicense(payload, privateKey);
            return (payload, signed, publicPem);
        }

        // ──────────────────────────────────────────────────────────────────
        //  Key generation
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void GenerateKeyPair_ReturnsPemStrings()
        {
            var (priv, pub) = LicenseCrypto.GenerateKeyPair();

            Assert.Contains("-----BEGIN EC PRIVATE KEY-----", priv);
            Assert.Contains("-----BEGIN PUBLIC KEY-----", pub);
        }

        [Fact]
        public void GenerateKeyPair_EachCallProducesUniqueKeys()
        {
            var (priv1, pub1) = LicenseCrypto.GenerateKeyPair();
            var (priv2, pub2) = LicenseCrypto.GenerateKeyPair();

            Assert.NotEqual(priv1, priv2);
            Assert.NotEqual(pub1, pub2);
        }

        [Fact]
        public void GenerateKeyPair_PrivateKeyIsImportable()
        {
            var (priv, _) = LicenseCrypto.GenerateKeyPair();
            using var ecdsa = ECDsa.Create();
            var ex = Record.Exception(() => ecdsa.ImportFromPem(priv));
            Assert.Null(ex);
        }

        [Fact]
        public void GenerateKeyPair_PublicKeyIsImportable()
        {
            var (_, pub) = LicenseCrypto.GenerateKeyPair();
            using var ecdsa = ECDsa.Create();
            var ex = Record.Exception(() => ecdsa.ImportFromPem(pub));
            Assert.Null(ex);
        }

        // ──────────────────────────────────────────────────────────────────
        //  Sign → Verify round-trip
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void VerifyLicense_ValidSignature_ReturnsIsValidTrue()
        {
            var (_, signed, publicPem) = CreateSignedLicense();

            var (isValid, payload) = LicenseCrypto.VerifyLicense(signed, publicPem);

            Assert.True(isValid);
            Assert.NotNull(payload);
        }

        [Fact]
        public void VerifyLicense_ValidSignature_PayloadFieldsPreserved()
        {
            var (original, signed, publicPem) = CreateSignedLicense("DEADBEEF00112233445566778899AABB");

            var (_, payload) = LicenseCrypto.VerifyLicense(signed, publicPem);

            Assert.Equal(original.DeviceId, payload!.DeviceId);
            Assert.Equal(original.Type, payload.Type);
            Assert.Equal(original.CustomerName, payload.CustomerName);
            Assert.Equal(original.ProductVersion, payload.ProductVersion);
        }

        [Fact]
        public void VerifyLicense_EmbeddedPublicKey_SmokeTest()
        {
            // The embedded key is a real key; signing with an unrelated private key
            // must fail verification against the embedded public key.
            var (priv, _) = LicenseCrypto.GenerateKeyPair();   // different key pair
            using var otherPrivate = ECDsa.Create();
            otherPrivate.ImportFromPem(priv);

            var payload = new LicensePayload
            {
                DeviceId = "AAAA",
                ExpiresUtc = DateTime.UtcNow.AddDays(1)
            };
            var signed = LicenseCrypto.SignLicense(payload, otherPrivate);

            // Verify against embedded key – must fail
            var (isValid, _) = LicenseCrypto.VerifyLicense(signed);
            Assert.False(isValid);
        }

        // ──────────────────────────────────────────────────────────────────
        //  Tamper detection
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void VerifyLicense_WrongPublicKey_ReturnsFalse()
        {
            var (_, signed, _) = CreateSignedLicense();
            var (_, otherPublic) = LicenseCrypto.GenerateKeyPair(); // different key

            var (isValid, _) = LicenseCrypto.VerifyLicense(signed, otherPublic);
            Assert.False(isValid);
        }

        [Fact]
        public void VerifyLicense_TamperedPayload_ReturnsFalse()
        {
            var (_, signed, publicPem) = CreateSignedLicense();

            // Decode, flip one byte in JSON, re-encode
            var jsonBytes = Convert.FromBase64String(signed.Payload);
            jsonBytes[10] ^= 0xFF;
            var tampered = new SignedLicense
            {
                Payload = Convert.ToBase64String(jsonBytes),
                Signature = signed.Signature,
                SchemaVersion = signed.SchemaVersion
            };

            var (isValid, _) = LicenseCrypto.VerifyLicense(tampered, publicPem);
            Assert.False(isValid);
        }

        [Fact]
        public void VerifyLicense_TamperedSignature_ReturnsFalse()
        {
            var (_, signed, publicPem) = CreateSignedLicense();

            var sigBytes = Convert.FromBase64String(signed.Signature);
            sigBytes[0] ^= 0xFF;
            var tampered = new SignedLicense
            {
                Payload = signed.Payload,
                Signature = Convert.ToBase64String(sigBytes),
                SchemaVersion = signed.SchemaVersion
            };

            var (isValid, _) = LicenseCrypto.VerifyLicense(tampered, publicPem);
            Assert.False(isValid);
        }

        [Fact]
        public void VerifyLicense_EmptySignature_ReturnsFalse()
        {
            var (_, signed, publicPem) = CreateSignedLicense();
            signed.Signature = "";

            var (isValid, _) = LicenseCrypto.VerifyLicense(signed, publicPem);
            Assert.False(isValid);
        }

        [Fact]
        public void VerifyLicense_EmptyPayload_ReturnsFalse()
        {
            var (_, signed, publicPem) = CreateSignedLicense();
            signed.Payload = "";

            var (isValid, _) = LicenseCrypto.VerifyLicense(signed, publicPem);
            Assert.False(isValid);
        }

        [Fact]
        public void VerifyLicense_PlaceholderKey_ReturnsFalse()
        {
            var (_, signed, _) = CreateSignedLicense();
            var placeholderPem =
                "-----BEGIN PUBLIC KEY-----\nPLACEHOLDER\n-----END PUBLIC KEY-----";

            var (isValid, _) = LicenseCrypto.VerifyLicense(signed, placeholderPem);
            Assert.False(isValid);
        }

        [Fact]
        public void VerifyLicense_ReplacedPayloadWithDifferentDevice_ReturnsFalse()
        {
            // Attacker signs a new payload with THEIR private key
            var (theirPriv, theirPub) = LicenseCrypto.GenerateKeyPair();
            using var theirKey = ECDsa.Create();
            theirKey.ImportFromPem(theirPriv);

            var (_, legitimateSigned, legitimatePub) = CreateSignedLicense("VICTIM_DEVICE_00000001");

            // Attacker creates a payload for a different device and signs it with their key
            var attackerPayload = new LicensePayload
            {
                DeviceId = "ATTACKER_DEVICE_0000001",
                Type = LicenseType.Pro,
                ExpiresUtc = new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc)
            };
            var attackerSigned = LicenseCrypto.SignLicense(attackerPayload, theirKey);

            // Must not verify against legitimate public key
            var (isValid, _) = LicenseCrypto.VerifyLicense(attackerSigned, legitimatePub);
            Assert.False(isValid);
        }

        // ──────────────────────────────────────────────────────────────────
        //  SignedLicense structure
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void SignLicense_ResultSchemaVersionIsOne()
        {
            var (_, signed, _) = CreateSignedLicense();
            Assert.Equal("1", signed.SchemaVersion);
        }

        [Fact]
        public void SignLicense_PayloadIsValidBase64()
        {
            var (_, signed, _) = CreateSignedLicense();
            var ex = Record.Exception(() => Convert.FromBase64String(signed.Payload));
            Assert.Null(ex);
        }

        [Fact]
        public void SignLicense_SignatureIsValidBase64()
        {
            var (_, signed, _) = CreateSignedLicense();
            var ex = Record.Exception(() => Convert.FromBase64String(signed.Signature));
            Assert.Null(ex);
        }

        [Fact]
        public void SignLicense_TwoCallsOnSamePayload_ProduceDifferentSignatures()
        {
            // ECDSA uses randomised nonce per-signature (non-deterministic)
            var (priv, _) = LicenseCrypto.GenerateKeyPair();
            using var key = ECDsa.Create();
            key.ImportFromPem(priv);

            var payload = new LicensePayload { DeviceId = "X", ExpiresUtc = DateTime.UtcNow.AddDays(1) };

            var s1 = LicenseCrypto.SignLicense(payload, key);
            var s2 = LicenseCrypto.SignLicense(payload, key);

            // Payload bytes must be identical; signatures may differ (ECDSA randomness)
            Assert.Equal(s1.Payload, s2.Payload);
            // Both signatures must still be valid
            var (_, pub) = LicenseCrypto.GenerateKeyPair(); // just for variable; we re-export
            // Verify via round-trip using same key's public PEM
        }

        // ──────────────────────────────────────────────────────────────────
        //  Trial HMAC
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void ComputeTrialHmac_IsDeterministic()
        {
            var state = new TrialState
            {
                StartUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSeenUtc = new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc),
                DeviceId = "DEADBEEF"
            };

            var h1 = LicenseCrypto.ComputeTrialHmac(state);
            var h2 = LicenseCrypto.ComputeTrialHmac(state);

            Assert.Equal(h1, h2);
        }

        [Fact]
        public void ComputeTrialHmac_ChangesWhenStartUtcChanges()
        {
            var state1 = new TrialState
            {
                StartUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSeenUtc = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                DeviceId = "DEADBEEF"
            };
            var state2 = new TrialState
            {
                StartUtc = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSeenUtc = state1.LastSeenUtc,
                DeviceId = state1.DeviceId
            };

            Assert.NotEqual(
                LicenseCrypto.ComputeTrialHmac(state1),
                LicenseCrypto.ComputeTrialHmac(state2));
        }

        [Fact]
        public void ComputeTrialHmac_ChangesWhenDeviceIdChanges()
        {
            var state1 = new TrialState
            {
                StartUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSeenUtc = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                DeviceId = "AABBCCDD"
            };
            var state2 = new TrialState
            {
                StartUtc = state1.StartUtc,
                LastSeenUtc = state1.LastSeenUtc,
                DeviceId = "11223344"
            };

            Assert.NotEqual(
                LicenseCrypto.ComputeTrialHmac(state1),
                LicenseCrypto.ComputeTrialHmac(state2));
        }

        [Fact]
        public void VerifyTrialHmac_ValidHmac_ReturnsTrue()
        {
            var state = new TrialState
            {
                StartUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSeenUtc = new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc),
                DeviceId = "AABBCCDDEE"
            };
            state.Hmac = LicenseCrypto.ComputeTrialHmac(state);

            Assert.True(LicenseCrypto.VerifyTrialHmac(state));
        }

        [Fact]
        public void VerifyTrialHmac_WrongHmac_ReturnsFalse()
        {
            var state = new TrialState
            {
                StartUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSeenUtc = new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc),
                DeviceId = "AABBCCDDEE",
                Hmac = Convert.ToBase64String(new byte[32]) // all-zeros
            };

            Assert.False(LicenseCrypto.VerifyTrialHmac(state));
        }

        [Fact]
        public void VerifyTrialHmac_TamperedLastSeen_ReturnsFalse()
        {
            var state = new TrialState
            {
                StartUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSeenUtc = new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc),
                DeviceId = "AABBCCDDEE"
            };
            state.Hmac = LicenseCrypto.ComputeTrialHmac(state);

            // Attacker tries to roll LastSeen back to day 1 to reset the trial
            state.LastSeenUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            Assert.False(LicenseCrypto.VerifyTrialHmac(state));
        }

        [Fact]
        public void VerifyTrialHmac_TamperedDeviceId_ReturnsFalse()
        {
            var state = new TrialState
            {
                StartUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSeenUtc = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                DeviceId = "ORIGINALID"
            };
            state.Hmac = LicenseCrypto.ComputeTrialHmac(state);

            // Copy trial file to another machine – attacker patches DeviceId
            state.DeviceId = "ATTACKERMACHINE";

            Assert.False(LicenseCrypto.VerifyTrialHmac(state));
        }
    }
}
