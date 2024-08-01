using CommandLine;
using FlexPkg;
using FlexPkg.Data;
using FlexPkg.Steam;
using FlexPkg.UserInterface;
using FlexPkg.UserInterface.Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var parserResult = Parser.Default.ParseArguments<CliOptions>(args);
if (parserResult.Errors.Any())
    return -1;

var options = parserResult.Value;

var serviceCollection = new ServiceCollection();
serviceCollection.AddLogging(builder => builder.AddConsole());
serviceCollection.AddSingleton(options);
serviceCollection.AddSqlite<FlexPkgContext>("Data Source=FlexPkg.db");
serviceCollection.AddSingleton<IUserInterface, DiscordUserInterface>();
serviceCollection.AddTransient<SteamTokenStore>();
serviceCollection.AddTransient<SteamAuthenticator>();
serviceCollection.AddSteam(new SteamAuthenticationInfo(
    options.Username, 
    options.Password, 
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