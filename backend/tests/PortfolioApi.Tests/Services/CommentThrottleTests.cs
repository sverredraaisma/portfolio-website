using FluentAssertions;
using PortfolioApi.Services;

namespace PortfolioApi.Tests.Services;

public class CommentThrottleTests
{
    private readonly CommentThrottle _sut = new();

    [Fact]
    public void Fresh_user_can_comment()
    {
        var act = () => _sut.EnsureCanComment(Guid.NewGuid());

        act.Should().NotThrow();
    }

    [Fact]
    public void Five_records_in_the_window_are_fine_the_sixth_is_blocked()
    {
        var u = Guid.NewGuid();
        for (int i = 0; i < 5; i++) _sut.Record(u);

        var act = () => _sut.EnsureCanComment(u);

        act.Should().Throw<AuthFailedException>();
    }

    [Fact]
    public void Different_users_are_isolated()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        for (int i = 0; i < 5; i++) _sut.Record(alice);

        var act = () => _sut.EnsureCanComment(bob);

        act.Should().NotThrow("a cap on alice must not splash onto bob");
    }

    [Fact]
    public void EnsureCanComment_does_not_throw_for_a_user_who_has_only_partially_filled_the_window()
    {
        var u = Guid.NewGuid();
        for (int i = 0; i < 4; i++) _sut.Record(u);

        var act = () => _sut.EnsureCanComment(u);

        act.Should().NotThrow();
    }

    [Fact]
    public void Record_is_safe_to_call_under_concurrent_writers()
    {
        // Concurrency check: the implementation locks per-user, but the
        // outer dictionary is concurrent. Spray writes from many threads
        // and confirm no exception escapes and the cap is still enforced.
        var u = Guid.NewGuid();
        Parallel.For(0, 100, _ => _sut.Record(u));

        var act = () => _sut.EnsureCanComment(u);

        act.Should().Throw<AuthFailedException>();
    }
}
