using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Localization;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Integrations.ConfigFlow;

public enum IntegrationConfigEntryStatus
{
	Ready,
	Connecting,
	Connected,
	Reconnecting,
	Disconnected,
	NeedsReconfiguration
}

public sealed record IntegrationConfigEntryDescription(
	Guid Id,
	string IntegrationId,
	string Title,
	DateTime CreatedAt,
	IntegrationConfigEntryStatus Status,
	bool Usable);

public sealed record IntegrationConfigMutationPreparation(
	string IntegrationId,
	Guid EntryId,
	string Title,
	IReadOnlyDictionary<string, JsonElement> Values,
	ConfigEntryRecord? Existing,
	IReadOnlyList<ConfigEntryRecord> Siblings,
	bool TitleChanged);

public sealed record IntegrationConfigMutationPreparationResult(
	bool Success,
	IReadOnlyDictionary<string, JsonElement> Values,
	LocalizedText Error = default);

public sealed record IntegrationConfigMutationOutcome(
	bool Success,
	Guid? EntryId = null,
	LocalizedText Error = default);

public interface IIntegrationConfigMutationCoordinator
{
	Task<IntegrationConfigMutationOutcome> CompleteAsync(
		string integrationId,
		Guid entryId,
		string title,
		IReadOnlyDictionary<string, JsonElement> values,
		CancellationToken cancellationToken);

	Task<IntegrationConfigMutationOutcome> RenameAsync(
		string integrationId,
		Guid entryId,
		string title,
		CancellationToken cancellationToken);

	Task<IntegrationConfigMutationOutcome> DeleteAsync(
		string integrationId,
		Guid entryId,
		bool confirmed,
		CancellationToken cancellationToken);

	Task<IReadOnlyList<IntegrationConfigEntryDescription>> DescribeAsync(
		string integrationId,
		CancellationToken cancellationToken);

	Task<IntegrationConfigMutationOutcome> DeleteWithoutStoringDisabledAsync(
		string integrationId,
		Guid entryId,
		CancellationToken cancellationToken)
	{
		return DeleteAsync(integrationId, entryId, true, cancellationToken);
	}

	Task<IntegrationConfigMutationOutcome> CompleteUnlessDisabledAsync(
		string integrationId,
		Guid entryId,
		string title,
		IReadOnlyDictionary<string, JsonElement> values,
		CancellationToken cancellationToken)
	{
		return CompleteAsync(integrationId, entryId, title, values, cancellationToken);
	}
}

