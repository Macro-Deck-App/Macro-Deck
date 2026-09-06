using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Devices.Surfaces;

public sealed class DeviceSurfaceService : IDeviceSurfaceService, IDeviceSurfaceRenderSink, IDisposable
{
	// Long enough for a bulk move to arrive as one rebuild, short enough that a single edit still lands
	// as promptly as a client push does.
	private static readonly TimeSpan _rebuildDebounce = TimeSpan.FromMilliseconds(50);

	private readonly ConcurrentDictionary<Guid, DeviceSurfaceSession> _sessions = new();

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IDeviceSurfaceProviderResolver _providers;
	private readonly IProfileRegistry _profiles;
	private readonly ProviderDevicePresenceTracker _providerPresence;
	private readonly DeviceInteractionRouter _interactions;
	private readonly IApplicationFocusCoordinator _focus;
	private readonly IEventBus _bus;
	private readonly WidgetStateSubscriptionTracker _widgetStateSubscriptions;
	private readonly LabelSubscriptionTracker _labelSubscriptions;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public DeviceSurfaceService(
		IServiceScopeFactory scopeFactory,
		IDeviceSurfaceProviderResolver providers,
		IProfileRegistry profiles,
		ProviderDevicePresenceTracker providerPresence,
		DeviceInteractionRouter interactions,
		IApplicationFocusCoordinator focus,
		IEventBus bus,
		WidgetStateSubscriptionTracker widgetStateSubscriptions,
		LabelSubscriptionTracker labelSubscriptions,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_providers = providers;
		_profiles = profiles;
		_providerPresence = providerPresence;
		_interactions = interactions;
		_focus = focus;
		_bus = bus;
		_widgetStateSubscriptions = widgetStateSubscriptions;
		_labelSubscriptions = labelSubscriptions;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<DeviceSurfaceService>();
	}

	public bool IsOpen(Guid deviceId) => _sessions.ContainsKey(deviceId);

	public async Task OpenAsync(
		Guid deviceId,
		string providerId,
		string providerDeviceId,
		CancellationToken cancellationToken = default)
	{
		if (_sessions.ContainsKey(deviceId))
		{
			return;
		}

		if (_providers.Resolve(providerId) is not { } provider)
		{
			return;
		}

		var session = new DeviceSurfaceSession(deviceId, providerId, providerDeviceId, provider)
		{
			// A device registered while unreachable gets its session and its surface built, but is not
			// pushed to until the provider reports it back online.
			Online = _providerPresence.IsOnline(deviceId)
		};
		session.Presses = new DeviceSurfacePressTracker(_timeProvider,
			(widgetId, triggerType) => _interactions.ExecuteTriggerAsync(session, widgetId, triggerType));

		if (!_sessions.TryAdd(deviceId, session))
		{
			session.Dispose();
			return;
		}

		try
		{
			var accepted = await provider.OpenAsync(new DeviceSurfaceSessionDescriptor(deviceId.ToString(),
					providerDeviceId),
				cancellationToken);
			if (!accepted)
			{
				await DiscardAsync(session, reason: null, notifyProvider: false);
				return;
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception,
				"Provider '{ProviderId}' failed to open a session for device {DeviceId}",
				providerId,
				deviceId);
			await DiscardAsync(session, reason: null, notifyProvider: false);
			return;
		}

		// The first surface is always delivered, complete and at revision 1, even when it is the empty
		// one: a session that never pushed would leave the provider with nothing to render.
		await RebuildAsync(session, force: true, cancellationToken);
	}

	public async Task CloseAsync(Guid deviceId, string? reason, CancellationToken cancellationToken = default)
	{
		if (!_sessions.TryGetValue(deviceId, out var session))
		{
			return;
		}

		await DiscardAsync(session, reason, notifyProvider: true);
	}

