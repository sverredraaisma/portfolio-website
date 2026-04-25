using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using PortfolioApi.Constants;
using PortfolioApi.Data;
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
        var sut = new LocationService(test.Db, geo, audit, Options.Create(new LocationOptions
        {
            PublicPrecisionDecimals = 3
        }));
        return (sut, test, geo, user);
    }

    [Fact]
    public async Task SetCoordsAsync_inserts_a_row_with_browser_source()
    {
        var (sut, test, _, user) = Build();

        await sut.SetCoordsAsync(user.Id, 52.379, 4.900, label: "home");

        var row = await test.Db.SharedLocations.SingleAsync();
        row.UserId.Should().Be(user.Id);
        row.Latitude.Should().Be(52.379);
        row.Longitude.Should().Be(4.900);
        row.Label.Should().Be("home");
        row.Source.Should().Be("browser");
        test.Dispose();
    }

    [Fact]
    public async Task SetCoordsAsync_updates_the_existing_row_so_one_user_only_has_one_pin()
    {
        var (sut, test, _, user) = Build();
        await sut.SetCoordsAsync(user.Id, 1.0, 1.0, null);

        await sut.SetCoordsAsync(user.Id, 2.0, 2.0, "moved");

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

        var act = async () => await sut.SetCoordsAsync(user.Id, lat, lon, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
        test.Dispose();
    }

    [Fact]
    public async Task SetByNameAsync_calls_the_geocoder_and_stashes_the_returned_label()
    {
        var (sut, test, geo, user) = Build();
        geo.NextResult = new GeocodeResult(52.37, 4.9, "Amsterdam, Netherlands");

        await sut.SetByNameAsync(user.Id, "Amsterdam");

        var row = await test.Db.SharedLocations.SingleAsync();
        row.Latitude.Should().Be(52.37);
        row.Source.Should().Be("named");
        row.Label.Should().Be("Amsterdam, Netherlands");
        geo.LastQuery.Should().Be("Amsterdam");
        test.Dispose();
    }

    [Fact]
    public async Task SetByNameAsync_throws_a_clean_validation_error_when_the_geocoder_returns_null()
    {
        var (sut, test, geo, user) = Build();
        geo.NextResult = null;

        var act = async () => await sut.SetByNameAsync(user.Id, "blarghxyz");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*find*");
        (await test.Db.SharedLocations.AnyAsync()).Should().BeFalse(
            "an unsuccessful geocode must not leave a row behind");
        test.Dispose();
    }

    [Fact]
    public async Task ClearAsync_removes_the_row_and_audits()
    {
        var (sut, test, _, user) = Build();
        await sut.SetCoordsAsync(user.Id, 1.0, 1.0, null);

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
    public async Task ListAsync_rounds_coords_to_the_configured_precision()
    {
        var (sut, test, _, user) = Build();
        await sut.SetCoordsAsync(user.Id, 52.3791234, 4.9001234, null);

        var rows = await sut.ListAsync();

        rows.Should().HaveCount(1);
        // 3 decimals → 110m precision; the stored full-precision value
        // never reaches the wire.
        rows[0].Latitude.Should().Be(52.379);
        rows[0].Longitude.Should().Be(4.900);
        test.Dispose();
    }

    [Fact]
    public async Task ListAsync_includes_username_and_admin_flag_for_a_visual_marker_label()
    {
        var (sut, test, _, user) = Build();
        // Promote the user.
        var u = await test.Db.Users.FirstAsync();
        u.IsAdmin = true;
        await test.Db.SaveChangesAsync();
        await sut.SetCoordsAsync(user.Id, 0, 0, null);

        var rows = await sut.ListAsync();

        rows[0].Username.Should().Be("alice");
        rows[0].IsAdmin.Should().BeTrue();
        test.Dispose();
    }

    [Fact]
    public async Task First_share_audits_as_LocationShared_and_subsequent_updates_as_LocationUpdated()
    {
        var (sut, test, _, user) = Build();
        await sut.SetCoordsAsync(user.Id, 1.0, 1.0, null);
        await sut.SetCoordsAsync(user.Id, 2.0, 2.0, null);

        var kinds = await test.Db.AuditEvents
            .Where(a => a.Kind.StartsWith("location."))
            .OrderBy(a => a.At)
            .Select(a => a.Kind)
            .ToListAsync();

        kinds.Should().Equal(AuditKind.LocationShared, AuditKind.LocationUpdated);
        test.Dispose();
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