public sealed class IntegrationConfigMutationCoordinator : IIntegrationConfigMutationCoordinator
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IIntegrationLifecycle _lifecycle;
	private readonly IIntegrationRegistry _registry;
	private readonly IMediator _mediator;
	private readonly IVariablePollingInvalidationSignal _variableInvalidation;
	private readonly ILogger _logger;
	private readonly VariableRegistry? _variableRegistry;
	private readonly IIntegrationVariableApiFactory? _variableApis;
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<Guid, Guid> _lifecycleReservations = new();

	public IntegrationConfigMutationCoordinator(
		IServiceScopeFactory scopeFactory,
		IIntegrationLifecycle lifecycle,
		IIntegrationRegistry registry,
		IMediator mediator,
		IVariablePollingInvalidationSignal variableInvalidation,
		ILogger logger,
		VariableRegistry? variableRegistry = null,
		IIntegrationVariableApiFactory? variableApis = null)
	{
		_variableRegistry = variableRegistry;
		_variableApis = variableApis;
		_scopeFactory = scopeFactory;
		_lifecycle = lifecycle;
		_registry = registry;
		_mediator = mediator;
		_variableInvalidation = variableInvalidation;
		_logger = logger;
	}

	public Task<IntegrationConfigMutationOutcome> CompleteAsync(
		string integrationId,
		Guid entryId,
		string title,
		IReadOnlyDictionary<string, JsonElement> values,
		CancellationToken cancellationToken)
	{
		return Complete(integrationId, entryId, title, values, false, cancellationToken);
	}

	// Checked in the same serialized section as a user's delete, so an entry the host creates on its own
	// never follows the deletion of the last one.
	public Task<IntegrationConfigMutationOutcome> CompleteUnlessDisabledAsync(
		string integrationId,
		Guid entryId,
		string title,
		IReadOnlyDictionary<string, JsonElement> values,
		CancellationToken cancellationToken)
	{
		return Complete(integrationId, entryId, title, values, true, cancellationToken);
	}

	private Task<IntegrationConfigMutationOutcome> Complete(
		string integrationId,
		Guid entryId,
		string title,
		IReadOnlyDictionary<string, JsonElement> values,
		bool unlessDisabled,
		CancellationToken cancellationToken)
	{
		return Serialized(integrationId,
			async store =>
			{
				if (unlessDisabled && _registry.IsExplicitlyDisabled(integrationId))
				{
					return new IntegrationConfigMutationOutcome(false);
				}

				var existing = await store.Find(entryId);
				if (existing is not null && !OwnedBy(existing, integrationId))
				{
					return MissingEntry();
				}

				var siblings = await LoadRecords(store, integrationId);
				var prepared = await PrepareAsync(integrationId,
					entryId,
					title,
					values,
					existing,
					siblings.Where(entry => entry.Id != entryId).ToList(),
					false,
					cancellationToken);
				if (!prepared.Success)
				{
					return new IntegrationConfigMutationOutcome(false, Error: prepared.Error);
				}

				try
				{
					var stored = existing is null
						? await store.Create(entryId, integrationId, title, prepared.Values)
						: await store.Replace(entryId, title, prepared.Values);
					if (!stored)
					{
						return MissingEntry();
					}

					_registry.SetEnabled(integrationId, true);
					await ReloadAndPublish(integrationId, cancellationToken);
					await SynchronizeVariables(integrationId,
						await LoadRecords(store, integrationId),
						cancellationToken);
					return new IntegrationConfigMutationOutcome(true, entryId);
				}
				finally
				{
					await ReleasePreparationAsync(integrationId, entryId, cancellationToken);
				}
			},
			cancellationToken);
	}

	public Task<IntegrationConfigMutationOutcome> RenameAsync(
		string integrationId,
		Guid entryId,
		string title,
		CancellationToken cancellationToken)
	{
		return Serialized(integrationId,
			async store =>
			{
				var existing = await store.Find(entryId);
				if (!OwnedBy(existing, integrationId))
				{
					return MissingEntry();
				}

				// A title-only mutation must never promote legacy configuration data into the
				// current schema. Only completing the configuration flow may do that.
				if (!await IsUsableAsync(integrationId, existing!, cancellationToken))
				{
					if (!await store.Rename(entryId, title, existing!.Values))
					{
						return MissingEntry();
					}

					await ReloadAndPublish(integrationId, cancellationToken);
					await SynchronizeVariables(integrationId,
						await LoadRecords(store, integrationId),
						cancellationToken);
					return new IntegrationConfigMutationOutcome(true, entryId);
				}

				var siblings = await LoadRecords(store, integrationId);
				var prepared = await PrepareAsync(integrationId,
					entryId,
					title,
					existing!.Values,
					existing,
					siblings.Where(entry => entry.Id != entryId).ToList(),
					!string.Equals(title, existing.Title, StringComparison.Ordinal),
					cancellationToken);
				if (!prepared.Success)
				{
					return new IntegrationConfigMutationOutcome(false, Error: prepared.Error);
				}

				try
				{
					if (!await store.Rename(entryId, title, prepared.Values))
					{
						return MissingEntry();
					}

					await ReloadAndPublish(integrationId, cancellationToken);
					await SynchronizeVariables(integrationId,
						await LoadRecords(store, integrationId),
						cancellationToken);
					return new IntegrationConfigMutationOutcome(true, entryId);
				}
				finally
				{
					await ReleasePreparationAsync(integrationId, entryId, cancellationToken);
				}
			},
			cancellationToken);
	}

	public Task<IntegrationConfigMutationOutcome> DeleteAsync(
		string integrationId,
		Guid entryId,
		bool confirmed,
		CancellationToken cancellationToken)
	{
		if (!confirmed)
		{
			return Task.FromResult(new IntegrationConfigMutationOutcome(false,
				Error: AppStrings.Errors.Config.DeleteConfirmationRequired()));
		}

		return Delete(integrationId, entryId, true, cancellationToken);
	}

	// For an entry the host removes on its own: nobody chose "off", so only a stored "on" is cleared, and
	// an "off" a user stores meanwhile survives.
	public Task<IntegrationConfigMutationOutcome> DeleteWithoutStoringDisabledAsync(
		string integrationId,
		Guid entryId,
		CancellationToken cancellationToken)
	{
		return Delete(integrationId, entryId, false, cancellationToken);
	}

	private Task<IntegrationConfigMutationOutcome> Delete(
		string integrationId,
		Guid entryId,
		bool storeDisabled,
		CancellationToken cancellationToken)
	{
		return Serialized(integrationId,
			async store =>
			{
				var existing = await store.Find(entryId);
				if (!OwnedBy(existing, integrationId))
				{
					return MissingEntry();
				}

				await store.Delete(entryId);
				var remaining = await LoadRecords(store, integrationId);
				await SynchronizeVariables(integrationId, remaining, cancellationToken);
				if (RequiresConfiguration(integrationId)
					&& !await AnyUsableAsync(integrationId, remaining, cancellationToken))
				{
					if (storeDisabled)
					{
						_registry.SetEnabled(integrationId, false);
					}
					else
					{
						_registry.ClearEnabledChoice(integrationId);
					}

					await _lifecycle.ShutdownAsync(integrationId, cancellationToken);
					await _mediator.Publish(new IntegrationStateChangedNotification(integrationId), cancellationToken);
					_variableInvalidation.MarkStale(integrationId);
				}
				else
				{
					await ReloadAndPublish(integrationId, cancellationToken);
				}

				return new IntegrationConfigMutationOutcome(true, entryId);
			},
			cancellationToken);
	}

	public async Task<IReadOnlyList<IntegrationConfigEntryDescription>> DescribeAsync(
		string integrationId,
		CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var store = scope.ServiceProvider.GetRequiredService<IIntegrationConfigStore>();
		var entries = await LoadRecords(store, integrationId);

		// A plugin answers each of these over the wire; asked one after another, a stalled plugin would cost
		// one timeout per question and entry instead of one in total.
		return await Task.WhenAll(entries.Select(entry => DescribeAsync(integrationId, entry, cancellationToken)));
	}

	private async Task<IntegrationConfigEntryDescription> DescribeAsync(
		string integrationId,
		ConfigEntryRecord entry,
		CancellationToken cancellationToken)
	{
		var status = StatusAsync(integrationId, entry, cancellationToken);
		var usable = IsUsableAsync(integrationId, entry, cancellationToken);
		await Task.WhenAll(status, usable);

		return new IntegrationConfigEntryDescription(entry.Id,
			entry.IntegrationId,
			entry.Title,
			entry.CreatedAt,
			await status,
			await usable);
	}

	private async Task<IntegrationConfigMutationOutcome> Serialized(
		string integrationId,
		Func<IIntegrationConfigStore, Task<IntegrationConfigMutationOutcome>> operation,
		CancellationToken cancellationToken)
	{
		var gate = _gates.GetOrAdd(integrationId, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken);
		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();
			return await operation(scope.ServiceProvider.GetRequiredService<IIntegrationConfigStore>());
		}
		finally
		{
			gate.Release();
		}
	}

	private LifecycleConfigEntryBinding? Binding(string integrationId)
	{
		return _registry.Integrations.FirstOrDefault(candidate =>
			string.Equals(candidate.Id, integrationId, StringComparison.Ordinal)) is IConfigEntryLifecycle lifecycle
			? new LifecycleConfigEntryBinding(integrationId, lifecycle, _variableRegistry, _lifecycleReservations)
			: null;
	}

	private Task<IntegrationConfigMutationPreparationResult> PrepareAsync(
		string integrationId,
		Guid entryId,
		string title,
		IReadOnlyDictionary<string, JsonElement> values,
		ConfigEntryRecord? existing,
		IReadOnlyList<ConfigEntryRecord> siblings,
		bool titleChanged,
		CancellationToken cancellationToken)
	{
		return Binding(integrationId) is { } binding
			? binding.PrepareAsync(new IntegrationConfigMutationPreparation(integrationId,
					entryId,
					title,
					values,
					existing,
					siblings,
					titleChanged),
				cancellationToken)
			: Task.FromResult(new IntegrationConfigMutationPreparationResult(true, values));
	}

	private async Task SynchronizeVariables(
		string integrationId,
		IReadOnlyList<ConfigEntryRecord> entries,
		CancellationToken cancellationToken)
	{
		if (Binding(integrationId) is not { } binding)
		{
			return;
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		await binding.SynchronizeAsync(entries,
			_variableApis?.Create(integrationId, scope.ServiceProvider),
			cancellationToken);
	}

	private Task ReleasePreparationAsync(string integrationId, Guid entryId, CancellationToken cancellationToken)
	{
		return Binding(integrationId)?.ReleasePreparationAsync(entryId, cancellationToken) ?? Task.CompletedTask;
	}

	private async Task ReloadAndPublish(string integrationId, CancellationToken cancellationToken)
	{
		var reloaded = false;
		var integration = _registry.Integrations.FirstOrDefault(candidate =>
			string.Equals(candidate.Id, integrationId, StringComparison.Ordinal));
		if (integration is { IsInitialized: true } && Binding(integrationId) is { } binding)
		{
			try
			{
				reloaded = await binding.ReloadAsync(cancellationToken);
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "Incremental configuration reload failed for '{IntegrationId}'", integrationId);
			}
		}

		if (!reloaded)
		{
			await _lifecycle.ReinitializeAsync(integrationId, cancellationToken);
			return;
		}

		_variableInvalidation.MarkStale(integrationId);
		await _mediator.Publish(new IntegrationStateChangedNotification(integrationId), cancellationToken);
	}

	private Task<IntegrationConfigEntryStatus> StatusAsync(
		string integrationId,
		ConfigEntryRecord entry,
		CancellationToken cancellationToken)
	{
		return Binding(integrationId)?.GetStatusAsync(entry, cancellationToken)
			?? Task.FromResult(IntegrationConfigEntryStatus.Ready);
	}

	private bool RequiresConfiguration(string integrationId)
	{
		return _registry.Integrations.FirstOrDefault(candidate =>
				string.Equals(candidate.Id, integrationId, StringComparison.Ordinal)) is not { } integration
			|| integration.RequiresConfiguration();
	}

	private Task<bool> IsUsableAsync(string integrationId, ConfigEntryRecord entry, CancellationToken cancellationToken)
	{
		return Binding(integrationId)?.IsUsableAsync(entry, cancellationToken) ?? Task.FromResult(true);
	}

	private async Task<bool> AnyUsableAsync(
		string integrationId,
		IReadOnlyList<ConfigEntryRecord> entries,
		CancellationToken cancellationToken)
	{
		foreach (var entry in entries)
		{
			if (await IsUsableAsync(integrationId, entry, cancellationToken))
			{
				return true;
			}
		}

		return false;
	}

	private static async Task<IReadOnlyList<ConfigEntryRecord>> LoadRecords(
		IIntegrationConfigStore store,
		string integrationId)
	{
		var summaries = await store.List(integrationId);
		var entries = new List<ConfigEntryRecord>(summaries.Count);
		foreach (var summary in summaries)
		{
			if (await store.Find(summary.Id) is { } entry && OwnedBy(entry, integrationId))
			{
				entries.Add(entry);
			}
		}

		return entries;
	}

	private static bool OwnedBy(ConfigEntryRecord? entry, string integrationId)
	{
		return entry is not null && string.Equals(entry.IntegrationId, integrationId, StringComparison.Ordinal);
	}

	private static IntegrationConfigMutationOutcome MissingEntry()
	{
		return new IntegrationConfigMutationOutcome(false, Error: AppStrings.Errors.Config.EntryNotFound());
	}
}
