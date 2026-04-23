using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PortfolioApi.Data;

/// EF Core CLI tools (dotnet ef) use this when generating migrations so they
/// don't have to bootstrap the full web host (which would fail because options
/// like Jwt:Key are not set at design time). The connection string here is only
/// used for migrations metadata; nothing connects to the DB during scaffolding.
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=portfolio_design;Username=design;Password=design")
            .Options;
        return new AppDbContext(options);
    }
}
