using Microsoft.EntityFrameworkCore;

namespace FlexPkg;

public delegate void DatabaseConnectorDelegate(DbContextOptionsBuilder builder, string connectionString);

public static class DatabaseUtil
{
    public const string SqliteProvider = "sqlite";
    public const string MySqlProvider = "mysql";
    public const string PostgreSqlProvider = "postgresql";
    
    public static string GetProviderDisplayName(string provider)
        => provider switch
        {
            SqliteProvider => "SQLite",
            MySqlProvider => "MySQL",
            PostgreSqlProvider => "PostgreSQL",
            _ => throw new NotSupportedException($"Provider '{provider}' is not supported")
        };
    
    public static DatabaseConnectorDelegate GetDatabaseConnector(string provider, string? migrationsAssemblyName)
    {
        return provider switch
        {
            SqliteProvider => (builder, connectionString) => builder.UseSqlite(
                connectionString, 
                x => x.MigrationsAssembly(migrationsAssemblyName)),
            MySqlProvider => (builder, connectionString) => builder.UseMySql(
                connectionString, 
                ServerVersion.AutoDetect(connectionString),
                x => x.MigrationsAssembly(migrationsAssemblyName)),
            PostgreSqlProvider => (builder, connectionString) => builder.UseNpgsql(
                connectionString, 
                x => x.MigrationsAssembly(migrationsAssemblyName)),
            _ => throw new NotSupportedException($"Provider '{provider}' is not supported")
        };
    }
    
    public static string GetMigrationsAssemblyName(string provider)
        => provider switch
        {
            SqliteProvider => "FlexPkg.SqliteMigrations",
            MySqlProvider => "FlexPkg.MySqlMigrations",
            PostgreSqlProvider => "FlexPkg.PostgreSqlMigrations",
            _ => throw new NotSupportedException($"Provider '{provider}' is not supported")
        };
}