	public async Task<bool> NavigateAsync(
		Guid deviceId,
		DeckNavigationCommand command,
		CancellationToken cancellationToken = default)
	{
		if (!_sessions.TryGetValue(deviceId, out var session))
		{
			return false;
		}

		string? previousFolderId;
		DeviceSurfaceProjection projection;

		await session.Gate.WaitAsync(cancellationToken);
		try
		{
			previousFolderId = session.FolderId;
			if (!TryApplyNavigation(session, command))
			{
				return false;
			}

			projection = await ProjectAsync(session, cancellationToken);
			await ApplyAsync(session, projection, cancellationToken);
		}
		finally
		{
			session.Gate.Release();
		}

		if (string.Equals(previousFolderId, session.FolderId, StringComparison.Ordinal))
		{
			return true;
		}

		// Deliberately outside the gate: reporting the folder can drive a focus rule straight back into
		// NavigateAsync for the same session.
		if (session.Presses is { } presses)
		{
			await presses.CancelAllAsync();
		}

		await ReportFolderChanged(session, projection, command, cancellationToken);
		return true;
	}

	public Task<DeviceInteractionOutcome> SubmitInteractionAsync(
		Guid deviceId,
		DeviceInteraction interaction,
		CancellationToken cancellationToken = default)
		=> _sessions.TryGetValue(deviceId, out var session)
			? _interactions.SubmitAsync(session, interaction, cancellationToken)
			: Task.FromResult(DeviceInteractionOutcome.Reject(DeviceSurfaceErrorCodes.SessionNotFound));

