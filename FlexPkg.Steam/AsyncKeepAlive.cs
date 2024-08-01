namespace FlexPkg.Steam;

public sealed class AsyncKeepAlive<T>(Func<Task<T>> factory, Func<T, bool> aliveChecker) : IAsyncDisposable
{
    private T? value;

    public async Task<T> GetOrCreateValue()
    {
        if (value is null)
        {
            value = await factory();
            return value;
        }

        if (!aliveChecker(value))
        {
            switch (value)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }

            value = await factory();
            return value;
        }

        return value;
    }

    public async ValueTask DisposeAsync()
    {
        if (value is null)
            return;
        
        switch (value)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}