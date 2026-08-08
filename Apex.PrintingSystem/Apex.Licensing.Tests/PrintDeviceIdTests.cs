using System.IO;
using Xunit;

namespace Apex.Licensing.Tests;

/// <summary>Utility: writes THIS machine's device id so a local license can be signed for it.</summary>
public class PrintDeviceIdTests
{
    [Fact]
    public void WriteDeviceId()
    {
        var info = MachineIdentity.GetDeviceInfo();
        Directory.CreateDirectory(@"D:\Apex\publish");
        File.WriteAllText(@"D:\Apex\publish\deviceid.txt",
            $"RAW={info.DeviceId}\nDISPLAY={info.DisplayId}\n");
        Assert.False(string.IsNullOrWhiteSpace(info.DeviceId));
    }
}
