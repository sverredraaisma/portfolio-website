using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Constants;
using PortfolioApi.Models;
using PortfolioApi.Services;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.Services;

public class LocationServiceTests
{
    /// Sets up the SUT with a stubbed geocoder. Each test owns its TestDb
    /// so the unique-on-UserId index doesn't carry state across cases.
    private static (LocationService sut, TestDb test, FakeGeocoder geo, User user) Build()
    {
        var test = new TestDb();
        var user = new User { Username = "alice", Email = "a@x", PasswordHash = new byte[]{1}, PasswordSalt = new byte[]{1} };
        test.Db.Users.Add(user);
        test.Db.SaveChanges();
        test.Db.ChangeTracker.Clear();

        var geo = new FakeGeocoder();
        var audit = new AuditService(test.Db);
        var sut = new LocationService(test.Db, geo, audit);
        return (sut, test, geo, user);
    }

    [Fact]
    public async Task SetCoordsAsync_inserts_a_row_with_browser_source()
    {
        var (sut, test, _, user) = Build();

        await sut.SetCoordsAsync(user.Id, 52.379, 4.900, label: "home", precisionDecimals: null);

        var row = await test.Db.SharedLocations.SingleAsync();
        row.UserId.Should().Be(user.Id);
        row.Latitude.Should().Be(52.379);
        row.Longitude.Should().Be(4.900);
        row.Label.Should().Be("home");
        row.Source.Should().Be("browser");
        row.PrecisionDecimals.Should().Be(3, "no precision argument and no prior row → default 3");
        test.Dispose();
    }

    [Fact]
    public async Task SetCoordsAsync_updates_the_existing_row_so_one_user_only_has_one_pin()
    {
        var (sut, test, _, user) = Build();
        await sut.SetCoordsAsync(user.Id, 1.0, 1.0, null, null);

        await sut.SetCoordsAsync(user.Id, 2.0, 2.0, "moved", null);

        (await test.Db.SharedLocations.CountAsync()).Should().Be(1);
        var row = await test.Db.SharedLocations.SingleAsync();
        row.Latitude.Should().Be(2.0);
        row.Label.Should().Be("moved");
        test.Dispose();
    }

