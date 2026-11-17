using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.WakeOnLan.Actions;
using MacroDeckHost.Integrations.WakeOnLan.Configuration;
using MacroDeckHost.Integrations.WakeOnLan.Protocol;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.WakeOnLan;

[MacroDeckIntegration]
public sealed class WakeOnLanIntegration : IIntegration, IConfigFlowProvider, IIntegrationIconProvider
{
	public const string IntegrationId = "app.macro-deck.wake-on-lan";

	private static readonly ILogger _logger = IntegrationLog.For<WakeOnLanIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private IIntegrationContext? _context;

	public WakeOnLanIntegration()
		: this(new UdpWakeOnLanSender(), Task.Delay, _logger)
	{
	}

	internal WakeOnLanIntegration(
		IWakeOnLanSender sender,
		Func<TimeSpan, CancellationToken, Task> delay,
		ILogger logger)
	{
		Actions = [new WakeDeviceActionDefinition(() => _context?.Config, sender, delay, logger)];
	}

	public string Id => IntegrationId;

	public LocalizedText Name => AppStrings.Integrations.WakeOnLan.Name();

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public bool AllowsMultipleConfigurations => true;

	public byte[] GetIcon()
	{
		return _icon;
	}

	public IConfigFlow CreateConfigFlow()
	{
		return new WakeOnLanConfigFlow();
	}

	public Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		IsInitialized = true;
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		IsInitialized = false;
		return Task.CompletedTask;
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(WakeOnLanIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("wake-on-lan-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
