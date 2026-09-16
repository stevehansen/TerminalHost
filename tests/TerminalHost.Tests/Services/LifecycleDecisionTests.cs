using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

public class LifecycleDecisionTests
{
    [Theory]
    [InlineData(SessionLifecycle.Completed)]
    [InlineData(SessionLifecycle.Failed)]
    [InlineData(SessionLifecycle.TimedOut)]
    public void ClassifyArrival_TerminalState_RevivesToActive(SessionLifecycle current)
    {
        var verdict = LifecycleDecision.ClassifyArrival(current);

        verdict.Revive.ShouldBeTrue();
        verdict.NewLifecycle.ShouldBe(SessionLifecycle.Active);
    }

    [Theory]
    [InlineData(SessionLifecycle.Active)]
    [InlineData(SessionLifecycle.WaitingPermission)]
    public void ClassifyArrival_NonTerminalState_NoOp(SessionLifecycle current)
    {
        var verdict = LifecycleDecision.ClassifyArrival(current);

        verdict.Revive.ShouldBeFalse();
        verdict.NewLifecycle.ShouldBeNull();
    }
}
