using Apex.UI.ViewModels;
using Xunit;

namespace Apex.UI.Tests;

/// <summary>
/// Reported from the floor: printing "finished", the program was closed, and the rest
/// of the order never reached the printer. The batch runs inside the process, so the
/// copies it still owed died with it — silently. Closing now asks first.
/// </summary>
public class ShutdownDuringPrintTests
{
    [Fact]
    public void Idle_ClosesWithoutAsking() =>
        Assert.False(MainViewModel.ShutdownPromptFor(runInFlight: false, outstandingCopies: 0).Ask);

    [Fact]
    public void MidRun_AsksAndSaysHowManyCopiesWouldBeLost()
    {
        var prompt = MainViewModel.ShutdownPromptFor(runInFlight: true, outstandingCopies: 27);

        Assert.True(prompt.Ask);
        Assert.Equal(27, prompt.OutstandingCopies);
    }

    [Fact]
    public void RunFinishedButSheetsStillComingOut_DoesNotAsk()
    {
        // Everything owed was handed to the spooler: those pages print whether Apex
        // is open or not, so holding the operator in the app would be a lie.
        var prompt = MainViewModel.ShutdownPromptFor(runInFlight: true, outstandingCopies: 0);

        Assert.False(prompt.Ask);
    }

    [Fact]
    public void AStaleRunningFlagWithNothingOwed_DoesNotBlockClosing() =>
        Assert.False(MainViewModel.ShutdownPromptFor(runInFlight: true, outstandingCopies: 0).Ask);
}
