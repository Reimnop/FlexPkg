using CommandLine;
using FlexPkg;
using FlexPkg.Data;
using FlexPkg.Steam;
using FlexPkg.UserInterface;
using FlexPkg.UserInterface.Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var parserResult = Parser.Default.ParseArguments<CliOptions>(args);
if (parserResult.Errors.Any())
    return -1;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile(parserResult.Value.ConfigPath)
    .Build();

var serviceCollection = new ServiceCollection();
var options = configuration.Get<AppOptions>()!;
serviceCollection.Configure<AppOptions>(configuration);

serviceCollection.AddLogging(builder => builder.AddConsole());
serviceCollection.AddSqlite<FlexPkgContext>($"Data Source={options.DbPath}");
serviceCollection.AddSingleton<IUserInterface, DiscordUserInterface>();
serviceCollection.AddTransient<SteamTokenStore>();
serviceCollection.AddTransient<SteamAuthenticator>();
serviceCollection.AddSteam(new SteamAuthenticationInfo(
    options.Steam.UserName, 
    options.Steam.Password, 
    serviceProvider => serviceProvider.GetRequiredService<SteamTokenStore>(),
    serviceProvider => serviceProvider.GetRequiredService<SteamAuthenticator>()));
serviceCollection.AddSingleton<App>();

var serviceProvider = serviceCollection.BuildServiceProvider();

// Make sure the database is created
var dbContext = serviceProvider.GetRequiredService<FlexPkgContext>();
await dbContext.Database.EnsureCreatedAsync();

var app = serviceProvider.GetRequiredService<App>();
await app.RunAsync();
return 0;