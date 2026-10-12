using MacroDeck.Sdk.Batteries;

namespace MacroDeckHost.Application.Batteries;

public interface IBatteryRegistry : IBatteryApi
{
	void Publish(string ownerId, string? sessionId, IReadOnlyList<BatteryDevice> devices);

	void Remove(string ownerId, IReadOnlyList<string> localIds);

	void Sync(string ownerId, string? sessionId, IReadOnlyList<BatteryDevice> devices);

	void RemoveAll(string ownerId);

	void RemoveSession(string ownerId, string sessionId);

	void SetProviderName(string ownerId, string providerName);
}
