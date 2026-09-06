using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Migration;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Migration;

namespace MacroDeck.Plugin.Hosting.Capabilities.Migration;

/// <summary>
/// Exposes every registered integration's <c>IMigrationProvider</c> as the <c>migration</c> capability, so
/// a plugin can take its own setup over from another application exactly as a built-in integration does.
/// </summary>
/// <remarks>
/// Provider-shaped like <c>weather</c>: one <c>provider</c> local id, with the source application named in
/// the operation arguments. A migration is addressed by which application it reads rather than by an id of
/// its own, because that is the only thing the host knows about it when it goes looking - it is reading
/// that application's files, and asks whoever claims what it found.
/// </remarks>
internal sealed class MigrationCapabilityHandler(IEnumerable<IPluginIntegration> integrations)
	: ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly IReadOnlyList<IIntegrationMigration> _migrations =
		[.. integrations.OfType<IMigrationProvider>().SelectMany(provider => provider.Migrations)];

	public string Kind => CapabilityKinds.Migration;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> _migrations.Count == 0
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Migration, LocalId = ProviderCapabilityId.LocalId, VersionRange = _version
				}
			];

	public async Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		// describe ignores the local id entirely - see EventsCapabilityHandler's identical remark.
		if (string.Equals(invocation.Operation, CapabilityOperations.Migration.Describe, StringComparison.Ordinal))
		{
			return Describe();
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No migration provider '{invocation.LocalId}' is registered in this plugin.");
		}

		return invocation.Operation switch
		{
			CapabilityOperations.Migration.MigrateAction => await MigrateAction(invocation, cancellationToken)
				.ConfigureAwait(false),
			CapabilityOperations.Migration.MigrateConfiguration => await MigrateConfiguration(invocation,
					cancellationToken)
				.ConfigureAwait(false),
			_ => CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The migration capability does not support '{invocation.Operation}'.")
		};
	}

	private CapabilityInvocationResult Describe()
		=> CapabilityInvocationResult.Ok(new MigrationDescribePayload
		{
			Migrations =
			[
				.. _migrations.Select(migration => new MigrationDescriptorDto
				{
					Source = migration.Source.ToString(),
					ClaimedActionSources = [.. migration.ClaimedActionSources],
					ClaimedSettingsSources = [.. migration.ClaimedSettingsSources]
				})
			]
		});

	private async Task<CapabilityInvocationResult> MigrateAction(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<MigrateActionRequestPayload>(PluginProtocolJson.Options);
		if (arguments is null || Find(arguments.Source) is not { } migration)
		{
			return NoSuchSource(arguments?.Source);
		}

		var result = await migration.MigrateActionAsync(new ForeignAction(arguments.TypeName,
					arguments.ActionSource,
					arguments.DisplayName,
					arguments.Configuration,
					arguments.ConfigurationSummary),
				cancellationToken)
			.ConfigureAwait(false);

		// "No equivalent" is an answer, not a failure: the host keeps the action as a placeholder carrying
		// its original configuration rather than this plugin inventing something that behaves differently.
		return CapabilityInvocationResult.Ok(result is null
			? new MigrateActionResultPayload { Translated = false }
			: new MigrateActionResultPayload
			{
				Translated = true,
				IntegrationId = result.IntegrationId,
				ActionId = result.ActionId,
				Label = result.Label,
				Parameters = result.Parameters,
				Warnings = result.Warnings
			});
	}

	private async Task<CapabilityInvocationResult> MigrateConfiguration(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments =
			invocation.Arguments?.Deserialize<MigrateConfigurationRequestPayload>(PluginProtocolJson.Options);
		if (arguments is null || Find(arguments.Source) is not { } migration)
		{
			return NoSuchSource(arguments?.Source);
		}

		var configurations = await migration.MigrateConfigurationAsync(new ForeignPluginSettings(
					arguments.SettingsSource,
					arguments.Settings,
					arguments.Credentials,
					[
						.. (arguments.Actions ?? []).Select(action => new ForeignAction(action.TypeName,
							action.ActionSource,
							action.DisplayName,
							action.Configuration,
							action.ConfigurationSummary))
					]),
				cancellationToken)
			.ConfigureAwait(false);

		return CapabilityInvocationResult.Ok(new MigrateConfigurationResultPayload
		{
			Configurations =
			[
				.. configurations.Select(configuration => new MigratedConfigurationDto
				{
					IntegrationId = configuration.IntegrationId,
					Title = configuration.Title,
					Values = configuration.Values,
					Secrets = configuration.Secrets.ToDictionary(pair => pair.Key,
						pair => new MigratedSecretDto { Value = pair.Value.Value, Kind = pair.Value.Kind.ToString() },
						StringComparer.Ordinal)
				})
			]
		});
	}

	private IIntegrationMigration? Find(string? source)
		=> Enum.TryParse<MigrationSource>(source, ignoreCase: true, out var parsed)
			? _migrations.FirstOrDefault(migration => migration.Source == parsed)
			: null;

	private static CapabilityInvocationResult NoSuchSource(string? source)
		=> CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
			$"This plugin declares no migration from '{source}'.");
}
