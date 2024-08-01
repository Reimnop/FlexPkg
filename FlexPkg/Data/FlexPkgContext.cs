using Microsoft.EntityFrameworkCore;

namespace FlexPkg.Data;

public class FlexPkgContext(DbContextOptions<FlexPkgContext> options) : DbContext(options)
{
    public DbSet<SteamAccount> SteamAccounts { get; set; }
    public DbSet<SteamAppManifest> SteamAppManifests { get; set; }
}