using Microsoft.EntityFrameworkCore;

namespace FlexPkg.Database;

public class FlexPkgContext(DbContextOptions<FlexPkgContext> options) : DbContext(options)
{
    public DbSet<SteamAccount> SteamAccounts { get; set; }
    public DbSet<SteamAppManifest> SteamAppManifests { get; set; }
}