using System;
using Apex.Services;
using Xunit;

namespace Apex.Services.Tests.Printing;

/// <summary>
/// Queue-drain stall detection: the device-agnostic backstop for a printer that a cheap
/// driver never reports as Offline. A queue is "stuck" only when it is a real pile-up
/// that has not drained for a sustained window — never a station that is merely slow on
/// a single big job.
/// </summary>
public class PrinterStallTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PileUp_NotDrainingForLong_IsFlaggedStuck()
    {
        // 5 jobs, last drained 10 minutes ago → well past the threshold.
        var stalled = PrinterMonitoringService.EvaluateStall(
            queueLength: 5, prevLen: 5, lastDrainUtc: T0.AddMinutes(-10), nowUtc: T0, out _);
        Assert.True(stalled);
    }

    [Fact]
    public void PileUp_ButRecentlyDraining_IsNotStuck()
    {
        // A big queue that drained a second ago is healthy, not stuck.
        var stalled = PrinterMonitoringService.EvaluateStall(
            queueLength: 20, prevLen: 20, lastDrainUtc: T0.AddSeconds(-1), nowUtc: T0, out _);
        Assert.False(stalled);
    }

    [Fact]
    public void ShrinkingQueue_ResetsTheClock_AndIsNotStuck()
    {
        // Queue went 8 → 5: it is draining, so it must be healthy AND reset the clock,
        // even though the previous drain timestamp was ancient.
        var stalled = PrinterMonitoringService.EvaluateStall(
            queueLength: 5, prevLen: 8, lastDrainUtc: T0.AddMinutes(-30), nowUtc: T0, out var newDrain);
        Assert.False(stalled);
        Assert.Equal(T0, newDrain);
    }

    [Fact]
    public void SingleSlowJob_IsNeverStuck()
    {
        // One long job sitting for an hour is slow, not stuck: below the pile-up floor,
        // so we must never hold the station and stop feeding it.
        var stalled = PrinterMonitoringService.EvaluateStall(
            queueLength: 1, prevLen: 1, lastDrainUtc: T0.AddHours(-1), nowUtc: T0, out _);
        Assert.False(stalled);
    }

    [Fact]
    public void EmptyQueue_IsHealthy_AndResetsClock()
    {
        var stalled = PrinterMonitoringService.EvaluateStall(
            queueLength: 0, prevLen: 3, lastDrainUtc: T0.AddMinutes(-30), nowUtc: T0, out var newDrain);
        Assert.False(stalled);
        Assert.Equal(T0, newDrain);
    }
}
