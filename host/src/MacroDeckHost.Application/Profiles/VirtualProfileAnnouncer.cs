using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Sdk.Profiles;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using TransportProfile = MacroDeckHost.Application.Ui.Transport.Messages.Profiles.Profile;

namespace MacroDeckHost.Application.Profiles;

public sealed class VirtualProfileAnnouncer : IDisposable
{
	private const string RemovedReason = "The widget was removed by its integration.";
	private const string ReconfiguredMessage = "The widget was reconfigured and its view has to be built again.";

	private readonly IProfileRegistry _profiles;
	private readonly IIntegrationRegistry _integrations;
	private readonly IUiTransport _transport;
	private readonly UiSessionRegistry _sessions;
	private readonly WidgetUiProviderRegistry _widgetUiProviders;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Dictionary<string, IReadOnlyList<AnnouncedProfile>> _announced = new(StringComparer.Ordinal);

	public VirtualProfileAnnouncer(
		IProfileRegistry profiles,
		IIntegrationRegistry integrations,
		IUiTransport transport,
		UiSessionRegistry sessions,
		WidgetUiProviderRegistry widgetUiProviders,
		IServiceScopeFactory scopeFactory)
	{
		_profiles = profiles;
		_integrations = integrations;
		_transport = transport;
		_sessions = sessions;
		_widgetUiProviders = widgetUiProviders;
		_scopeFactory = scopeFactory;
	}

	public async Task AnnounceAsync(string integrationId, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(integrationId);

		bool changed;
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var known = _announced.TryGetValue(integrationId, out var previous);
			if (!known && !IsProfileProvider(integrationId))
			{
				return;
			}

			var current = Read(integrationId);
			changed = await SendDifferencesAsync(previous ?? [], current, cancellationToken).ConfigureAwait(false);

			if (current.Count == 0)
			{
				_announced.Remove(integrationId);
			}
			else
			{
				_announced[integrationId] = current;
			}
		}
		finally
		{
			_gate.Release();
		}

		if (!changed)
		{
			return;
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		await scope.ServiceProvider.GetRequiredService<IMediator>()
			.Publish(new VirtualProfilesAnnouncedNotification(integrationId), cancellationToken)
			.ConfigureAwait(false);
	}

	public void Dispose()
	{
		_gate.Dispose();
	}

	private bool IsProfileProvider(string integrationId)
	{
		return _integrations.Integrations.Any(integration => integration is IProfileProvider
			&& string.Equals(integration.Id, integrationId, StringComparison.Ordinal));
	}

	private List<AnnouncedProfile> Read(string integrationId)
	{
		return _profiles.GetProfiles()
			.Where(profile => profile.IsVirtual
				&& string.Equals(profile.SourceIntegrationId, integrationId, StringComparison.Ordinal))
			.Select(profile => new AnnouncedProfile(profile, _profiles.GetFoldersForProfile(profile.Id)))
			.ToList();
	}

