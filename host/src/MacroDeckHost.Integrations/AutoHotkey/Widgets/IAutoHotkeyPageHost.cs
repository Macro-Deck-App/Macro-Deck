using MacroDeck.Sdk.Ui;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using MacroDeckHost.Integrations.AutoHotkey.State;

namespace MacroDeckHost.Integrations.AutoHotkey.Widgets;

internal interface IAutoHotkeyPageHost
{
	AutoHotkeyScriptStore? Library { get; }

	AutoHotkeyProcessManager? Runs { get; }

	IUiResourceRegistry? Resources { get; }

	AutoHotkeyInstallation Installation { get; }

	event Action? RuntimeChanged;
}
