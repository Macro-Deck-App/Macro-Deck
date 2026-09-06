using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.MusicPlayer;

/// <summary>
/// Serves the device picker as a Macro Deck UI dialog.
///
/// <para>
/// It replaces a picker that was its own protocol - a push, a device endpoint and a submit, with the
/// dialog itself written once per client framework. A client that renders the widget profile renders
/// this, which is the whole reason the dialog surface exists.
/// </para>
/// </summary>
public sealed class MusicPlayerDevicePickerUiProvider : IBuiltInIntegrationUiProvider
{
	/// <summary>The view id the device picker actions ask for.</summary>
	public const string PickViewId = "music-player-device-pick";

	internal const string InstanceIdKey = "instanceId";

	private readonly IMusicPlayerRegistry _registry;
	private readonly ILogger _logger;

	public MusicPlayerDevicePickerUiProvider(IMusicPlayerRegistry registry, ILogger logger)
	{
		_registry = registry;
		_logger = logger;
	}

	// Spelled here rather than referenced, the same way the track picker's is: the integrations project
	// sits below this one.
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

		var session = new MusicPlayerDevicePickerSession(request.Surface, _registry, instanceId, _logger);

		return Task.FromResult<IUiSession?>(session);
	}

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
