using CommandLine;

namespace FlexPkg;

public class CliOptions
{
    [Option('c', "config", HelpText = "Path to the config file.", Default = "userdata/config.json")]
    public string ConfigPath { get; set; } = "userdata/config.json";
}