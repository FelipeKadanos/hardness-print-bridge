namespace Hardness.PrintBridge.Agent.Application;

public interface IRemoteJobFetcher {
    Task<RemoteFetchResult> FetchAsync(CancellationToken cancellationToken);
}
