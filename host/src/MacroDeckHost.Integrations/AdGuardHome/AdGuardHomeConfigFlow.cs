using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Application.AdGuardHome;
using MacroDeckHost.Localization;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AdGuardHome.Config;

namespace MacroDeckHost.Integrations.AdGuardHome;

public sealed class AdGuardHomeConfigFlow : IConfigFlow
{
	private const string ConnectionStepId = "connection";

	private static readonly ILogger _logger =
		IntegrationLog.For<AdGuardHomeConfigFlow>(AdGuardHomeIntegration.IntegrationId);

	private readonly Func<AdGuardHomeConnectionSettings, IAdGuardHomeClient> _clientFactory;

	public AdGuardHomeConfigFlow()
		: this(settings => new AdGuardHomeClient(settings))
	{
	}

	internal AdGuardHomeConfigFlow(Func<AdGuardHomeConnectionSettings, IAdGuardHomeClient> clientFactory)
	{
		_clientFactory = clientFactory;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(ConnectionStep(context)));

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
		=> stepId == ConnectionStepId
			? await SubmitConnection(input, context, cancellationToken)
			: ConfigFlowResult.Error(ConnectionStep(context), Strings.UnknownStep());

	private async Task<ConfigFlowResult> SubmitConnection(
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		var entryTitle = (context as IConfigFlowEntryContext)?.EntryTitle;
		var name = entryTitle ?? (input.GetValueOrDefault(AdGuardHomeConfigKeys.Name) as string ?? string.Empty).Trim();
		var baseUrl = input.GetValueOrDefault(AdGuardHomeConfigKeys.BaseUrl) as string;
		var username = (input.GetValueOrDefault(AdGuardHomeConfigKeys.Username) as string ?? string.Empty).Trim();
		var password = input.GetValueOrDefault(AdGuardHomeConfigKeys.Password) as string ?? string.Empty;
		var acceptUntrusted = input.GetValueOrDefault(AdGuardHomeConfigKeys.AcceptUntrustedCertificate) is true;

		var fieldErrors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(name))
		{
			fieldErrors[AdGuardHomeConfigKeys.Name] = Strings.EnterName();
		}

		var controlUrl = AdGuardHomeEndpoint.TryBuild(baseUrl);
		if (controlUrl is null)
		{
			fieldErrors[AdGuardHomeConfigKeys.BaseUrl] = Strings.EnterAddress(exampleUrl: AdGuardHomeEndpoint.ExampleUrl);
		}

		if (username.Length == 0 && password.Length > 0)
		{
			fieldErrors[AdGuardHomeConfigKeys.Username] = Strings.EnterUsername();
		}

		if (fieldErrors.Count > 0 || controlUrl is null)
		{
			return ConfigFlowResult.Error(ConnectionStep(context), fieldErrors: fieldErrors);
		}

		var client = _clientFactory(new AdGuardHomeConnectionSettings(controlUrl,
			username.Length > 0 ? username : null,
			password,
			acceptUntrusted));

		try
		{
			await client.GetStatusAsync(cancellationToken);
		}
		catch (AdGuardHomeException exception)
		{
			_logger.Warning(exception, "AdGuard Home connection test failed for {Url}", controlUrl);

			if (exception.Failure == AdGuardHomeConnection.Unauthorized)
			{
				return ConfigFlowResult.Error(ConnectionStep(context),
					fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
					{
						[AdGuardHomeConfigKeys.Password] = Strings.AuthenticationFailed()
					});
			}

			return ConfigFlowResult.Error(ConnectionStep(context), AdGuardHomeText.ConnectionError(exception.Failure));
		}

		return ConfigFlowResult.Complete(name, new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal));
	}

	private static ConfigFlowStep ConnectionStep(IConfigFlowContext context)
	{
		var fields = new List<ActionParameter>();
		if ((context as IConfigFlowEntryContext)?.EntryTitle is null)
		{
			fields.Add(ActionParameter.Text(AdGuardHomeConfigKeys.Name,
				label: AppStrings.Integrations.Detail.ConfigurationNameLabel(),
				placeholder: Strings.NamePlaceholder(),
				required: true));
		}

		fields.AddRange(
		[
			ActionParameter.Url(AdGuardHomeConfigKeys.BaseUrl,
				label: Strings.AddressLabel(),
				description: Strings.AddressDescription(),
				placeholder: AdGuardHomeEndpoint.ExampleUrl,
				required: true),
			ActionParameter.Text(AdGuardHomeConfigKeys.Username,
				label: Strings.UsernameLabel(),
				description: Strings.UsernameDescription()),
			ActionParameter.Secret(AdGuardHomeConfigKeys.Password,
				label: Strings.PasswordLabel()),
			ActionParameter.Toggle(AdGuardHomeConfigKeys.AcceptUntrustedCertificate,
				label: Strings.AcceptUntrustedCertificateLabel(),
				description: Strings.AcceptUntrustedCertificateDescription())
		]);

		return new ConfigFlowStep
		{
			StepId = ConnectionStepId,
			Title = Strings.ConnectionTitle(),
			Description = Strings.ConnectionDescription(),
			Links =
			[
				new ConfigFlowLink { Label = Strings.WebsiteLinkLabel(), Url = "https://adguard.com/adguard-home/overview.html" }
			],
			Fields = fields
		};
	}
}
