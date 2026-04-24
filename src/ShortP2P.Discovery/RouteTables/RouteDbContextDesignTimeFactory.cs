using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShortP2P.Discovery.RouteTables;

/// <summary>
/// Позволяет выполнять <c>dotnet ef</c> для библиотеки без отдельного startup-проекта.
/// </summary>
public sealed class RouteDbContextDesignTimeFactory : IDesignTimeDbContextFactory<RouteDbContext>
{
    public RouteDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RouteDbContext>();
        optionsBuilder.UseSqlite("Data Source=route-design.db");
        return new RouteDbContext(optionsBuilder.Options);
    }
}
