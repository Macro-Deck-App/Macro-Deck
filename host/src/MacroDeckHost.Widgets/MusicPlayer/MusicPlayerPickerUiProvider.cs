using System.Text.Json;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.MusicPlayer;

/// <summary>
/// Serves the Play Track / Play Playlist picker as a Macro Deck UI dialog.
///
/// <para>
/// It replaces a picker that was its own protocol - a push, a catalogue endpoint and a submit, with the
/// dialog itself written once per client framework. A client that renders the widget profile renders
/// this, which is the whole reason the dialog surface exists.
/// </para>
/// </summary>
public sealed class MusicPlayerPickerUiProvider : IBuiltInIntegrationUiProvider
{
	/// <summary>The view id the Play Track / Play Playlist action asks for.</summary>
	public const string PickViewId = "music-player-pick";

	internal const string InstanceIdKey = "instanceId";
	internal const string KindKey = "kind";

	private readonly IMusicPlayerRegistry _registry;
	private readonly IMusicPlayerArtworkService _artworkService;
	private readonly IUiResourceStore _resources;
	private readonly ILogger _logger;

	public MusicPlayerPickerUiProvider(
		IMusicPlayerRegistry registry,
		IMusicPlayerArtworkService artworkService,
		IUiResourceStore resources,
		ILogger logger)
	{
		_registry = registry;
		_artworkService = artworkService;
		_resources = resources;
		_logger = logger;
	}

	// Spelled here rather than referenced, the same way the Weather dialog spells its own: the
	// integrations project sits below this one.
	public string IntegrationId => MusicPlayerWidgetIcons.OwnerId;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new()
			{ Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive },
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Surface.Kind is not UiSurfaceKinds.Dialog || ViewId(request.Surface) != PickViewId)
		{
			return Task.FromResult<IUiSession?>(null);
		}

		var instanceId = Data(request.Surface, InstanceIdKey);
		if (instanceId is null)
		{
			return Task.FromResult<IUiSession?>(null);
		}

		var session = new MusicPlayerPickerSession(request.Surface,
			_registry,
			_artworkService,
			_resources,
			instanceId,
			ParseKind(Data(request.Surface, KindKey)),
			_logger);

		return Task.FromResult<IUiSession?>(session);
	}

	/// <summary>Anything but an explicit playlist is a track - the kind the action asks for by default.</summary>
	internal static MusicPlayerCatalogItemKind ParseKind(string? kind)
		=> string.Equals(kind, nameof(MusicPlayerCatalogItemKind.Playlist), StringComparison.OrdinalIgnoreCase)
			? MusicPlayerCatalogItemKind.Playlist
			: MusicPlayerCatalogItemKind.Track;

	private static string? ViewId(UiSurface surface)
		=> surface.Attributes.TryGetValue(UiDialogSurfaceAttributes.ViewId, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

	private static string? Data(UiSurface surface, string key)
	{
		if (!surface.Attributes.TryGetValue(UiDialogSurfaceAttributes.Data, out var data) ||
			data.ValueKind != JsonValueKind.Object ||
			!data.TryGetProperty(key, out var member) ||
			member.ValueKind != JsonValueKind.String)
		{
			return null;
		}

		var value = member.GetString();

		return string.IsNullOrEmpty(value) ? null : value;
	}
}
