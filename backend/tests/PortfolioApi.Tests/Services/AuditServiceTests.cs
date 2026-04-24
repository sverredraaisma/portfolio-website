using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Constants;
using PortfolioApi.Models;
using PortfolioApi.Services;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.Services;

public class AuditServiceTests
{
    private static (AuditService sut, TestDb test, Guid userId) Setup()
    {
        var test = new TestDb();
        var u = new User { Username = "u", Email = "u@x", PasswordHash = new byte[]{1}, PasswordSalt = new byte[]{1} };
        test.Db.Users.Add(u);
        test.Db.SaveChanges();
        test.Db.ChangeTracker.Clear();
        return (new AuditService(test.Db), test, u.Id);
    }

    [Fact]
    public void Record_stages_the_row_but_does_not_save_until_the_caller_commits()
    {
        var (sut, test, userId) = Setup();

        sut.Record(userId, AuditKind.PasswordChanged);

        // Staged in the change tracker — but no SaveChanges yet.
        test.Db.AuditEvents.Local.Should().HaveCount(1);
        test.Db.AuditEvents.Local.Single().UserId.Should().Be(userId);
        test.Dispose();
    }

    [Fact]
    public async Task Record_only_persists_when_the_outer_SaveChanges_runs()
    {
        var (sut, test, userId) = Setup();

        sut.Record(userId, AuditKind.SessionsRevoked);
        await test.Db.SaveChangesAsync();

        (await test.Db.AuditEvents.CountAsync()).Should().Be(1);
        test.Dispose();
    }

    [Fact]
    public async Task Record_carries_the_optional_detail_through()
    {
        var (sut, test, userId) = Setup();

        sut.Record(userId, AuditKind.EmailChanged, "to *@example.com");
        await test.Db.SaveChangesAsync();

        var stored = await test.Db.AuditEvents.SingleAsync();
        stored.Detail.Should().Be("to *@example.com");
        stored.Kind.Should().Be(AuditKind.EmailChanged);
        test.Dispose();
    }

    [Fact]
    public async Task Record_stamps_At_to_a_recent_timestamp()
    {
        var (sut, test, userId) = Setup();
        var before = DateTime.UtcNow.AddSeconds(-1);

        sut.Record(userId, AuditKind.TotpEnabled);
        await test.Db.SaveChangesAsync();

        var stored = await test.Db.AuditEvents.SingleAsync();
        stored.At.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow.AddSeconds(1));
        test.Dispose();
    }
}
