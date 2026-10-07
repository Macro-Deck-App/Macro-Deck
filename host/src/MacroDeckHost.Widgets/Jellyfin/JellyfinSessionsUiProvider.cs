using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Jellyfin;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.JellyfinSessions;

namespace MacroDeckHost.Widgets.Jellyfin;

public sealed class JellyfinSessionsUiProvider : IBuiltInIntegrationUiProvider
{
	private readonly IIntegrationRegistry _integrations;
	private readonly IMusicPlayerArtworkService _artwork;
	private readonly IUiResourceStore _resources;
	private readonly TimeProvider _time;
	private readonly Lazy<UiResource?> _glyph;

	public JellyfinSessionsUiProvider(
		IIntegrationRegistry integrations,
		IMusicPlayerArtworkService artwork,
		IUiResourceStore resources,
		TimeProvider time)
	{
		_integrations = integrations;
		_artwork = artwork;
		_resources = resources;
		_time = time;
		_glyph = new Lazy<UiResource?>(RegisterGlyph);
	}

	public string IntegrationId => JellyfinWidgetTypes.OwnerId;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;
		var source = _integrations.Integrations.OfType<IJellyfinSessionSource>().FirstOrDefault();

		if (surface.Kind == UiSurfaceKinds.Config)
		{
			return Task.FromResult<IUiSession?>(WidgetConfigSurfaces.IsFor(surface, JellyfinWidgetTypes.SessionsQualifiedId)
				? new WidgetConfigSession(new UiView(surface,
					BuildConfig(WidgetConfigSurfaces.Data(surface), source?.GetServers() ?? [])))
				: null);
		}

		if (surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview) ||
			ReadString(surface, UiWidgetSurfaceAttributes.WidgetType) != JellyfinWidgetTypes.SessionsQualifiedId)
		{
			return Task.FromResult<IUiSession?>(null);
		}

		var data = surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var element) &&
			element.ValueKind == JsonValueKind.Object
				? element
				: default;
		var radius = WidgetSafeArea.RadiusOf(surface);
		var background = JellyfinSessionsSettings.BackgroundColor(data);

		if (WidgetSamplePreview.IsRequested(surface))
		{
			var sample = new UiState<JellyfinSessionsState>(JellyfinSessionsSample.State with { Glyph = _glyph.Value });
			return Task.FromResult<IUiSession?>(
				new StaticWidgetUiSession(new UiView(surface, JellyfinSessionsView.Build(sample, radius, background))));
		}

		return Task.FromResult<IUiSession?>(new JellyfinSessionsWidgetSession(surface,
			source,
			JellyfinSessionsSettings.Server(data),
			_artwork,
			_resources,
			_glyph.Value,
			_time,
			state => JellyfinSessionsView.Build(state, radius, background)));
	}

	internal static UiElement BuildConfig(JsonElement data, IReadOnlyList<JellyfinServerSummary> servers)
	{
		var server = new UiState<string>(JellyfinSessionsSettings.Server(data) ?? string.Empty);

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "jellyfin-heading", Text = Strings.Name() },
					new UiChoiceInput
					{
						Key = JellyfinWidgetTypes.ServerKey,
						Label = Strings.Server(),
						Description = Strings.ServerDescription(),
						Placeholder = Strings.AllServers(),
						Binding = Bind.To(server),
						SupportsReset = true,
						DefaultValue = string.Empty,
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							.. servers.Select(candidate => UiOption.Of(candidate.ServerId, candidate.Title)),
						]),
					},
					UiWidgetAppearance.Section(data,
						UiWidgetAppearanceFields.Border | UiWidgetAppearanceFields.BackgroundColor |
						UiWidgetAppearanceFields.TransparentBackground),
				],
			},
		};
	}

	private UiResource? RegisterGlyph()
	{
		if (_integrations.Integrations.FirstOrDefault(integration => integration.Id == JellyfinWidgetTypes.OwnerId) is
			not IIntegrationIconProvider provider)
		{
			return null;
		}

		return _resources.Register(new UiResourceRegistration
		{
			OwnerId = JellyfinWidgetTypes.OwnerId,
			Name = "sessions-glyph",
			MediaType = provider.IconMimeType,
			Content = provider.GetIcon(),
		});
	}

	private static string? ReadString(UiSurface surface, string name)
		=> surface.Attributes.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
}

internal sealed class JellyfinSessionsWidgetSession : IUiSession
{
	private const int ArtworkSize = 256;

	private static readonly TimeSpan _progressInterval = TimeSpan.FromSeconds(5);

