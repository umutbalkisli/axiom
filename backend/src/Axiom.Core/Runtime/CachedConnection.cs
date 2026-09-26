using System.Data.Common;

namespace Axiom.Runtime;

internal sealed class CachedConnection(DbConnection dbConnection) : IAsyncDisposable
{
    public DbConnection DbConnection { get; } = dbConnection;
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public async ValueTask DisposeAsync()
    {
        await DbConnection.DisposeAsync();
        Gate.Dispose();
    }
}
