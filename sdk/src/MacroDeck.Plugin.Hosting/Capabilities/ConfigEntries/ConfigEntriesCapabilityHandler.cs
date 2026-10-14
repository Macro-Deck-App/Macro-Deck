using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigEntries;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeck.Plugin.Hosting.Capabilities.ConfigEntries;

internal sealed class ConfigEntriesCapabilityHandler(
	IEnumerable<IPluginIntegration> integrations,
	IIntegrationContext? context = null) : ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly IConfigEntryLifecycle? _lifecycle = integrations.OfType<IConfigEntryLifecycle>().FirstOrDefault();

	public string Kind => CapabilityKinds.ConfigEntries;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
	{
		return _lifecycle is null
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.ConfigEntries,
					LocalId = ProviderCapabilityId.LocalId,
					VersionRange = _version
				}
			];
	}

	public async Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		if (string.Equals(invocation.Operation, CapabilityOperations.ConfigEntries.Describe, StringComparison.Ordinal))
		{
			return CapabilityInvocationResult.Ok();
		}

		if (_lifecycle is null
			|| !string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No config-entries provider '{invocation.LocalId}' is registered in this plugin.");
		}

		switch (invocation.Operation)
		{
			case CapabilityOperations.ConfigEntries.Prepare:
			{
				if (Arguments<ConfigEntryPrepareArguments>(invocation) is not { } arguments)
				{
					return MissingArguments(invocation.Operation);
				}

				var result = await _lifecycle.PrepareEntryAsync(new ConfigEntryPreparation
						{
							EntryId = arguments.EntryId,
							Title = arguments.Title,
							Values = arguments.Values,
							Existing = arguments.Existing is null ? null : ToEntry(arguments.Existing),
							Siblings = [.. arguments.Siblings.Select(ToEntry)],
							TitleChanged = arguments.TitleChanged,
							RejectedVariableNames = arguments.RejectedVariableNames ?? []
						},
						cancellationToken)
					.ConfigureAwait(false);

				return CapabilityInvocationResult.Ok(new ConfigEntryPrepareResult
				{
					Success = result.Success,
					Values = result.Values,
					Error = result.Success || result.Error.IsEmpty ? null : result.Error,
					RetryOnConflict = result.RetryOnConflict ? true : null,
					ReservedVariables =
					[
						.. result.ReservedVariables
							.Where(variable
								=> !string.IsNullOrEmpty(variable.Name) && !string.IsNullOrEmpty(variable.ResolvedId))
							.Select(variable => new ConfigEntryReservedVariableDto
							{
								Name = variable.Name!, DefinitionId = variable.ResolvedId!,
								Type = variable.Type.ToString()
							})
					]
				});
			}

			case CapabilityOperations.ConfigEntries.Status:
			{
				if (Arguments<ConfigEntryArguments>(invocation) is not { } arguments)
				{
					return MissingArguments(invocation.Operation);
				}

				var status = await _lifecycle.GetEntryStatusAsync(ToEntry(arguments.Entry), cancellationToken)
					.ConfigureAwait(false);
				return CapabilityInvocationResult.Ok(new ConfigEntryStatusResult { Status = status.ToString() });
			}

			case CapabilityOperations.ConfigEntries.Usable:
			{
				if (Arguments<ConfigEntryArguments>(invocation) is not { } arguments)
				{
					return MissingArguments(invocation.Operation);
				}

				var usable = await _lifecycle.IsEntryUsableAsync(ToEntry(arguments.Entry), cancellationToken)
					.ConfigureAwait(false);
				return CapabilityInvocationResult.Ok(new ConfigEntryUsableResult { Usable = usable });
			}

			case CapabilityOperations.ConfigEntries.Reload:
			{
				var reloaded = await _lifecycle.ReloadEntriesAsync(cancellationToken).ConfigureAwait(false);
				return CapabilityInvocationResult.Ok(new ConfigEntryReloadResult { Reloaded = reloaded });
			}

			case CapabilityOperations.ConfigEntries.Synchronize:
			{
				if (Arguments<ConfigEntrySynchronizeArguments>(invocation) is not { } arguments)
				{
					return MissingArguments(invocation.Operation);
				}

				var entries = arguments.Entries.Select(ToEntry).ToList();
				var variables = context?.Variables;
				await (variables is null
						? _lifecycle.SynchronizeEntriesAsync(entries, cancellationToken)
						: _lifecycle.SynchronizeEntriesAsync(entries, variables, cancellationToken))
					.ConfigureAwait(false);
				return CapabilityInvocationResult.Ok();
			}

			case CapabilityOperations.ConfigEntries.Release:
			{
				if (Arguments<ConfigEntryReleaseArguments>(invocation) is not { } arguments)
				{
					return MissingArguments(invocation.Operation);
				}

				await _lifecycle.ReleasePreparationAsync(arguments.EntryId, cancellationToken).ConfigureAwait(false);
				return CapabilityInvocationResult.Ok();
			}

			default:
				return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
					$"The config-entries capability has no operation '{invocation.Operation}'.");
		}
	}

	private static StoredConfigEntry ToEntry(ConfigEntryDto dto)
	{
		return new StoredConfigEntry { Id = dto.Id, Title = dto.Title, CreatedAt = dto.CreatedAt, Values = dto.Values };
	}

	private static T? Arguments<T>(CapabilityInvocation invocation)
		where T : class
	{
		try
		{
			return invocation.Arguments?.Deserialize<T>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static CapabilityInvocationResult MissingArguments(string operation)
	{
		return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
			$"The {operation} operation requires valid arguments.");
	}
}
