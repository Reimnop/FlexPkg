using CommandLine;

namespace FlexPkg;

public class CliOptions
{
    [Option('c', "config", HelpText = "Path to the config file.", Default = "config.json")]
    public string ConfigPath { get; set; } = "config.json";
}