using FluentAssertions;
using PortfolioApi.Services;

namespace PortfolioApi.Tests.Services;

public class LoginThrottleTests
{
    private readonly LoginThrottle _sut = new();

    [Fact]
    public void Fresh_username_is_not_locked()
    {
        var act = () => _sut.EnsureNotLocked("alice");

        act.Should().NotThrow();
    }

    [Fact]
    public void Fifth_failure_inside_the_window_locks_the_username()
    {
        for (int i = 0; i < 5; i++) _sut.RecordFailure("alice");

        var act = () => _sut.EnsureNotLocked("alice");

        act.Should().Throw<AuthFailedException>();
    }

    [Fact]
    public void Lockout_reads_the_same_for_a_locked_existing_account_and_a_locked_unknown_username()
    {
        // Hammering an unknown username locks it — that's deliberate; it
        // means a probing attacker can't fast-cycle one name and the
        // response shape doesn't disclose whether the name exists.
        for (int i = 0; i < 5; i++) _sut.RecordFailure("ghost-account-xyz");

        var act = () => _sut.EnsureNotLocked("ghost-account-xyz");

        act.Should().Throw<AuthFailedException>();
    }

    [Fact]
    public void Clear_releases_the_lockout_immediately()
    {
        for (int i = 0; i < 5; i++) _sut.RecordFailure("alice");
        _sut.Clear("alice");

        var act = () => _sut.EnsureNotLocked("alice");

        act.Should().NotThrow();
    }

    [Fact]
    public void Username_comparison_is_case_insensitive()
    {
        for (int i = 0; i < 5; i++) _sut.RecordFailure("Alice");

        var act = () => _sut.EnsureNotLocked("ALICE");

        act.Should().Throw<AuthFailedException>("counter is keyed by lowercased username");
    }

    [Fact]
    public void Different_usernames_are_isolated()
    {
        for (int i = 0; i < 5; i++) _sut.RecordFailure("alice");

        var act = () => _sut.EnsureNotLocked("bob");

        act.Should().NotThrow("a lockout on alice must not splash onto bob");
    }
}
