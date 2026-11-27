using MacroDeck.Sdk;
using MacroDeckHost.Integrations.Clipboard.Native;
using MacroDeckHost.Integrations.Clipboard.State;
using Serilog;

namespace MacroDeckHost.Integrations.Clipboard.Actions;

internal sealed record ClipboardActionServices(
	IClipboardBackend Backend,
	ClipboardMonitor Monitor,
	Func<IIntegrationContext?> Context,
	ILogger Logger);
