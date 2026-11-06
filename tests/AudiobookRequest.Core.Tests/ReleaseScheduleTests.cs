using AudiobookRequest.Core.Library;

namespace AudiobookRequest.Core.Tests;

public class ReleaseScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsUnreleased_OnlyForADateStillAhead()
    {
        Assert.True(ReleaseSchedule.IsUnreleased(Now.AddDays(1), Now));
        Assert.False(ReleaseSchedule.IsUnreleased(Now, Now));
        Assert.False(ReleaseSchedule.IsUnreleased(Now.AddDays(-30), Now));
        Assert.False(ReleaseSchedule.IsUnreleased(null, Now));
    }

    [Theory]
    [InlineData(200, 7 * 24)]
    [InlineData(61, 7 * 24)]
    [InlineData(60, 3 * 24)]
    [InlineData(15, 3 * 24)]
    [InlineData(14, 24)]
    [InlineData(4, 24)]
    [InlineData(3, 6)]
    [InlineData(0.2, 6)]
    public void UpcomingInterval_GetsShorterTheCloserTheRelease(double daysAway, double expectedHours)
    {
        var interval = ReleaseSchedule.UpcomingInterval(Now.AddDays(daysAway), Now);

        Assert.Equal(expectedHours, interval.TotalHours, 3);
    }

    [Fact]
    public void NextUpcomingCheck_IsImmediateTheFirstTimeThenOneIntervalAfterTheLastCheck()
    {
        var requestedAt = Now.AddHours(-1);
        var release = Now.AddDays(10); // 1 day interval

        Assert.Equal(requestedAt, ReleaseSchedule.NextUpcomingCheck(null, requestedAt, release, Now));
        Assert.Equal(Now.AddHours(-2).AddDays(1), ReleaseSchedule.NextUpcomingCheck(Now.AddHours(-2), requestedAt, release, Now));
    }

    [Theory]
    [InlineData(0, 0.5)]    // release day
    [InlineData(1.5, 0.5)]  // still the first two days
    [InlineData(2, 2)]      // up to a week
    [InlineData(6.9, 2)]
    [InlineData(7, 6)]      // up to a month
    [InlineData(29, 6)]
    [InlineData(30, 12)]    // back to the configured pace
    [InlineData(400, 12)]
    public void RetryInterval_IsFastJustAfterReleaseAndEasesBackToTheWishlistPace(double daysSinceRelease, double expectedHours)
    {
        var interval = ReleaseSchedule.RetryInterval(Now.AddDays(-daysSinceRelease), Now, wishlistRetryHours: 12);

        Assert.Equal(expectedHours, interval.TotalHours, 3);
    }

    [Fact]
    public void RetryInterval_NeverSlowerThanTheConfiguredPace()
    {
        // A 1-hour wishlist pace beats the 2-hour and 6-hour steps; the 30-minute step still wins on release day.
        Assert.Equal(1, ReleaseSchedule.RetryInterval(Now.AddDays(-3), Now, 1).TotalHours);
        Assert.Equal(1, ReleaseSchedule.RetryInterval(Now.AddDays(-20), Now, 1).TotalHours);
        Assert.Equal(0.5, ReleaseSchedule.RetryInterval(Now.AddHours(-1), Now, 1).TotalHours);
    }

    [Fact]
    public void RetryInterval_IsTheNormalPaceWhenThereIsNoDateOrItIsStillAhead()
    {
        Assert.Equal(12, ReleaseSchedule.RetryInterval(null, Now, 12).TotalHours);
        Assert.Equal(12, ReleaseSchedule.RetryInterval(Now.AddDays(5), Now, 12).TotalHours);
        Assert.Equal(1, ReleaseSchedule.RetryInterval(null, Now, 0).TotalHours);
    }
}