	public async Task<DeviceIconImage?> GetIconAsync(
		Guid deviceId,
		string iconId,
		int? size = null,
		string? knownETag = null,
		CancellationToken cancellationToken = default)
	{
		if (!_sessions.ContainsKey(deviceId) || !Guid.TryParse(iconId, out var id))
		{
			return null;
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var icons = scope.ServiceProvider.GetRequiredService<IIconService>();

		// Never null: a null size resolves to the unbounded master variant, which a key-sized device does
		// not want and which does not fit ProtocolLimits.MaxAssetBytes.
		var requestedSize = size ?? IconVariants.TargetSizes.Max();

		var result = await icons.GetImage(id, requestedSize, acceptWebp: true, staticFrame: false, cancellationToken);
		if (!result.Success || result.Data is not { } image)
		{
			return null;
		}

		await using var content = image.Content;
		if (knownETag is { Length: > 0 } && string.Equals(knownETag, image.ETag, StringComparison.Ordinal))
		{
			return new DeviceIconImage
			{
				IconId = iconId,
				ContentType = image.ContentType,
				ETag = image.ETag,
				Content = ReadOnlyMemory<byte>.Empty,
				NotModified = true
			};
		}

		using var buffer = new MemoryStream();
		await content.CopyToAsync(buffer, cancellationToken);

		if (buffer.Length > ProtocolLimits.MaxAssetBytes)
		{
			throw new DeviceSessionException(DeviceSessionReasons.IconTooLarge,
				$"Icon '{iconId}' is {buffer.Length} bytes at size {requestedSize}, over the " +
				$"{ProtocolLimits.MaxAssetBytes} byte transfer limit.");
		}

		return new DeviceIconImage
		{
			IconId = iconId,
			ContentType = image.ContentType,
			ETag = image.ETag,
			Content = buffer.ToArray(),
			NotModified = false
		};
	}

	public async Task<DeviceWidgetIconImage?> GetWidgetIconAsync(
		Guid deviceId,
		string widgetId,
		string? knownETag = null,
		CancellationToken cancellationToken = default)
	{
		if (!_sessions.TryGetValue(deviceId, out var session) ||
			session.LastPushed is not { } surface ||
			surface.Widgets.All(widget => !string.Equals(widget.Id, widgetId, StringComparison.Ordinal)) ||
			!Guid.TryParse(widgetId, out var id))
		{
			return null;
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var icons = scope.ServiceProvider.GetRequiredService<IWidgetIconService>();
		var resources = scope.ServiceProvider.GetRequiredService<IUiResourceStore>();

		var resolution = await icons.Resolve(id, cancellationToken);
		if (resolution is not { IsActive: true, Resource: { } resource } ||
			!resources.TryGet(resource.ResourceId, out var content))
		{
			return null;
		}

		if (knownETag is { Length: > 0 } && string.Equals(knownETag, content.ContentHash, StringComparison.Ordinal))
		{
			return new DeviceWidgetIconImage
			{
				WidgetId = widgetId,
				ContentType = content.MediaType,
				ETag = content.ContentHash,
				Content = ReadOnlyMemory<byte>.Empty,
				NotModified = true
			};
		}

		return new DeviceWidgetIconImage
		{
			WidgetId = widgetId,
			ContentType = content.MediaType,
			ETag = content.ContentHash,
			Content = content.Content,
			NotModified = false
		};
	}

	public Task InvalidateAsync(CancellationToken cancellationToken = default)
	{
		foreach (var session in _sessions.Values)
		{
			ScheduleRebuild(session);
		}

		return Task.CompletedTask;
	}

	public Task InvalidateAsync(Guid deviceId, CancellationToken cancellationToken = default)
	{
		if (_sessions.TryGetValue(deviceId, out var session))
		{
			ScheduleRebuild(session);
		}

		return Task.CompletedTask;
	}

	public async Task SetPresenceAsync(Guid deviceId, bool online, CancellationToken cancellationToken = default)
	{
		if (!_sessions.TryGetValue(deviceId, out var session) || session.Online == online)
		{
			return;
		}

		session.Online = online;
		if (!online)
		{
			// A device that vanished mid-press must not leave a press open, and must not be handed a
			// long press when it comes back.
			if (session.Presses is { } presses)
			{
				await presses.CancelAllAsync();
			}

			return;
		}

		// Forced: a device that was powered off renders nothing, so the surface it missed has to be sent
		// again even when nothing changed while it was away.
		await RebuildAsync(session, force: true, cancellationToken);
	}

	public Task OnWidgetStateChanged(Guid widgetId, CancellationToken cancellationToken = default)
		=> InvalidateSubscribers(widgetId);

	public Task OnWidgetLabelChanged(Guid widgetId, string state, CancellationToken cancellationToken = default)
		=> InvalidateSubscribers(widgetId);

	public Task OnWidgetIconChanged(Guid widgetId, CancellationToken cancellationToken = default)
		=> InvalidateSubscribers(widgetId);

	public void Dispose()
	{
		foreach (var deviceId in _sessions.Keys)
		{
			if (_sessions.TryRemove(deviceId, out var session))
			{
				Unsubscribe(session);
				session.Dispose();
			}
		}
	}

	private Task InvalidateSubscribers(Guid widgetId)
	{
		foreach (var session in _sessions.Values)
		{
			if (session.Subscriptions.Any(subscription => subscription.WidgetId == widgetId))
			{
				ScheduleRebuild(session);
			}
		}

		return Task.CompletedTask;
	}

	private void ScheduleRebuild(DeviceSurfaceSession session)
	{
		if (!session.TryArmRebuild())
		{
			return;
		}

		session.RebuildTimer ??= _timeProvider.CreateTimer(_ => _ = RunScheduledRebuild(session),
			null,
			Timeout.InfiniteTimeSpan,
			Timeout.InfiniteTimeSpan);
		session.RebuildTimer.Change(_rebuildDebounce, Timeout.InfiniteTimeSpan);
	}

	private async Task RunScheduledRebuild(DeviceSurfaceSession session)
	{
		session.DisarmRebuild();

		try
		{
			await RebuildAsync(session, force: false, CancellationToken.None);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception, "Failed to rebuild the surface for device {DeviceId}", session.DeviceId);
		}
	}

	private async Task RebuildAsync(DeviceSurfaceSession session, bool force, CancellationToken cancellationToken)
	{
		if (session.Closed)
		{
			return;
		}

		await session.Gate.WaitAsync(cancellationToken);
		try
		{
			var projection = await ProjectAsync(session, cancellationToken);
			await ApplyAsync(session, projection, cancellationToken, force);
		}
		finally
		{
			session.Gate.Release();
		}
	}

	private async Task<DeviceSurfaceProjection> ProjectAsync(
		DeviceSurfaceSession session,
		CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var devices = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

		var device = await devices.GetById(session.DeviceId);
		if (device is null)
		{
			return DeviceSurfaceProjection.Empty;
		}

		// Reassigning the device has to win over wherever a cross-profile navigation left the session,
		// so a changed assignment resets the session back to the new profile's start folder.
		if (session.HasProjected &&
			!string.Equals(session.AssignedProfileId, device.StartupProfileId, StringComparison.Ordinal))
		{
			session.ProfileId = null;
			session.FolderId = null;
			session.ClearHistory();
		}

		session.AssignedProfileId = device.StartupProfileId;
		session.HasProjected = true;

		var builder = scope.ServiceProvider.GetRequiredService<DeviceSurfaceBuilder>();
		return await builder.BuildAsync(device, session.ProfileId, session.FolderId, cancellationToken);
	}

	private async Task ApplyAsync(
		DeviceSurfaceSession session,
		DeviceSurfaceProjection projection,
		CancellationToken cancellationToken,
		bool force = false)
	{
		// The current folder was deleted under the device: the projection falls back to the profile's
		// start folder, and the stale id must not survive in the history either.
		var fellBack = session.FolderId is not null &&
			!string.Equals(session.FolderId, projection.FolderId, StringComparison.Ordinal);

		session.ProfileId = projection.ProfileId;
		session.FolderId = projection.FolderId;
		if (fellBack && projection.ProfileId is { } profileId)
		{
			session.PruneHistory(profileId,
				FoldersOf(profileId).Select(folder => folder.Id).ToHashSet(StringComparer.Ordinal));
		}

		Subscribe(session, projection.Subscriptions);

		// A session discarded while this rebuild was in flight must not have a surface delivered after
		// its close, and must not re-subscribe a connection Unsubscribe has already dropped.
		if (session.Closed)
		{
			Unsubscribe(session);
			return;
		}

		if (!session.Online)
		{
			return;
		}

		// The whole point of the rule: a rebuild that lands on the same content is not a change, so
		// nothing is pushed and the revision the provider is rendering stays valid.
		if (!force &&
			session.LastPushed is { } last &&
			DeviceSurfaceComparison.SameContent(last, projection.Surface))
		{
			return;
		}

		var surface = session.Advance(projection.Surface, projection.WidgetFolderIds);
		try
		{
			await session.Provider.PushAsync(session.DeviceId.ToString(), surface, cancellationToken);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception, "Failed to push a surface to device {DeviceId}", session.DeviceId);
		}
	}

