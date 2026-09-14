using System.Collections.Generic;
using Apex.Licensing;
using Xunit;

namespace Apex.Licensing.Tests
{
    /// <summary>
    /// The device-id formula against described hardware, so the disk choice is tested
    /// without depending on the test machine's disks.
    /// </summary>
    public class DeviceIdFormulaTests
    {
        private const string Board = "11111111-2222-3333-4444-555555555555";
        private const string Cpu = "BFEBFBFF000906EA";

        // Enumerated the way the owner's PC really is: a data HDD listed before the boot SSD.
        private static HardwareFacts DataDiskListedFirst(string? boot = @"\\.\PHYSICALDRIVE0") => new(Board, Cpu,
            new List<DiskSerial>
            {
                new(@"\\.\PHYSICALDRIVE2", "DATA-HDD-2", false),
                new(@"\\.\PHYSICALDRIVE0", "BOOT-SSD-0", false),
                new(@"\\.\PHYSICALDRIVE1", "DATA-HDD-1", false),
                new(@"\\.\CDROM0", "", false),
            }, boot);

        [Fact]
        public void UsesTheBootDisk_NotWhicheverDiskWindowsListsFirst()
        {
            Assert.Equal(MachineIdentity.ComputeId(Board, Cpu, "BOOT-SSD-0"),
                MachineIdentity.CanonicalId(DataDiskListedFirst()));
        }

        [Fact]
        public void LegacyId_IsWhatEveryEarlierInstallProduced()
        {
            Assert.Equal(MachineIdentity.ComputeId(Board, Cpu, "DATA-HDD-2"),
                MachineIdentity.LegacyId(DataDiskListedFirst()));
        }

        [Fact]
        public void ALicenseIssuedUnderTheOldFormula_IsStillAccepted()
        {
            var candidates = MachineIdentity.CandidateIds(DataDiskListedFirst());

            Assert.Equal(MachineIdentity.CanonicalId(DataDiskListedFirst()), candidates[0]);
            Assert.Contains(MachineIdentity.LegacyId(DataDiskListedFirst()), candidates);
        }

        [Fact]
        public void RemovingTheOldFirstListedDisk_DoesNotChangeTheId()
        {
            var before = DataDiskListedFirst();
            var after = before with { Disks = new List<DiskSerial>
            {
                new(@"\\.\PHYSICALDRIVE0", "BOOT-SSD-0", false),
                new(@"\\.\PHYSICALDRIVE1", "DATA-HDD-1", false),
            } };

            Assert.Equal(MachineIdentity.CanonicalId(before), MachineIdentity.CanonicalId(after));
        }

        [Fact]
        public void AUsbStickListedFirst_IsNeverTheIdentity()
        {
            var facts = DataDiskListedFirst(boot: null) with { Disks = new List<DiskSerial>
            {
                new(@"\\.\PHYSICALDRIVE3", "USB-STICK", true),
                new(@"\\.\PHYSICALDRIVE1", "DATA-HDD-1", false),
                new(@"\\.\PHYSICALDRIVE0", "BOOT-SSD-0", false),
            } };

            // No boot disk resolved: the lowest-numbered internal disk, not the stick.
            Assert.Equal(MachineIdentity.ComputeId(Board, Cpu, "BOOT-SSD-0"), MachineIdentity.CanonicalId(facts));
            Assert.NotEqual(MachineIdentity.ComputeId(Board, Cpu, "USB-STICK"), MachineIdentity.CanonicalId(facts));
        }

        [Fact]
        public void ADifferentMotherboard_MatchesNoCandidate()
        {
            var otherBoard = DataDiskListedFirst() with { BoardUuid = "99999999-0000-0000-0000-000000000000" };

            foreach (var id in MachineIdentity.CandidateIds(otherBoard))
                Assert.DoesNotContain(id, MachineIdentity.CandidateIds(DataDiskListedFirst()));
        }

        [Fact]
        public void NoHardwareReadable_IsUnknown() =>
            Assert.Equal("UNKNOWN", MachineIdentity.CanonicalId(new HardwareFacts("", "", new List<DiskSerial>(), null)));

        [Fact]
        public void TheFormulaItselfIsUnchanged()
        {
            // SHA-256("a|b|c") → first 32 hex chars, upper case — what every issued licence was signed against.
            Assert.Equal("A52DD81BFD5E4E66D96B9F598382F6CB", MachineIdentity.ComputeId("a", "b", "c"));
        }
    }
}