	private async Task<bool> SendDifferencesAsync(
		IReadOnlyList<AnnouncedProfile> previous,
		IReadOnlyList<AnnouncedProfile> current,
		CancellationToken cancellationToken)
	{
		var profilesBefore = previous.ToDictionary(p => p.Profile.Id, StringComparer.Ordinal);
		var profilesAfter = current.ToDictionary(p => p.Profile.Id, StringComparer.Ordinal);
		var foldersBefore = FoldersOf(previous);
		var foldersAfter = FoldersOf(current);
		var widgetsBefore = WidgetsOf(previous);
		var widgetsAfter = WidgetsOf(current);
		var changed = false;

		// Profiles go first so a client leaves a removed profile before its start folder disappears.
		foreach (var profileId in profilesBefore.Keys.Where(id => !profilesAfter.ContainsKey(id)))
		{
			await _transport.Send(new ProfileDeletedEvent { ProfileId = profileId }, cancellationToken);
			changed = true;
		}

		var removedWidgets = widgetsBefore.Values.Where(w => !widgetsAfter.ContainsKey(w.Widget.Id)).ToList();
		foreach (var removed in removedWidgets)
		{
			foreach (var session in _sessions.SessionsForWidget(removed.Widget.Id))
			{
				_sessions.TryClose(session.SessionId, RemovedReason);
			}

			_widgetUiProviders.EvictWidget(removed.Widget.Id);
		}

		foreach (var group in removedWidgets.GroupBy(w => w.FolderId, StringComparer.Ordinal))
		{
			await _transport.Send(new WidgetsDeletedEvent
					{ FolderId = group.Key, WidgetIds = [.. group.Select(w => w.Widget.Id)] },
				cancellationToken);
			changed = true;
		}

		foreach (var folder in foldersBefore
			.Where(entry => !foldersAfter.ContainsKey(entry.Key))
			.OrderByDescending(entry => Depth(entry.Key, foldersBefore)))
		{
			await _transport.Send(new FolderDeletedEvent { FolderId = folder.Value.Id }, cancellationToken);
			changed = true;
		}

		foreach (var (profileId, after) in profilesAfter)
		{
			if (!profilesBefore.TryGetValue(profileId, out var before))
			{
				await _transport.Send(new ProfileCreatedEvent { Profile = after.Profile }, cancellationToken);
				changed = true;
			}
			else if (!SameProfile(before.Profile, after.Profile))
			{
				await _transport.Send(new ProfileUpdatedEvent { Profile = after.Profile }, cancellationToken);
				changed = true;
			}
		}

		foreach (var (key, after) in foldersAfter.OrderBy(entry => Depth(entry.Key, foldersAfter)))
		{
			if (!foldersBefore.TryGetValue(key, out var before))
			{
				await _transport.Send(new FolderCreatedEvent { Folder = after }, cancellationToken);
				changed = true;
			}
			else if (!SameFolder(before, after))
			{
				await _transport.Send(new FolderUpdatedEvent { Folder = after }, cancellationToken);
				changed = true;
			}
		}

		foreach (var group in widgetsAfter.Values
			.Where(w => !widgetsBefore.ContainsKey(w.Widget.Id))
			.GroupBy(w => w.FolderId, StringComparer.Ordinal))
		{
			await _transport.Send(new WidgetsCreatedEvent
					{ FolderId = group.Key, Widgets = [.. group.Select(w => w.Widget)] },
				cancellationToken);
			changed = true;
		}

		var updatedWidgets = new List<PlacedWidget>();
		foreach (var after in widgetsAfter.Values)
		{
			if (!widgetsBefore.TryGetValue(after.Widget.Id, out var before) || Same(before.Widget, after.Widget))
			{
				continue;
			}

			if (!string.Equals(before.Widget.Type, after.Widget.Type, StringComparison.Ordinal))
			{
				_widgetUiProviders.EvictWidget(after.Widget.Id);
			}

			foreach (var session in _sessions.SessionsForWidget(after.Widget.Id))
			{
				_sessions.TryInvalidate(session.SessionId,
					UiSessionErrorCodes.WidgetReconfigured,
					ReconfiguredMessage,
					true);
			}

			updatedWidgets.Add(after);
		}

		foreach (var group in updatedWidgets.GroupBy(w => w.FolderId, StringComparer.Ordinal))
		{
			await _transport.Send(new WidgetsUpdatedEvent
					{ FolderId = group.Key, Widgets = [.. group.Select(w => w.Widget)] },
				cancellationToken);
			changed = true;
		}

		return changed;
	}

	private static Dictionary<FolderKey, Folder> FoldersOf(IReadOnlyList<AnnouncedProfile> profiles)
	{
		var folders = new Dictionary<FolderKey, Folder>();
		foreach (var profile in profiles)
		{
			foreach (var folder in profile.Folders)
			{
				folders.TryAdd(new FolderKey(profile.Profile.Id, folder.Id), folder);
			}
		}

		return folders;
	}

	private static Dictionary<string, PlacedWidget> WidgetsOf(IReadOnlyList<AnnouncedProfile> profiles)
	{
		var widgets = new Dictionary<string, PlacedWidget>(StringComparer.Ordinal);
		foreach (var folder in profiles.SelectMany(profile => profile.Folders))
		{
			foreach (var widget in folder.Widgets)
			{
				widgets.TryAdd(widget.Id, new PlacedWidget(folder.Id, widget));
			}
		}

		return widgets;
	}

	private static int Depth(FolderKey key, Dictionary<FolderKey, Folder> folders)
	{
		var depth = 0;
		var seen = new HashSet<string>(StringComparer.Ordinal);
		while (folders.TryGetValue(key, out var folder) && folder.ParentId is { } parentId && seen.Add(parentId))
		{
			depth++;
			key = key with { FolderId = parentId };
		}

		return depth;
	}

	// Order is the position the host assigns after every other profile, so it moves whenever another
	// profile is added or removed without this integration changing anything.
	private static bool SameProfile(TransportProfile before, TransportProfile after)
	{
		return SameWithout(before, after, nameof(TransportProfile.Order));
	}

	private static bool SameFolder(Folder before, Folder after)
	{
		return SameWithout(before, after, nameof(Folder.Widgets));
	}

	private static bool Same(Widget before, Widget after)
	{
		return string.Equals(JsonSerializer.Serialize(before),
			JsonSerializer.Serialize(after),
			StringComparison.Ordinal);
	}

	private static bool SameWithout<T>(T before, T after, string property)
	{
		var left = JsonSerializer.SerializeToNode(before)!.AsObject();
		var right = JsonSerializer.SerializeToNode(after)!.AsObject();
		left.Remove(property);
		right.Remove(property);
		return JsonNode.DeepEquals(left, right);
	}

	private sealed record AnnouncedProfile(TransportProfile Profile, IReadOnlyList<Folder> Folders);

	private readonly record struct FolderKey(string ProfileId, string FolderId);

	private sealed record PlacedWidget(string FolderId, Widget Widget);
}
