using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Colors;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ColorVariableChangedHandler :
	INotificationHandler<VariableUpdatedNotification>,
	INotificationHandler<VariableCreatedNotification>,
	INotificationHandler<VariableDeletedNotification>,
	INotificationHandler<AppearanceChangedNotification>
{
	private volatile AccentSource? _accent;

	private readonly IWidgetVariableIndex _index;
	private readonly IFolderCache _folders;
	private readonly IProfileCache _profiles;
	private readonly IWidgetRenderSignals _renderSignals;
	private readonly IUiSessionBroker _sessions;
	private readonly IColorReferenceResolver _colors;
	private readonly IUiTransport _uiTransport;
	private readonly IDeviceSurfaceService _surfaces;
	private readonly DeviceLayoutConstraintTracker _layoutConstraints;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ColorChangeSignal _signal;

	public ColorVariableChangedHandler(
		IWidgetVariableIndex index,
		IFolderCache folders,
		IProfileCache profiles,
		IWidgetRenderSignals renderSignals,
		IUiSessionBroker sessions,
		IColorReferenceResolver colors,
		IUiTransport uiTransport,
		IDeviceSurfaceService surfaces,
		DeviceLayoutConstraintTracker layoutConstraints,
		IServiceScopeFactory scopeFactory,
		ColorChangeSignal signal)
	{
		_signal = signal;
		_index = index;
		_folders = folders;
		_profiles = profiles;
		_renderSignals = renderSignals;
		_sessions = sessions;
		_colors = colors;
		_uiTransport = uiTransport;
		_surfaces = surfaces;
		_layoutConstraints = layoutConstraints;
		_scopeFactory = scopeFactory;
	}

	// Only a Color variable's own updates can change a resolved colour. A create or delete of a widget
	// variable of any type can too, because it shadows the global one of the same name for that widget.
	public async ValueTask Handle(VariableUpdatedNotification notification, CancellationToken cancellationToken)
	{
		if (notification.Variable.Type != VariableType.Color)
		{
			return;
		}

		await RefreshAsync(notification.Variable, notification.Variable.Name, cancellationToken);
		if (notification.PreviousName is { } previous)
		{
			await RefreshAsync(notification.Variable, previous, cancellationToken);
		}
	}

	public ValueTask Handle(AppearanceChangedNotification notification, CancellationToken cancellationToken)
	{
		_accent = new AccentSource(notification.AccentColorSource);
		return ValueTask.CompletedTask;
	}

	public ValueTask Handle(VariableCreatedNotification notification, CancellationToken cancellationToken)
		=> AffectsResolution(notification.Variable)
			? new ValueTask(RefreshAsync(notification.Variable, notification.Variable.Name, cancellationToken))
			: ValueTask.CompletedTask;

	public ValueTask Handle(VariableDeletedNotification notification, CancellationToken cancellationToken)
		=> AffectsResolution(notification.Variable)
			? new ValueTask(RefreshAsync(notification.Variable, notification.Variable.Name, cancellationToken))
			: ValueTask.CompletedTask;

	private static bool AffectsResolution(VariableEntity variable)
		=> variable.Type == VariableType.Color || variable.Scope == VariableScope.Widget;

	private async Task RefreshAsync(VariableEntity variable, string name, CancellationToken cancellationToken)
	{
		_signal.Raise();
		var changed = RefreshWidgets(variable, name);

		if (variable.Scope == VariableScope.Global)
		{
			changed |= await RefreshFoldersAsync(name, cancellationToken);
			changed |= await RefreshProfilesAsync(name, cancellationToken);
			await RefreshAccentAsync(name, cancellationToken);
		}

		if (changed)
		{
			await _surfaces.InvalidateAsync(cancellationToken);
		}
	}

	private bool RefreshWidgets(VariableEntity variable, string name)
	{
		IReadOnlyList<Guid> widgetIds = variable.Scope == VariableScope.Widget
			? Guid.TryParse(variable.ScopeRefId, out var ownerId) && _index.ColorReferences(ownerId, name)
				? [ownerId]
				: []
			: _index.FindColorReferences(name);

		if (widgetIds.Count == 0)
		{
			return false;
		}

		var wanted = widgetIds.ToHashSet();
		foreach (var widget in _folders.GetAllFolders().SelectMany(folder => folder.Widgets))
		{
			if (!wanted.Contains(widget.Id))
			{
				continue;
			}

			if (!_renderSignals.RaiseDataChanged(_colors.Resolved(widget)))
			{
				_sessions.InvalidateWidgetSessions(widget.Id);
			}
		}

		return true;
	}

	private async Task<bool> RefreshFoldersAsync(string name, CancellationToken cancellationToken)
	{
		var changed = false;
		foreach (var folder in _folders.GetAllFolders())
		{
			if (!ColorReferenceScanner.References(folder.BackgroundColor, name))
			{
				continue;
			}

			changed = true;
			await _uiTransport.Send(new FolderUpdatedEvent { Folder = FolderDtoMapper.MapToDto(folder, _colors) },
				cancellationToken);
		}

		return changed;
	}

	private async Task<bool> RefreshProfilesAsync(string name, CancellationToken cancellationToken)
	{
		var changed = false;
		foreach (var profile in _profiles.GetAll())
		{
			if (!ColorReferenceScanner.References(profile.DefaultBackgroundColor, name))
			{
				continue;
			}

			changed = true;
			var constraint = _layoutConstraints.Get(profile.Id.ToString());
			await _uiTransport.Send(
				new ProfileUpdatedEvent { Profile = ProfileDtoMapper.MapJsonProfile(profile, constraint, _colors) },
				cancellationToken);
		}

		return changed;
	}

	// The accent source is read from the store once and then followed through AppearanceChanged, so a
	// variable the accent does not reference costs no store read.
	private async Task RefreshAccentAsync(string name, CancellationToken cancellationToken)
	{
		if (_accent is { } known && !ColorReferenceScanner.References(known.Source, name))
		{
			return;
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var appearance = await scope.ServiceProvider.GetRequiredService<IAppPreferenceService>().GetAppearance();
		_accent = new AccentSource(appearance.AccentColorSource);
		if (!ColorReferenceScanner.References(appearance.AccentColorSource, name))
		{
			return;
		}

		await scope.ServiceProvider.GetRequiredService<IMediator>()
			.Publish(new AppearanceChangedNotification(appearance.ThemeMode,
					appearance.AccentColor,
					appearance.FontFamily,
					appearance.AccentColorSource),
				cancellationToken);
	}

	private sealed record AccentSource(string? Source);
}
