using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using MacroDeckHost.Integrations.AutoHotkey.State;
using MacroDeckHost.Integrations.AutoHotkey.Widgets;
using MacroDeckHost.Integrations.Ui;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Page;

namespace MacroDeckHost.Integrations.AutoHotkey;

public sealed partial class AutoHotkeyIntegration : IUiProvider, IAutoHotkeyPageHost
{
	internal const string ScriptsPageId = "scripts";

	private Action? _runtimeChanged;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new()
			{ Kind = UiSurfaceKinds.Page, SessionMode = UiSessionModes.Exclusive }
	];

	public IReadOnlyList<UiPageDeclaration> Pages { get; } =
	[
		new()
			{ Id = ScriptsPageId, Title = Strings.Title() }
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		var surface = request.Surface;
		if (surface.Kind != UiSurfaceKinds.Page
			|| Attribute(surface, UiPageSurfaceAttributes.PageId) != ScriptsPageId
			|| _context is null
			|| _store is null
			|| _manager is null)
		{
			return Task.FromResult<IUiSession?>(null);
		}

		var page = new AutoHotkeyScriptsPage(this, Attribute(surface, UiPageSurfaceAttributes.Culture), _logger);
		var session = new UiViewSession(new UiView(surface, page.Build()), page.DisposeAsync);
		page.Session = session;
		return Task.FromResult<IUiSession?>(session);
	}

	event Action? IAutoHotkeyPageHost.RuntimeChanged
	{
		add => _runtimeChanged += value;
		remove => _runtimeChanged -= value;
	}

	AutoHotkeyScriptStore? IAutoHotkeyPageHost.Library => _store;

	AutoHotkeyProcessManager? IAutoHotkeyPageHost.Runs => _manager;

	IUiResourceRegistry? IAutoHotkeyPageHost.Resources => _context?.UiResources;

	AutoHotkeyInstallation IAutoHotkeyPageHost.Installation => _installation;

	private void RaiseRuntimeChanged()
	{
		try
		{
			_runtimeChanged?.Invoke();
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "An AutoHotkey page could not follow the restarted integration");
		}
	}

	private static string? Attribute(UiSurface surface, string name)
	{
		return surface.Attributes.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}
}
