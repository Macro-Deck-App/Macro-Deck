using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>Proxies <see cref="IIntegrationConfig"/> over <c>host.invoke</c> against
/// <see cref="HostApis.Config"/>.</summary>
internal sealed class RemoteIntegrationConfig(IHostInvoker invoker) : IIntegrationConfig
{
	public async Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
	{
		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Config,
			HostOperations.Config.Entries,
			null,
			cancellationToken);
		return result?.Deserialize<List<ConfigEntrySnapshot>>(PluginProtocolJson.Options) ?? [];
	}

	public async Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Config,
			HostOperations.Config.GetString,
			new ConfigGetArguments { EntryId = entryId, Key = key },
			cancellationToken);
		return result?.Deserialize<string?>(PluginProtocolJson.Options);
	}

	public async Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Config,
			HostOperations.Config.GetSecret,
			new ConfigGetArguments { EntryId = entryId, Key = key },
			cancellationToken);
		return result?.Deserialize<string?>(PluginProtocolJson.Options);
	}

	public Task SetStringAsync(Guid entryId, string key, string? value, CancellationToken cancellationToken = default)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.Config,
			HostOperations.Config.SetString,
			new ConfigSetStringArguments { EntryId = entryId, Key = key, Value = value },
			cancellationToken);

	public Task SetSecretAsync(Guid entryId, string key, string value, CancellationToken cancellationToken = default)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.Config,
			HostOperations.Config.SetSecret,
			new ConfigSetSecretArguments { EntryId = entryId, Key = key, Value = value },
			cancellationToken);
}
