using CommandLine;

namespace FlexPkg.Design;

public class DesignTimeCliOptions
{
    [Option('p', "provider", HelpText = "Database provider.", Required = true)]
    public required string Provider { get; set; }
    
    [Option('c', "connection-string", HelpText = "Database connection string.", Required = true)]
    public required string ConnectionString { get; set; }
}