    [Theory]
    [InlineData(91.0, 0.0)]
    [InlineData(-91.0, 0.0)]
    [InlineData(0.0, 181.0)]
    [InlineData(0.0, -181.0)]
    [InlineData(double.NaN, 0.0)]
    public async Task SetCoordsAsync_rejects_out_of_range_or_NaN_coords(double lat, double lon)
    {
        var (sut, test, _, user) = Build();

        var act = async () => await sut.SetCoordsAsync(user.Id, lat, lon, null, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
        test.Dispose();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(99)]
    public async Task SetCoordsAsync_rejects_out_of_range_precision(int badPrecision)
    {
        var (sut, test, _, user) = Build();

        var act = async () => await sut.SetCoordsAsync(user.Id, 0, 0, null, badPrecision);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*precisionDecimals*");
        test.Dispose();
    }

    [Fact]
    public async Task SetCoordsAsync_inherits_the_users_previous_precision_when_none_supplied()
    {
        // Without inheritance a user who picked "exact" (5) for a meet-up
        // would silently snap back to default (3) the next time they share
        // browser location — surprising and bad for the meet-up use case.
        var (sut, test, _, user) = Build();
        await sut.SetCoordsAsync(user.Id, 1.0, 1.0, null, precisionDecimals: 5);

        await sut.SetCoordsAsync(user.Id, 2.0, 2.0, null, precisionDecimals: null);

        var row = await test.Db.SharedLocations.SingleAsync();
        row.PrecisionDecimals.Should().Be(5);
        test.Dispose();
    }

    [Fact]
    public async Task SetByNameAsync_calls_the_geocoder_and_stashes_the_returned_label()
    {
        var (sut, test, geo, user) = Build();
        geo.NextResult = new GeocodeResult(52.37, 4.9, "Amsterdam, Netherlands");

        await sut.SetByNameAsync(user.Id, "Amsterdam", label: null, precisionDecimals: null);

        var row = await test.Db.SharedLocations.SingleAsync();
        row.Latitude.Should().Be(52.37);
        row.Source.Should().Be("named");
        row.Label.Should().Be("Amsterdam, Netherlands");
        geo.LastQuery.Should().Be("Amsterdam");
        test.Dispose();
    }

    [Fact]
    public async Task SetByNameAsync_user_supplied_label_overrides_the_geocoder_display_name()
    {
        var (sut, test, geo, user) = Build();
        geo.NextResult = new GeocodeResult(52.37, 4.9, "Amsterdam, Netherlands");

        await sut.SetByNameAsync(user.Id, "Amsterdam", label: "Software developer meetup", precisionDecimals: null);

        var row = await test.Db.SharedLocations.SingleAsync();
        row.Label.Should().Be("Software developer meetup");
        test.Dispose();
    }

    [Fact]
    public async Task SetByNameAsync_throws_a_clean_validation_error_when_the_geocoder_returns_null()
    {
        var (sut, test, geo, user) = Build();
        geo.NextResult = null;

        var act = async () => await sut.SetByNameAsync(user.Id, "blarghxyz", null, null);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*find*");
        (await test.Db.SharedLocations.AnyAsync()).Should().BeFalse(
            "an unsuccessful geocode must not leave a row behind");
        test.Dispose();
    }

    [Fact]
    public async Task ClearAsync_removes_the_row_and_audits()
    {
        var (sut, test, _, user) = Build();
        await sut.SetCoordsAsync(user.Id, 1.0, 1.0, null, null);

        await sut.ClearAsync(user.Id);

        (await test.Db.SharedLocations.AnyAsync()).Should().BeFalse();
        (await test.Db.AuditEvents.AnyAsync(a => a.Kind == AuditKind.LocationCleared)).Should().BeTrue();
        test.Dispose();
    }

    [Fact]
    public async Task ClearAsync_is_a_noop_when_nothing_was_shared()
    {
        var (sut, test, _, user) = Build();

        var act = async () => await sut.ClearAsync(user.Id);

        await act.Should().NotThrowAsync();
        test.Dispose();
    }

    [Fact]
    public async Task ListAsync_rounds_each_row_to_its_own_precision()
    {
        var (sut, test, _, _) = Build();

        // Three users, three precisions. The list must show each at the
        // user's chosen rounding — no global default, no min/max snap.
        var bob   = await AddUser(test, "bob");
        var carol = await AddUser(test, "carol");
        var dave  = await AddUser(test, "dave");
        await sut.SetCoordsAsync(bob.Id,   52.3791234, 4.9001234, null, precisionDecimals: 5); // ~1m
        await sut.SetCoordsAsync(carol.Id, 52.3791234, 4.9001234, null, precisionDecimals: 3); // ~110m
        await sut.SetCoordsAsync(dave.Id,  52.3791234, 4.9001234, null, precisionDecimals: 1); // ~11km

        var rows = (await sut.ListAsync()).OrderBy(r => r.Username).ToList();
        rows.Single(r => r.Username == "bob").Latitude.Should().Be(52.37912);
        rows.Single(r => r.Username == "carol").Latitude.Should().Be(52.379);
        rows.Single(r => r.Username == "dave").Latitude.Should().Be(52.4);
        test.Dispose();
    }

    [Fact]
    public async Task ListAsync_includes_PrecisionDecimals_so_the_UI_can_label_the_pin()
    {
        var (sut, test, _, user) = Build();
        await sut.SetCoordsAsync(user.Id, 0, 0, null, precisionDecimals: 4);

        var rows = await sut.ListAsync();

        rows[0].PrecisionDecimals.Should().Be(4);
        test.Dispose();
    }

    [Fact]
    public async Task ListAsync_includes_username_and_admin_flag_for_a_visual_marker_label()
    {
        var (sut, test, _, user) = Build();
        var u = await test.Db.Users.FirstAsync();
        u.IsAdmin = true;
        await test.Db.SaveChangesAsync();
        await sut.SetCoordsAsync(user.Id, 0, 0, null, null);

        var rows = await sut.ListAsync();

        rows[0].Username.Should().Be("alice");
        rows[0].IsAdmin.Should().BeTrue();
        test.Dispose();
    }

    [Fact]
    public async Task SetCoordsAsync_rounds_stored_coords_to_the_chosen_precision()
    {
        // Defence in depth: the frontend rounds before the RPC call, but a
        // misbehaving client (or a future caller that forgets to) must not
        // be able to leave finer-than-tier precision in the database.
        var (sut, test, _, user) = Build();

        await sut.SetCoordsAsync(user.Id, 52.3791234, 4.9001234, null, precisionDecimals: 3);

        var row = await test.Db.SharedLocations.SingleAsync();
        row.Latitude.Should().Be(52.379);
        row.Longitude.Should().Be(4.900);
        test.Dispose();
    }

    [Fact]
    public async Task SetByNameAsync_rounds_geocoder_coords_to_the_chosen_precision()
    {
        var (sut, test, geo, user) = Build();
        // Geocoders routinely return 7+ decimal places. The user chose
        // "city" precision, so nothing finer than that should be stored.
        geo.NextResult = new GeocodeResult(52.3791234, 4.9001234, "Amsterdam");

        await sut.SetByNameAsync(user.Id, "Amsterdam", label: null, precisionDecimals: 1);

        var row = await test.Db.SharedLocations.SingleAsync();
        row.Latitude.Should().Be(52.4);
        row.Longitude.Should().Be(4.9);
        test.Dispose();
    }

    [Fact]
    public async Task UpdateMetaAsync_lowering_precision_re_rounds_the_stored_coords()
    {
        // Switching from "exact" to "city" mid-share should not leave the
        // finer coords sitting on the row even though the public list
        // would round them on output. A DB peek must reflect the user's
        // current privacy choice.
        var (sut, test, _, user) = Build();
        await sut.SetCoordsAsync(user.Id, 52.3791234, 4.9001234, null, precisionDecimals: 5);
        // Sanity: exact-tier kept 5 decimals.
        (await test.Db.SharedLocations.SingleAsync()).Latitude.Should().Be(52.37912);

        await sut.UpdateMetaAsync(user.Id, label: null, precisionDecimals: 1, clearLabel: false);

        var row = await test.Db.SharedLocations.SingleAsync();
        row.PrecisionDecimals.Should().Be(1);
        row.Latitude.Should().Be(52.4);
        row.Longitude.Should().Be(4.9);
        test.Dispose();
    }

    [Fact]
    public async Task UpdateMetaAsync_updates_label_and_precision_without_touching_coords()
    {
        var (sut, test, _, user) = Build();
        await sut.SetCoordsAsync(user.Id, 52.379, 4.900, "old label", precisionDecimals: 3);

        var ok = await sut.UpdateMetaAsync(user.Id, label: "Software developer meetup", precisionDecimals: 5, clearLabel: false);

        ok.Should().BeTrue();
        var row = await test.Db.SharedLocations.SingleAsync();
        row.Latitude.Should().Be(52.379);
        row.Longitude.Should().Be(4.900);
        row.Label.Should().Be("Software developer meetup");
        row.PrecisionDecimals.Should().Be(5);
        test.Dispose();
    }

    [Fact]
    public async Task UpdateMetaAsync_returns_false_when_no_row_exists()
    {
        var (sut, test, _, user) = Build();

        var ok = await sut.UpdateMetaAsync(user.Id, label: "x", precisionDecimals: null, clearLabel: false);

        ok.Should().BeFalse();
        test.Dispose();
    }

    [Fact]
    public async Task UpdateMetaAsync_clearLabel_true_wipes_the_label()
    {
        // null Label means "leave alone" everywhere else, so removing a
        // label needs an explicit boolean opt-out — there's no other path.
        var (sut, test, _, user) = Build();
        await sut.SetCoordsAsync(user.Id, 0, 0, "to be removed", null);

        await sut.UpdateMetaAsync(user.Id, label: null, precisionDecimals: null, clearLabel: true);

        var row = await test.Db.SharedLocations.SingleAsync();
        row.Label.Should().BeNull();
        test.Dispose();
    }

    [Fact]
    public async Task UpdateMetaAsync_rejects_out_of_range_precision()
    {
        var (sut, test, _, user) = Build();
        await sut.SetCoordsAsync(user.Id, 0, 0, null, null);

        var act = async () => await sut.UpdateMetaAsync(user.Id, label: null, precisionDecimals: 99, clearLabel: false);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*precisionDecimals*");
        test.Dispose();
    }

    [Fact]
    public async Task UpdateMetaAsync_skips_persistence_when_nothing_actually_changed()
    {
        // No-op call (label & precision both unchanged) shouldn't bump
        // UpdatedAt or write an audit row — otherwise a UI that pings
        // updateMeta on every keystroke would noise up the audit log.
        var (sut, test, _, user) = Build();
        await sut.SetCoordsAsync(user.Id, 0, 0, "stable", precisionDecimals: 3);
        var before = (await test.Db.SharedLocations.SingleAsync()).UpdatedAt;
        await Task.Delay(15); // ensure DateTime.UtcNow would advance if persisted

        await sut.UpdateMetaAsync(user.Id, label: "stable", precisionDecimals: 3, clearLabel: false);

        var after = (await test.Db.SharedLocations.SingleAsync()).UpdatedAt;
        after.Should().Be(before);
        test.Dispose();
    }

    [Fact]
    public async Task First_share_audits_as_LocationShared_and_subsequent_updates_as_LocationUpdated()
    {
        var (sut, test, _, user) = Build();
        await sut.SetCoordsAsync(user.Id, 1.0, 1.0, null, null);
        await sut.SetCoordsAsync(user.Id, 2.0, 2.0, null, null);

        var kinds = await test.Db.AuditEvents
            .Where(a => a.Kind.StartsWith("location."))
            .OrderBy(a => a.At)
            .Select(a => a.Kind)
            .ToListAsync();

        kinds.Should().Equal(AuditKind.LocationShared, AuditKind.LocationUpdated);
        test.Dispose();
    }

    private static async Task<User> AddUser(TestDb test, string username)
    {
        var u = new User { Username = username, Email = $"{username}@x", PasswordHash = new byte[]{1}, PasswordSalt = new byte[]{1} };
        test.Db.Users.Add(u);
        await test.Db.SaveChangesAsync();
        return u;
    }

    private sealed class FakeGeocoder : IGeocodingService
    {
        public GeocodeResult? NextResult { get; set; }
        public string? LastQuery { get; private set; }
        public Task<GeocodeResult?> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(NextResult);
        }
    }
}