	private readonly IJellyfinSessionSource? _source;
	private readonly string? _serverId;
	private readonly IMusicPlayerArtworkService _artworkService;
	private readonly IUiResourceStore _resources;
	private readonly UiResource? _glyph;
	private readonly UiState<JellyfinSessionsState> _state;
	private readonly UiView _view;
	private readonly ITimer _timer;
	private readonly Lock _sync = new();
	private readonly string _resourcePrefix = $"sessions-{Guid.NewGuid():N}-";
	private readonly Dictionary<JellyfinArtworkRef, UiResource> _artwork = [];
	private readonly HashSet<JellyfinArtworkRef> _loading = [];
	private readonly HashSet<JellyfinArtworkRef> _unavailable = [];
	private readonly CancellationTokenSource _cts = new();
	private bool _disposed;

	public JellyfinSessionsWidgetSession(
		UiSurface surface,
		IJellyfinSessionSource? source,
		string? serverId,
		IMusicPlayerArtworkService artworkService,
		IUiResourceStore resources,
		UiResource? glyph,
		TimeProvider time,
		Func<UiState<JellyfinSessionsState>, UiElement> build)
	{
		_source = source;
		_serverId = serverId;
		_artworkService = artworkService;
		_resources = resources;
		_glyph = glyph;
		_state = new UiState<JellyfinSessionsState>(Compute());
		_view = new UiView(surface, build(_state));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
		_timer = time.CreateTimer(_ => Refresh(), null, _progressInterval, _progressInterval);

		if (_source is not null)
		{
			_source.SessionsChanged += OnSessionsChanged;
		}

		LoadMissingArtwork(_state.Value);
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

	public ValueTask DisposeAsync()
	{
		if (_source is not null)
		{
			_source.SessionsChanged -= OnSessionsChanged;
		}

		lock (_sync)
		{
			_disposed = true;
			foreach (var resource in _artwork.Values)
			{
				_resources.Remove(resource.ResourceId);
			}

			_artwork.Clear();
		}

		_cts.Cancel();
		_cts.Dispose();
		_timer.Dispose();
		_view.Dispose();
		return ValueTask.CompletedTask;
	}

	private void OnSessionsChanged(object? sender, EventArgs e) => Refresh();

	private JellyfinSessionsState Compute()
		=> JellyfinSessionsState.Compute(_source?.GetServers() ?? [],
			_serverId,
			reference => _artwork.GetValueOrDefault(reference),
			_glyph);

	private void Refresh()
	{
		JellyfinSessionsState next;
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			next = Compute();
			ReleaseUnused(next);
			if (next.Status != _state.Value.Status || !next.Rows.SequenceEqual(_state.Value.Rows))
			{
				using (_view.Batch())
				{
					_state.Set(next);
				}
			}
		}

		LoadMissingArtwork(next);
	}

	private void ReleaseUnused(JellyfinSessionsState state)
	{
		var shown = state.Rows.Select(row => row.ArtworkRef).OfType<JellyfinArtworkRef>().ToHashSet();
		_unavailable.RemoveWhere(reference => !shown.Contains(reference));
		foreach (var stale in _artwork.Keys.Where(reference => !shown.Contains(reference)).ToList())
		{
			_resources.Remove(_artwork[stale].ResourceId);
			_artwork.Remove(stale);
		}
	}

	private void LoadMissingArtwork(JellyfinSessionsState state)
	{
		foreach (var reference in state.Rows.Select(row => row.ArtworkRef).OfType<JellyfinArtworkRef>().Distinct())
		{
			lock (_sync)
			{
				if (_disposed || _artwork.ContainsKey(reference) || _unavailable.Contains(reference) ||
					!_loading.Add(reference))
				{
					continue;
				}
			}

			_ = LoadArtworkAsync(reference);
		}
	}

	private async Task LoadArtworkAsync(JellyfinArtworkRef reference)
	{
		try
		{
			var image = await _artworkService
				.GetImage(reference.InstanceId, reference.ArtworkId, ArtworkSize, _cts.Token)
				.ConfigureAwait(false);
			if (image is null)
			{
				lock (_sync)
				{
					_unavailable.Add(reference);
				}

				return;
			}

			lock (_sync)
			{
				if (_disposed)
				{
					return;
				}

				_artwork[reference] = _resources.Register(new UiResourceRegistration
				{
					OwnerId = JellyfinWidgetTypes.OwnerId,
					Name = _resourcePrefix + reference.ArtworkId,
					MediaType = image.ContentType,
					Content = image.Content,
				});
			}

			Refresh();
		}
		catch (OperationCanceledException)
		{
		}
#pragma warning disable CA1031 // A missing cover only leaves the placeholder; there is nobody to report it to.
		catch (Exception)
#pragma warning restore CA1031
		{
		}
		finally
		{
			lock (_sync)
			{
				_loading.Remove(reference);
			}
		}
	}
}
