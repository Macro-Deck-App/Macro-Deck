using MacroDeckHost.Integrations.Http.Actions;
using MacroDeckHost.Integrations.Http.Client;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Http;

[MacroDeckIntegration]
public sealed class HttpIntegration : IIntegration, IIntegrationIconProvider
{
	public const string IntegrationId = "app.macro-deck.http";

	private static readonly byte[] _icon = LoadIcon();

	private readonly HttpVariableAccessor _variables = new();

	public HttpIntegration()
		: this(new HttpRequestClient())
	{
	}

	internal HttpIntegration(IHttpRequestClient client)
	{
		Actions = HttpActions.Create(client, _variables);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => "HTTP";

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public byte[] GetIcon() => _icon;

	public Task InitializeAsync(IIntegrationContext context)
	{
		_variables.Current = context.Variables;
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
		var assembly = typeof(HttpIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("http-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
