using CommandLine;
using FlexPkg.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlexPkg.Design;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FlexPkgContext>
{
    public FlexPkgContext CreateDbContext(string[] args)
    {
        var parserResult = Parser.Default.ParseArguments<DesignTimeCliOptions>(args);
        if (parserResult.Errors.Any())
            throw new ArgumentException("Invalid arguments");
        
        var options = parserResult.Value;
        
        var optionsBuilder = new DbContextOptionsBuilder<FlexPkgContext>();
        var migrationsAssemblyName = DatabaseUtil.GetMigrationsAssemblyName(options.Provider);
        var connector = DatabaseUtil.GetDatabaseConnector(options.Provider, migrationsAssemblyName);
        connector(optionsBuilder, options.ConnectionString);
        return new FlexPkgContext(optionsBuilder.Options);
    }
}