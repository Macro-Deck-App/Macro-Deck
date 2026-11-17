using System.Globalization;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.System.Configuration;
using MacroDeckHost.Integrations.System.State;
using MacroDeckHost.Integrations.System.Variables;
using MacroDeckHost.Platform.Applications;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.System;

namespace MacroDeckHost.Integrations.System;

public sealed partial class SystemIntegration
{
	public Task<ConfigEntryPreparationResult> PrepareEntryAsync(
		ConfigEntryPreparation preparation,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(preparation);

		if (preparation.Existing is null
			&& preparation.Siblings.Count >= SystemConfigKeys.MaximumWatchedApplications)
		{
			return Task.FromResult(ConfigEntryPreparationResult.Refuse(Strings.Config.EntryLimitReached(
				SystemConfigKeys.MaximumWatchedApplications.ToString(CultureInfo.CurrentCulture))));
		}

		var key = ConfigurationIdentityPreparation.ChooseKey(preparation,
			ReadIdentity,
			candidate => ApplicationVariables.Declare(candidate, preparation.EntryId),
			null);
		if (key is null)
		{
			return Task.FromResult(ConfigEntryPreparationResult.Refuse(Strings.Config.VariableIdentityExhausted()));
		}

		var values = new Dictionary<string, JsonElement>(preparation.Values, StringComparer.Ordinal)
		{
			[SystemConfigKeys.VariableIdentityKey] = JsonSerializer.SerializeToElement(
				ConfigurationIdentityJson.Serialize(new ConfigurationIdentity(preparation.Title, key)))
		};

		return Task.FromResult(ConfigEntryPreparationResult.Accept(values,
				ApplicationVariables.Declare(key, preparation.EntryId, preparation.Title)) with
			{
				RetryOnConflict = true
			});
	}

	public Task<bool> IsEntryUsableAsync(StoredConfigEntry entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);
		return Task.FromResult(ReadIdentity(entry) is not null);
	}

	public async Task<bool> ReloadEntriesAsync(CancellationToken cancellationToken = default)
	{
		if (_context is null)
		{
			return false;
		}

		await LoadWatchedApplicationsAsync(cancellationToken);
		return true;
	}

	public Task SynchronizeEntriesAsync(
		IReadOnlyList<StoredConfigEntry> entries,
		CancellationToken cancellationToken = default)
	{
		return _context is { } context
			? SynchronizeEntriesAsync(entries, context.Variables, cancellationToken)
			: Task.CompletedTask;
	}

	public Task SynchronizeEntriesAsync(
		IReadOnlyList<StoredConfigEntry> entries,
		IVariableApi variables,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entries);
		ArgumentNullException.ThrowIfNull(variables);

		return ConfigurationIdentityPreparation.SynchronizeAsync(variables,
			[
				.. entries.Select(entry => (Entry: entry, Identity: ReadIdentity(entry)))
					.Where(item => item.Identity is not null)
					.Select(item => (item.Entry, item.Identity!))
			],
			definitionId => ApplicationVariables.TrySplit(definitionId, out var entryId, out _) ? entryId : null,
			(key, entryId, title) => ApplicationVariables.Declare(key, entryId, title),
			cancellationToken);
	}

	private static ConfigurationIdentity? ReadIdentity(StoredConfigEntry entry)
	{
		return ConfigurationIdentityPreparation.ReadIdentity(entry, SystemConfigKeys.VariableIdentityKey);
	}

	private async Task LoadWatchedApplicationsAsync(CancellationToken cancellationToken)
	{
		if (_context is not { } context)
		{
			return;
		}

		List<WatchedApplication> watched;
		try
		{
			watched = await ReadWatchedApplicationsAsync(context.Config, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
		{
			_logger.Warning(ex, "Could not read the watched applications");
			watched = [];
		}

		lock (_refreshLock)
		{
			if (watched.SequenceEqual(_watched))
			{
				return;
			}

			_watched = watched;
			ComposeVariables();
		}

		NotifyDeclarationsChanged();
	}

	private static async Task<List<WatchedApplication>> ReadWatchedApplicationsAsync(
		IIntegrationConfig config,
		CancellationToken cancellationToken)
	{
		var entries = await config.GetEntriesAsync(cancellationToken);
		var watched = new List<WatchedApplication>(entries.Count);
		foreach (var entry in entries.Take(SystemConfigKeys.MaximumWatchedApplications))
		{
			var identity = ConfigurationIdentityJson.TryParse(
				await config.GetStringAsync(entry.Id, SystemConfigKeys.VariableIdentityKey, cancellationToken),
				out var parsed)
				? parsed
				: null;
			var application = await config.GetStringAsync(entry.Id, SystemConfigKeys.Application, cancellationToken);
			if (identity is null || string.IsNullOrWhiteSpace(application))
			{
				_logger.Warning("Watched application entry {EntryId} is incomplete; skipping", entry.Id);
				continue;
			}

			watched.Add(new WatchedApplication(entry.Id, entry.Title, identity.Key, application.Trim()));
		}

		return watched;
	}

	private async Task RunApplicationSampleLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				SampleApplications();
				await Task.Delay(_applicationSampleInterval, cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	internal void SampleApplications()
	{
		var watched = _watched;
		var focus = _latestFocus;
		ApplicationSamples samples;
		try
		{
			samples = _applicationSampler.Sample(watched, focus);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			_logger.Warning(ex, "Could not sample the watched applications");
			return;
		}

		var previous = _applicationSamples;
		_applicationSamples = samples;

		var changed = new List<string>();
		foreach (var (entryId, sample) in samples.Entries)
		{
			if (!previous.Entries.TryGetValue(entryId, out var before) || before != sample)
			{
				changed.AddRange(ApplicationVariables.IdsOf(entryId));
			}
		}

		RequestDefinitionRefresh(changed);

		if (focus is not null && samples.Focused != _writtenFocusSample)
		{
			_focusChannel?.Writer.TryWrite(true);
		}
	}

	private async Task StopApplicationSamplingAsync()
	{
		if (_applicationSampleCancellation is not { } cancellation)
		{
			return;
		}

		await cancellation.CancelAsync();
		if (_applicationSampleLoop is { } loop)
		{
			await loop;
		}

		cancellation.Dispose();
		_applicationSampleCancellation = null;
		_applicationSampleLoop = null;
	}

	private VariableReading ReadApplication(Guid entryId, string slot)
	{
		if (_watched.All(application => application.EntryId != entryId))
		{
			return VariableReading.Unavailable;
		}

		return ApplicationVariables.Read(_applicationSamples.Entries.GetValueOrDefault(entryId), slot);
	}

	private IntegrationIssue? WindowStateIssue()
	{
		if (_watched.Count == 0)
		{
			return null;
		}

		var availability = _applicationSampler.Availability;
		LocalizedText description;
		if (availability == ApplicationWindowAvailability.Wayland)
		{
			description = Strings.Issues.WindowStateWaylandDescription();
		}
		else if (availability == ApplicationWindowAvailability.AccessibilityNotGranted)
		{
			description = Strings.Issues.WindowStateAccessibilityDescription();
		}
		else
		{
			return null;
		}

		return new IntegrationIssue
		{
			Id = WindowStateIssueId,
			Title = Strings.Issues.WindowStateUnavailableTitle(),
			Description = description,
			Severity = IntegrationIssueSeverity.Warning,
			ActionLabel = availability == ApplicationWindowAvailability.AccessibilityNotGranted
				? Strings.Issues.GrantAccessibilityAction()
				: default
		};
	}
}
