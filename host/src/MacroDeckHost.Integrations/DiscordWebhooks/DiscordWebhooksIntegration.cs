using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.DiscordWebhooks.Actions;
using MacroDeckHost.Integrations.DiscordWebhooks.Configuration;
using MacroDeckHost.Integrations.DiscordWebhooks.Protocol;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.DiscordWebhooks;

[MacroDeckIntegration]
public sealed class DiscordWebhooksIntegration : IIntegration, IConfigFlowProvider, IIntegrationIconProvider
{
	public const string IntegrationId = "app.macro-deck.discord-webhooks";

	private static readonly ILogger _logger = IntegrationLog.For<DiscordWebhooksIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private IIntegrationContext? _context;

	public DiscordWebhooksIntegration()
		: this(new DiscordWebhookSender(), TimeProvider.System, _logger)
	{
	}

	internal DiscordWebhooksIntegration(IDiscordWebhookSender sender, TimeProvider time, ILogger logger)
	{
		Actions =
		[
			new SendMessageActionDefinition(new DiscordWebhookTargets(() => _context?.Config), sender, time, logger)
		];
	}

	public string Id => IntegrationId;

	public LocalizedText Name => AppStrings.Integrations.DiscordWebhooks.Name();

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
		return new DiscordWebhooksConfigFlow();
	}

	public Task InitializeAsync(IIntegrationContext context)
	{
		IntegrationHttp.Use(context.Http);
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
		var assembly = typeof(DiscordWebhooksIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("discord-webhooks-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
