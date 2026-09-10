namespace MacroDeckHost.Application.Network.Discovery;

public interface IServiceAdvertiser : IDisposable
{
	event Action? StateChanged;

	bool IsAvailable { get; }

	bool NeedsRepublish { get; }

	void CheckLiveness();

	void Publish(ServiceAdvertisement advertisement);

	void Withdraw();
}

public interface IDiscoveryAdvertisementRefresher
{
	void RequestRefresh();
}
