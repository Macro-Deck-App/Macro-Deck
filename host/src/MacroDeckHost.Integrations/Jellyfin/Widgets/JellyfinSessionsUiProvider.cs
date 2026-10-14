using System.Text.Json;
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
using MacroDeck.Ui.Widgets;
using MacroDeckHost.Integrations.Ui;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.JellyfinSessions;

namespace MacroDeckHost.Integrations.Jellyfin.Widgets;

internal sealed class JellyfinSessionsUiProvider : IUiProvider
{
	public const string IconFolder = "widget";

	public const string GlyphIcon = "logo";

	private readonly IJellyfinSessionSource _source;
	private readonly Func<IUiResourceRegistry?> _resources;
	private readonly TimeProvider _time;

	public JellyfinSessionsUiProvider(
		IJellyfinSessionSource source,
		Func<IUiResourceRegistry?> resources,
		TimeProvider time)
	{
		_source = source;
		_resources = resources;
		_time = time;
	}

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
	];

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;

		if (UiWidgetSurface.IsConfigurationFor(surface, JellyfinWidgetTypes.SessionsQualifiedId))
		{
			return new UiViewSession(new UiView(surface,
				BuildConfig(UiWidgetSurface.Data(surface), _source.GetServers())));
		}

		if (!UiWidgetSurface.IsFor(surface, JellyfinWidgetTypes.SessionsQualifiedId))
		{
			return null;
		}

		var data = UiWidgetSurface.Data(surface);
		var radius = UiWidgetSafeArea.RadiusOf(surface);
		var background = JellyfinSessionsSettings.BackgroundColor(data);
		var registry = _resources();
		var glyph = await GlyphAsync(registry, cancellationToken).ConfigureAwait(false);

		if (UiWidgetSurface.IsSampleRequested(surface))
		{
			var sample = new UiState<JellyfinSessionsState>(JellyfinSessionsSample.State with { Glyph = glyph });
			return new UiViewSession(new UiView(surface, JellyfinSessionsView.Build(sample, radius, background)));
		}

		return new JellyfinSessionsWidgetSession(surface,
			_source,
			JellyfinSessionsSettings.Server(data),
			_resources,
			glyph,
			registry is null,
			_time,
			state => JellyfinSessionsView.Build(state, radius, background));
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
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							.. servers.Select(candidate => UiOption.Of(candidate.ServerId, candidate.Title))
						])
					},
					UiWidgetAppearance.Section(data,
						UiWidgetAppearanceFields.Border
						| UiWidgetAppearanceFields.BackgroundColor
						| UiWidgetAppearanceFields.TransparentBackground)
				]
			}
		};
	}

	internal static async Task<UiResource?> GlyphAsync(
		IUiResourceRegistry? resources,
		CancellationToken cancellationToken)
	{
		if (resources is null)
		{
			return null;
		}

		try
		{
			return await resources.GetPluginIconAsync(IconFolder, GlyphIcon, cancellationToken).ConfigureAwait(false);
		}
		catch (UiResourceException)
		{
			return null;
		}
	}
}

internal sealed class JellyfinSessionsWidgetSession : IUiSession
{
	public const int ArtworkSize = 256;

	private static readonly TimeSpan _progressInterval = TimeSpan.FromSeconds(5);

	private readonly IJellyfinSessionSource _source;
	private readonly string? _serverId;
	private readonly Func<IUiResourceRegistry?> _resources;
	private readonly UiState<JellyfinSessionsState> _state;
	private readonly UiView _view;
	private readonly ITimer _timer;
	private readonly Lock _sync = new();
	private readonly string _resourcePrefix = $"sessions-{Guid.NewGuid():N}-";
	private readonly Dictionary<JellyfinArtworkRef, string> _artwork = [];
	private readonly Dictionary<JellyfinArtworkRef, UiResource> _artworkHandles = [];
	private readonly HashSet<JellyfinArtworkRef> _loading = [];
	private readonly HashSet<JellyfinArtworkRef> _unavailable = [];
	private readonly CancellationTokenSource _cts = new();
	private UiResource? _glyph;
	private bool _glyphPending;
	private bool _disposed;

