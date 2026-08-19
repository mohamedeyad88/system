using Apex.Services;

namespace Apex.Services.Tests.Printing;

/// <summary>
/// The shop's rule is that a printer needing a person holds its job. That makes
/// the line between "fault" and "warning" load-bearing: put it in the wrong
/// place and a station either stops for no reason or keeps being fed work it
/// cannot do.
/// </summary>
public class PrinterConditionTests
{
    private static PrinterStatusEventArgs Status(PrinterCondition c) =>
        new("Test", c, c.ToString(), isOffline: c == PrinterCondition.Offline,
            hasError: false, queueLength: 0);

    [Theory]
    [InlineData(PrinterCondition.OutOfPaper)]
    [InlineData(PrinterCondition.OutOfToner)]
    [InlineData(PrinterCondition.DoorOpen)]
    [InlineData(PrinterCondition.PaperJam)]
    [InlineData(PrinterCondition.OutputBinFull)]
    [InlineData(PrinterCondition.PaperProblem)]
    [InlineData(PrinterCondition.NeedsAttention)]
    [InlineData(PrinterCondition.Offline)]
    public void AFaultHoldsTheJob(PrinterCondition condition)
    {
        Assert.True(Status(condition).RequiresIntervention);
    }

    /// <summary>
    /// A tray with sheets left still prints. WMI reports Low Paper as state 3,
    /// which the old mapping swept into the same bucket as an empty tray.
    /// </summary>
    [Theory]
    [InlineData(PrinterCondition.Ready)]
    [InlineData(PrinterCondition.LowPaper)]
    [InlineData(PrinterCondition.LowToner)]
    public void AWarningDoesNotHoldTheJob(PrinterCondition condition)
    {
        Assert.False(Status(condition).RequiresIntervention);
    }

    /// <summary>
    /// Every condition has to fall on one side of the line — a new value added
    /// without a decision would silently default to "keep printing".
    /// </summary>
    [Fact]
    public void EveryConditionIsClassified()
    {
        foreach (PrinterCondition c in Enum.GetValues<PrinterCondition>())
        {
            bool isWarning = c is PrinterCondition.Ready
                              or PrinterCondition.LowPaper
                              or PrinterCondition.LowToner;

            Assert.True(Status(c).RequiresIntervention != isWarning,
                $"{c} is neither classified as a fault nor as a warning");
        }
    }
}
