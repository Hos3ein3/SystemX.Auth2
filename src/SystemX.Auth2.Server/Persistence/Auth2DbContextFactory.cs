using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SystemX.Auth2.Server.Infrastructure;

namespace SystemX.Auth2.Server.Persistence;

/// <summary>Used only by <c>dotnet ef</c> to create migrations. It never connects to a database.</summary>
internal sealed class Auth2DbContextFactory : IDesignTimeDbContextFactory<Auth2DbContext>
{
    public Auth2DbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<Auth2DbContext>();
        builder.UseNpgsql("Host=localhost;Database=auth2_design;Username=postgres;Password=design-time-only");
        builder.UseOpenIddict<SystemX.Auth2.Server.Infrastructure.OpenIddict.Auth2Application, SystemX.Auth2.Server.Infrastructure.OpenIddict.Auth2Authorization, SystemX.Auth2.Server.Infrastructure.OpenIddict.Auth2Scope, SystemX.Auth2.Server.Infrastructure.OpenIddict.Auth2Token, Guid>();
        return new Auth2DbContext(builder.Options, new ProjectScope());
    }
}
