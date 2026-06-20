using System.Collections.Generic;
using Stranichnik.Sync;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SyncActivityServiceTests
{
    [Fact]
    public void BeginOperation_reports_activity_only_on_outer_scope_boundaries()
    {
        var service = new SyncActivityService();
        var states = new List<bool>();
        service.ActivityChanged += (_, e) => states.Add(e.IsActive);

        using (service.BeginOperation())
        {
            Assert.True(service.IsActive);
            using (service.BeginOperation())
            {
                Assert.True(service.IsActive);
            }

            Assert.True(service.IsActive);
        }

        Assert.False(service.IsActive);
        Assert.Collection(
            states,
            state => Assert.True(state),
            state => Assert.False(state));
    }

    [Fact]
    public void Operation_scope_is_idempotent()
    {
        var service = new SyncActivityService();
        var scope = service.BeginOperation();

        scope.Dispose();
        scope.Dispose();

        Assert.False(service.IsActive);
    }

    [Fact]
    public void ReportCompleted_reports_completion_result()
    {
        var service = new SyncActivityService();
        var results = new List<bool>();
        service.ActivityCompleted += (_, e) => results.Add(e.Succeeded);

        service.ReportCompleted(succeeded: true);
        service.ReportCompleted(succeeded: false);

        Assert.Collection(
            results,
            result => Assert.True(result),
            result => Assert.False(result));
    }
}