	public JellyfinSessionsWidgetSession(
		UiSurface surface,
		IJellyfinSessionSource source,
		string? serverId,
		Func<IUiResourceRegistry?> resources,
		UiResource? glyph,
		bool glyphPending,
		TimeProvider time,
		Func<UiState<JellyfinSessionsState>, UiElement> build)
	{
		_source = source;
		_serverId = serverId;
		_resources = resources;
		_glyph = glyph;
		_glyphPending = glyphPending;
		_state = new UiState<JellyfinSessionsState>(Compute());
		_view = new UiView(surface, build(_state));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
		_timer = time.CreateTimer(_ => Refresh(), null, _progressInterval, _progressInterval);
		_source.SessionsChanged += OnSessionsChanged;

		LoadMissingArtwork(_state.Value);
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree()
	{
		return _view.Tree;
	}

	public IReadOnlyList<UiPatch> DrainPatches()
	{
		return _view.DrainPatches();
	}

	public void Dispatch(UiEvent uiEvent)
	{
		_view.Dispatch(uiEvent);
	}

	public ValueTask DisposeAsync()
	{
		_source.SessionsChanged -= OnSessionsChanged;

		List<string> names;
		lock (_sync)
		{
			_disposed = true;
			names = [.. _artwork.Values];
			_artwork.Clear();
			_artworkHandles.Clear();
		}

		Remove(names);
		_cts.Cancel();
		_cts.Dispose();
		_timer.Dispose();
		_view.Dispose();
		return ValueTask.CompletedTask;
	}

	private void OnSessionsChanged(object? sender, EventArgs e)
	{
		Refresh();
	}

	private JellyfinSessionsState Compute()
	{
		return JellyfinSessionsState.Compute(_source.GetServers(),
			_serverId,
			reference => _artworkHandles.GetValueOrDefault(reference),
			_glyph);
	}

	private void Refresh()
	{
		JellyfinSessionsState next;
		List<string> stale;
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			next = Compute();
			stale = ReleaseUnused(next);
			if (next.Status != _state.Value.Status
				|| next.Glyph != _state.Value.Glyph
				|| !next.Rows.SequenceEqual(_state.Value.Rows))
			{
				using (_view.Batch())
				{
					_state.Set(next);
				}
			}

			if (_glyphPending && _resources() is { } registry)
			{
				_glyphPending = false;
				_ = ResolveGlyphAsync(registry);
			}
		}

		Remove(stale);
		LoadMissingArtwork(next);
	}

	// A deck can open the widget before the integration finished initializing, when no glyph can be
	// resolved yet; the first refresh after initialization fills it in.
	private async Task ResolveGlyphAsync(IUiResourceRegistry registry)
	{
		var glyph = await JellyfinSessionsUiProvider.GlyphAsync(registry, CancellationToken.None).ConfigureAwait(false);

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			_glyph = glyph;
		}

		Refresh();
	}

	private List<string> ReleaseUnused(JellyfinSessionsState state)
	{
		var shown = state.Rows.Select(row => row.ArtworkRef).OfType<JellyfinArtworkRef>().ToHashSet();
		_unavailable.RemoveWhere(reference => !shown.Contains(reference));

		var stale = new List<string>();
		foreach (var reference in _artwork.Keys.Where(reference => !shown.Contains(reference)).ToList())
		{
			stale.Add(_artwork[reference]);
			_artwork.Remove(reference);
			_artworkHandles.Remove(reference);
		}

		return stale;
	}

	private void Remove(List<string> names)
	{
		if (names.Count == 0 || _resources() is not { } registry)
		{
			return;
		}

		foreach (var name in names)
		{
			_ = RemoveAsync(registry, name);
		}
	}

	private static async Task RemoveAsync(IUiResourceRegistry registry, string name)
	{
		try
		{
			await registry.RemoveAsync(name).ConfigureAwait(false);
		}
		catch (UiResourceException)
		{
		}
	}

	private void LoadMissingArtwork(JellyfinSessionsState state)
	{
		if (_resources() is not { } registry)
		{
			return;
		}

		foreach (var reference in state.Rows.Select(row => row.ArtworkRef).OfType<JellyfinArtworkRef>().Distinct())
		{
			lock (_sync)
			{
				if (_disposed
					|| _artwork.ContainsKey(reference)
					|| _unavailable.Contains(reference)
					|| !_loading.Add(reference))
				{
					continue;
				}
			}

			_ = LoadArtworkAsync(registry, reference);
		}
	}

	private async Task LoadArtworkAsync(IUiResourceRegistry registry, JellyfinArtworkRef reference)
	{
		var name = _resourcePrefix + reference.ArtworkId;
		try
		{
			var handle = await registry
				.RegisterMusicPlayerArtworkAsync(name,
					reference.InstanceId,
					reference.ArtworkId,
					ArtworkSize,
					_cts.Token)
				.ConfigureAwait(false);
			if (handle is null)
			{
				lock (_sync)
				{
					_unavailable.Add(reference);
				}

				return;
			}

			bool kept;
			lock (_sync)
			{
				kept = !_disposed;
				if (kept)
				{
					_artwork[reference] = name;
					_artworkHandles[reference] = handle;
				}
			}

			if (!kept)
			{
				await RemoveAsync(registry, name).ConfigureAwait(false);
				return;
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
