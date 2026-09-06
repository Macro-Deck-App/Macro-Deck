using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Migration;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Migration;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Migration;

/// <summary>What a connected plugin declared about one application it takes a setup over from.</summary>
public sealed record RemoteMigrationDescriptor(
	MigrationSource Source,
	IReadOnlyList<string> ClaimedActionSources,
	IReadOnlyList<string> ClaimedSettingsSources);

/// <summary>
/// One declared migration of a connected plugin, answering over the connection rather than in process.
/// </summary>
/// <remarks>
/// The SDK contract is asynchronous precisely so this class can exist: a translation that had to be
/// answered synchronously could only be served by blocking on the plugin, which is what ADR 0062 forbids.
///
/// Every failure here is answered as "no equivalent" rather than propagated. A plugin that is unreachable,
/// slow or broken then costs the user a placeholder action carrying its original configuration - the same
/// outcome as a plugin that simply has no equivalent - instead of failing a migration that has already
/// read hundreds of buttons successfully.
/// </remarks>
internal sealed class RemoteIntegrationMigration : IIntegrationMigration
{
	private readonly IPluginCapabilityInvoker _invoker;
	private readonly string _pluginId;
	private readonly RemoteMigrationDescriptor _descriptor;

	public RemoteIntegrationMigration(
		IPluginCapabilityInvoker invoker,
		string pluginId,
		RemoteMigrationDescriptor descriptor)
	{
		_invoker = invoker;
		_pluginId = pluginId;
		_descriptor = descriptor;
	}

	public MigrationSource Source => _descriptor.Source;

	public IReadOnlyList<string> ClaimedActionSources => _descriptor.ClaimedActionSources;

	public IReadOnlyList<string> ClaimedSettingsSources => _descriptor.ClaimedSettingsSources;

	public async Task<ActionMigrationResult?> MigrateActionAsync(
		ForeignAction action,
		CancellationToken cancellationToken)
	{
		var result = await Invoke<MigrateActionResultPayload>(CapabilityOperations.Migration.MigrateAction,
				new MigrateActionRequestPayload
				{
					Source = Source.ToString(),
					TypeName = action.TypeName,
					ActionSource = action.ActionSource,
					DisplayName = action.DisplayName,
					Configuration = action.Configuration,
					ConfigurationSummary = action.ConfigurationSummary
				},
				cancellationToken)
			.ConfigureAwait(false);

		if (result is not { Translated: true } || result.IntegrationId is null || result.ActionId is null)
		{
			return null;
		}

		return new ActionMigrationResult(result.IntegrationId,
			result.ActionId,
			result.Label ?? result.ActionId,
			result.Parameters ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
			result.Warnings);
	}

	public async Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
	{
		var result = await Invoke<MigrateConfigurationResultPayload>(
				CapabilityOperations.Migration.MigrateConfiguration,
				new MigrateConfigurationRequestPayload
				{
					Source = Source.ToString(),
					SettingsSource = settings.SettingsSource,
					Settings = settings.Settings,
					Credentials = settings.Credentials,
					Actions =
					[
						.. settings.Actions.Select(action => new ForeignActionDto
						{
							TypeName = action.TypeName,
							ActionSource = action.ActionSource,
							DisplayName = action.DisplayName,
							Configuration = action.Configuration,
							ConfigurationSummary = action.ConfigurationSummary
						})
					]
				},
				cancellationToken)
			.ConfigureAwait(false);

		return result?.Configurations
				.Select(configuration => new MigratedConfiguration(configuration.IntegrationId,
					configuration.Title,
					configuration.Values,
					configuration.Secrets.ToDictionary(pair => pair.Key,
						pair => new MigratedSecret(pair.Value.Value, ParseKind(pair.Value.Kind)),
						StringComparer.Ordinal)))
				.ToList() ??
			[];
	}

	/// <summary>An unrecognised kind is treated as the stricter one, which is never shown back to anyone.</summary>
	private static MigratedSecretKind ParseKind(string kind)
		=> string.Equals(kind, nameof(MigratedSecretKind.Password), StringComparison.OrdinalIgnoreCase)
			? MigratedSecretKind.Password
			: MigratedSecretKind.Secret;

	private async Task<TResult?> Invoke<TResult>(
		string operation,
		object arguments,
		CancellationToken cancellationToken)
		where TResult : class
	{
		try
		{
			var data = await _invoker.InvokeAsync(_pluginId,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.Migration,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = operation,
						Arguments = arguments
					},
					cancellationToken)
				.ConfigureAwait(false);

			return data?.Deserialize<TResult>(PluginProtocolJson.Options);
		}
		catch (RemoteCapabilityException)
		{
			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