	private bool TryApplyNavigation(DeviceSurfaceSession session, DeckNavigationCommand command)
	{
		switch (command.Command)
		{
			case DeckNavigationCommands.ChangeTo:
			{
				if (command.FolderId is not { Length: > 0 } folderId)
				{
					return false;
				}

				var profileId = command.ProfileId ?? session.ProfileId;
				if (profileId is null ||
					!FoldersOf(profileId).Any(folder =>
						string.Equals(folder.Id, folderId, StringComparison.Ordinal)))
				{
					return false;
				}

				session.PushHistory();
				session.ProfileId = profileId;
				session.FolderId = folderId;
				return true;
			}

			case DeckNavigationCommands.Parent:
			{
				if (session.ProfileId is not { } profileId)
				{
					return false;
				}

				var folders = FoldersOf(profileId);
				var current = folders.FirstOrDefault(folder =>
					string.Equals(folder.Id, session.FolderId, StringComparison.Ordinal));
				if (current?.ParentId is not { Length: > 0 } parentId ||
					folders.All(folder => !string.Equals(folder.Id, parentId, StringComparison.Ordinal)))
				{
					return false;
				}

				session.PushHistory();
				session.FolderId = parentId;
				return true;
			}

			case DeckNavigationCommands.Back:
			{
				// Matches the client exactly, quirk included: "back" consumes history without pushing
				// the folder it leaves, so a back never becomes forward.
				while (session.PopHistory() is { } previous)
				{
					var profileId = previous.ProfileId ?? session.ProfileId;
					if (profileId is null ||
						!FoldersOf(profileId).Any(folder =>
							string.Equals(folder.Id, previous.FolderId, StringComparison.Ordinal)))
					{
						continue;
					}

					session.ProfileId = profileId;
					session.FolderId = previous.FolderId;
					return true;
				}

				return false;
			}

			default:
				return false;
		}
	}

