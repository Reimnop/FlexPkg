using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SteamKit2;
using SteamKit2.Authentication;

namespace FlexPkg.Steam;

public sealed class SteamHub : IAsyncDisposable
{
    public SteamClient Client => client;
    public SteamUser User => steamUser;
    public SteamApps Apps => steamApps;
    public SteamContent Content => steamContent;
    
    private readonly SteamClient client;
    private readonly CallbackManager manager;
    private readonly SteamUser steamUser;
    private readonly SteamApps steamApps;
    private readonly SteamContent steamContent;
    
    private readonly Task steamCallbackTask;
    private readonly CancellationTokenSource steamCallbackTaskCancellation;
    
    private SteamHub(SteamClient client, CallbackManager manager, Task steamCallbackTask, CancellationTokenSource steamCallbackTaskCancellation)
    {
        this.client = client;
        this.manager = manager;
        this.steamCallbackTask = steamCallbackTask;
        this.steamCallbackTaskCancellation = steamCallbackTaskCancellation;
        
        steamUser = client.GetHandler<SteamUser>()!;
        steamApps = client.GetHandler<SteamApps>()!;
        steamContent = client.GetHandler<SteamContent>()!;
    }
    
    public static async Task<SteamHub> CreateAsync(string username, string password, ISteamTokenStore tokenStore, Func<IAuthenticator> authenticatorFactory)
    {
        var client = new SteamClient();
        var manager = new CallbackManager(client);
        var callbackTaskCancellation = new CancellationTokenSource();
        var ct = callbackTaskCancellation.Token;
        var callbackTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested();
                
                manager.RunCallbacks();
                await Task.Delay(50, ct);
            }
        }, ct);
        var steamUser = client.GetHandler<SteamUser>();
        Debug.Assert(steamUser is not null);
        
        manager.Subscribe<SteamClient.ConnectedCallback>(async _ =>
        {
            var currentToken = await tokenStore.GetTokenAsync(username);

            // If we have a token, we can logon with it
            if (currentToken is not null)
            {
                steamUser.LogOn(new SteamUser.LogOnDetails
                {
                    Username = username,
                    AccessToken = currentToken,
                    ShouldRememberPassword = true,
                });
            }
            else
            {
                // Begin authenticating via credentials
                var authSession = await client.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
                {
                    Username = username,
                    Password = password,
                    IsPersistentSession = true,
                    Authenticator = authenticatorFactory(),
                });

                // Starting polling Steam for authentication response
                // ReSharper disable once MethodSupportsCancellation
                var pollResponse = await authSession.PollingWaitForResultAsync();

                var refreshToken = pollResponse.RefreshToken;
                await tokenStore.SaveTokenAsync(username, refreshToken);

                // Logon to Steam with the access token we have received
                steamUser.LogOn(new SteamUser.LogOnDetails
                {
                    Username = username,
                    AccessToken = refreshToken,
                    ShouldRememberPassword = true,
                });
            }
        });
        
        var tcs = new TaskCompletionSource();
        manager.Subscribe<SteamUser.LoggedOnCallback>(callback =>
        {
            if (callback.Result != EResult.OK)
            {
                tcs.SetException(new Exception($"Unable to logon to Steam: {callback.Result} / {callback.ExtendedResult}"));
                return;
            }

            tcs.SetResult();
        });
        client.Connect();
        
        await tcs.Task;
        return new SteamHub(client, manager, callbackTask, callbackTaskCancellation);
    }

    public async ValueTask DisposeAsync()
    {
        steamUser.LogOff();
        await steamCallbackTaskCancellation.CancelAsync();
    }
}