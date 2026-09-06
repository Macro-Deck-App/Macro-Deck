using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Localization;
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

public interface IIntegrationConfigMutationAdapter
{
	string IntegrationId { get; }

	IntegrationConfigMutationPreparationResult Prepare(IntegrationConfigMutationPreparation preparation);

	IntegrationConfigEntryStatus GetStatus(ConfigEntryRecord entry);

	bool IsUsable(ConfigEntryRecord entry);

	Task ReloadAsync(CancellationToken cancellationToken);

	Task SynchronizeVariablesAsync(
		IReadOnlyList<ConfigEntryRecord> entries,
		CancellationToken cancellationToken)
		=> Task.CompletedTask;

	void ReleasePreparation(Guid entryId)
	{
	}
}

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
}

public sealed class IntegrationConfigMutationCoordinator : IIntegrationConfigMutationCoordinator
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IIntegrationLifecycle _lifecycle;
	private readonly IIntegrationRegistry _registry;
	private readonly IMediator _mediator;
	private readonly IVariablePollingInvalidationSignal _variableInvalidation;
	private readonly Dictionary<string, IIntegrationConfigMutationAdapter> _adapters;
	private readonly ILogger _logger;
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

	public IntegrationConfigMutationCoordinator(
		IServiceScopeFactory scopeFactory,
		IIntegrationLifecycle lifecycle,
		IIntegrationRegistry registry,
		IMediator mediator,
		IVariablePollingInvalidationSignal variableInvalidation,
		IEnumerable<IIntegrationConfigMutationAdapter> adapters,
		ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_lifecycle = lifecycle;
		_registry = registry;
		_mediator = mediator;
		_variableInvalidation = variableInvalidation;
		_adapters = adapters.ToDictionary(adapter => adapter.IntegrationId, StringComparer.Ordinal);
		_logger = logger;
	}

	public Task<IntegrationConfigMutationOutcome> CompleteAsync(
		string integrationId,
		Guid entryId,
		string title,
		IReadOnlyDictionary<string, JsonElement> values,
		CancellationToken cancellationToken)
		=> Serialized(integrationId,
			async store =>
			{
				var existing = await store.Find(entryId);
				if (existing is not null && !OwnedBy(existing, integrationId))
				{
					return MissingEntry();
				}

				var siblings = await LoadRecords(store, integrationId);
				var prepared = Prepare(integrationId,
					entryId,
					title,
					values,
					existing,
					siblings.Where(entry => entry.Id != entryId).ToList(),
					false);
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
					ReleasePreparation(integrationId, entryId);
				}
			},
			cancellationToken);

	public Task<IntegrationConfigMutationOutcome> RenameAsync(
		string integrationId,
		Guid entryId,
		string title,
		CancellationToken cancellationToken)
		=> Serialized(integrationId,
			async store =>
			{
				var existing = await store.Find(entryId);
				if (!OwnedBy(existing, integrationId))
				{
					return MissingEntry();
				}

				// A title-only mutation must never promote legacy configuration data into the
				// current schema. Only completing the configuration flow may do that.
				if (!IsUsable(integrationId, existing!))
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
				var prepared = Prepare(integrationId,
					entryId,
					title,
					existing!.Values,
					existing,
					siblings.Where(entry => entry.Id != entryId).ToList(),
					!string.Equals(title, existing.Title, StringComparison.Ordinal));
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
					ReleasePreparation(integrationId, entryId);
				}
			},
			cancellationToken);

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
				if (!remaining.Any(entry => IsUsable(integrationId, entry)))
				{
					_registry.SetEnabled(integrationId, false);
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
		return entries.Select(entry => new IntegrationConfigEntryDescription(entry.Id,
			entry.IntegrationId,
			entry.Title,
			entry.CreatedAt,
			Status(integrationId, entry),
			IsUsable(integrationId, entry))).ToList();
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

	private IntegrationConfigMutationPreparationResult Prepare(
		string integrationId,
		Guid entryId,
		string title,
		IReadOnlyDictionary<string, JsonElement> values,
		ConfigEntryRecord? existing,
		IReadOnlyList<ConfigEntryRecord> siblings,
		bool titleChanged)
		=> _adapters.TryGetValue(integrationId, out var adapter)
			? adapter.Prepare(new IntegrationConfigMutationPreparation(integrationId,
				entryId,
				title,
				values,
				existing,
				siblings,
				titleChanged))
			: new IntegrationConfigMutationPreparationResult(true, values);

	private Task SynchronizeVariables(
		string integrationId,
		IReadOnlyList<ConfigEntryRecord> entries,
		CancellationToken cancellationToken)
		=> _adapters.TryGetValue(integrationId, out var adapter)
			? adapter.SynchronizeVariablesAsync(entries, cancellationToken)
			: Task.CompletedTask;

	private void ReleasePreparation(string integrationId, Guid entryId)
	{
		if (_adapters.TryGetValue(integrationId, out var adapter))
		{
			adapter.ReleasePreparation(entryId);
		}
	}

	private async Task ReloadAndPublish(string integrationId, CancellationToken cancellationToken)
	{
		var reloaded = false;
		var integration = _registry.Integrations.FirstOrDefault(candidate =>
			string.Equals(candidate.Id, integrationId, StringComparison.Ordinal));
		if (integration is { IsInitialized: true } &&
			_adapters.TryGetValue(integrationId, out var adapter))
		{
			try
			{
				await adapter.ReloadAsync(cancellationToken);
				reloaded = true;
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

	private IntegrationConfigEntryStatus Status(string integrationId, ConfigEntryRecord entry)
		=> _adapters.TryGetValue(integrationId, out var adapter)
			? adapter.GetStatus(entry)
			: IntegrationConfigEntryStatus.Ready;

	private bool IsUsable(string integrationId, ConfigEntryRecord entry)
		=> !_adapters.TryGetValue(integrationId, out var adapter) || adapter.IsUsable(entry);

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
		=> entry is not null && string.Equals(entry.IntegrationId, integrationId, StringComparison.Ordinal);

	private static IntegrationConfigMutationOutcome MissingEntry()
		=> new(false, Error: AppStrings.Errors.Config.EntryNotFound());
}