	private IReadOnlyList<Folder> FoldersOf(string profileId) => _profiles.GetFoldersForProfile(profileId);

	private async Task ReportFolderChanged(
		DeviceSurfaceSession session,
		DeviceSurfaceProjection projection,
		DeckNavigationCommand command,
		CancellationToken cancellationToken)
	{
		if (projection.FolderId is not { } folderId || projection.ProfileId is not { } profileId)
		{
			return;
		}

		// OnFolderReported does not raise the event itself - the client's report handler publishes it
		// separately - so a device session has to do both or device navigation stops driving automations.
		if (!command.IsResync)
		{
			_bus.Publish(new EventOccurrence(EventIds.Qualify(EventIds.FolderChanged),
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["folderId"] = folderId,
					["folderName"] = projection.FolderName ?? string.Empty,
					["profileId"] = profileId,
					["deviceId"] = session.DeviceId.ToString(),
					["clientId"] = DeviceOrigin.For(session.DeviceId)
				}));
		}

		if (Guid.TryParse(folderId, out var persistedFolderId))
		{
			await _focus.OnFolderReported(session.DeviceId,
				persistedFolderId,
				command.NavigationToken,
				command.IsResync,
				cancellationToken);
		}
	}

	private void Subscribe(DeviceSurfaceSession session, IReadOnlyList<DeviceSurfaceSubscription> subscriptions)
	{
		var connectionId = DeviceSessionGroups.For(session.DeviceId);
		foreach (var previous in session.Subscriptions)
		{
			_widgetStateSubscriptions.Remove(connectionId, previous.WidgetId.ToString());
			_labelSubscriptions.Remove(connectionId, previous.WidgetId.ToString(), previous.StateId);
		}

		foreach (var subscription in subscriptions)
		{
			_widgetStateSubscriptions.Add(connectionId, subscription.WidgetId.ToString());
			// Keyed by (widget, state): re-registering on every rebuild is what keeps label pushes
			// arriving after the widget's state has moved on.
			_labelSubscriptions.Add(connectionId, subscription.WidgetId.ToString(), subscription.StateId);
		}

		session.Subscriptions = subscriptions;
	}

	private void Unsubscribe(DeviceSurfaceSession session)
	{
		var connectionId = DeviceSessionGroups.For(session.DeviceId);
		_widgetStateSubscriptions.RemoveConnection(connectionId);
		_labelSubscriptions.RemoveConnection(connectionId);
		session.Subscriptions = [];
	}

	private async Task DiscardAsync(DeviceSurfaceSession session, string? reason, bool notifyProvider)
	{
		_sessions.TryRemove(session.DeviceId, out _);

		// Taken so the session is never disposed underneath a rebuild that is already inside the gate:
		// disposing it there would fail that rebuild on its own Release, which NavigateAsync surfaces to
		// its caller as an ObjectDisposedException.
		await session.Gate.WaitAsync(CancellationToken.None);
		try
		{
			session.Closed = true;
			Unsubscribe(session);
		}
		finally
		{
			session.Gate.Release();
		}

		if (session.Presses is { } presses)
		{
			await presses.CancelAllAsync();
		}

		if (notifyProvider)
		{
			try
			{
				await session.Provider.CloseAsync(session.DeviceId.ToString(), reason, CancellationToken.None);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Error(exception,
					"Provider failed to close the session for device {DeviceId}",
					session.DeviceId);
			}
		}

		session.Dispose();
	}
}
