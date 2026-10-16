using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.Steam.Protocol;
using MacroDeckHost.Integrations.Steam.Variables;
using MacroDeckHost.Localization;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Steam.Config;

namespace MacroDeckHost.Integrations.Steam.Configuration;

public sealed class SteamConfigFlow : IConfigFlow
{
	private const string SettingsStepId = "settings";

	private const string DefaultTitle = "Steam";

	private const string ApiKeyUrl = "https://steamcommunity.com/dev/apikey";

	private static readonly ILogger _logger = IntegrationLog.For<SteamConfigFlow>(SteamIntegration.IntegrationId);

	private readonly Func<string, ISteamWebApiClient> _clientFactory;

	public SteamConfigFlow()
		: this(key => new SteamWebApiClient(key))
	{
	}

	internal SteamConfigFlow(Func<string, ISteamWebApiClient> clientFactory)
	{
		_clientFactory = clientFactory;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
	{
		IntegrationHttp.Use(context.Http);
		return Task.FromResult(ConfigFlowResult.Step(SettingsStep(context)));
	}

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		if (stepId != SettingsStepId)
		{
			return ConfigFlowResult.Error(SettingsStep(context), Strings.UnknownStep());
		}

		var existingTitle = (context as IConfigFlowEntryContext)?.EntryTitle;
		var name = (input.GetValueOrDefault(SteamConfigKeys.Name) as string ?? string.Empty).Trim();
		var steamIdText = (input.GetValueOrDefault(SteamConfigKeys.SteamId) as string ?? string.Empty).Trim();
		var apiKey = (input.GetValueOrDefault(SteamConfigKeys.ApiKey) as string ?? string.Empty).Trim();
		var folders = ParseFolders(input.GetValueOrDefault(SteamConfigKeys.LibraryFolders) as string);

		var errors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);

		if (folders.Any(folder => !Path.IsPathFullyQualified(folder)))
		{
			errors[SteamConfigKeys.LibraryFolders] = Strings.FolderMustBeAbsolute();
		}

		if (steamIdText.Length > 0 && apiKey.Length == 0)
		{
			errors[SteamConfigKeys.ApiKey] = Strings.EnterApiKey();
		}
		else if (steamIdText.Length == 0 && apiKey.Length > 0)
		{
			errors[SteamConfigKeys.SteamId] = Strings.EnterSteamId();
		}

		var identity = SteamIdentity.Parse(steamIdText);
		if (steamIdText.Length > 0 && !identity.IsValid)
		{
			errors[SteamConfigKeys.SteamId] = Strings.InvalidSteamId();
		}

		if (errors.Count > 0)
		{
			return ConfigFlowResult.Error(SettingsStep(context), fieldErrors: errors);
		}

		var values = new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal);

		if (steamIdText.Length == 0)
		{
			values[SteamConfigKeys.VariableKey] = ConfigFlowValue.Plain(KeyFor(input, existingTitle ?? name));
			return ConfigFlowResult.Complete(existingTitle ?? (name.Length > 0 ? name : DefaultTitle), values);
		}

		var steamId = identity.SteamId;
		string? personaName = null;
		var client = _clientFactory(apiKey);
		try
		{
			if (steamId is null)
			{
				steamId = await client.ResolveVanityUrlAsync(identity.VanityName!, cancellationToken);
				if (steamId is null)
				{
					return ConfigFlowResult.Error(SettingsStep(context),
						fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
						{
							[SteamConfigKeys.SteamId] = Strings.UnknownProfile()
						});
				}
			}

			var summary = await client.GetPlayerSummaryAsync(steamId, cancellationToken);
			if (summary is null)
			{
				return ConfigFlowResult.Error(SettingsStep(context),
					fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
					{
						[SteamConfigKeys.SteamId] = Strings.UnknownProfile()
					});
			}

			personaName = summary.Name;
		}
		catch (SteamWebApiException exception)
		{
			_logger.Warning("Steam Web API check failed: {Failure}", exception.Failure);

			return exception.Failure == SteamWebFailure.InvalidKey
				? ConfigFlowResult.Error(SettingsStep(context),
					fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
					{
						[SteamConfigKeys.ApiKey] = Strings.RejectedApiKey()
					})
				: ConfigFlowResult.Error(SettingsStep(context), Strings.ServiceUnavailable());
		}
		finally
		{
			(client as IDisposable)?.Dispose();
		}

		var title = existingTitle
			?? (name.Length > 0 ? name : string.IsNullOrWhiteSpace(personaName) ? DefaultTitle : personaName.Trim());

		values[SteamConfigKeys.SteamId] = ConfigFlowValue.Plain(steamId);
		values[SteamConfigKeys.ApiKey] = ConfigFlowValue.Secret(apiKey);
		values[SteamConfigKeys.VariableKey] = ConfigFlowValue.Plain(KeyFor(input, title));

		return ConfigFlowResult.Complete(title, values);
	}

	internal static IReadOnlyList<string> ParseFolders(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return [];
		}

		IEnumerable<string> candidates;
		try
		{
			candidates = text.TrimStart().StartsWith('[')
				? JsonSerializer.Deserialize<string?[]>(text)?.OfType<string>() ?? []
				: text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
		}
		catch (JsonException)
		{
			return [];
		}

		return
		[
			.. candidates.Select(folder => folder.Trim())
				.Where(folder => folder.Length > 0)
				.Distinct(StringComparer.Ordinal)
		];
	}

	private static string KeyFor(IReadOnlyDictionary<string, object?> input, string title)
	{
		return input.GetValueOrDefault(SteamConfigKeys.VariableKey) is string { Length: > 0 } stored
			? stored
			: SteamVariables.Slugify(title) is { Length: > 0 } slug
				? slug
				: "account";
	}

	private static ConfigFlowStep SettingsStep(IConfigFlowContext context)
	{
		var fields = new List<ActionParameter>();
		if ((context as IConfigFlowEntryContext)?.EntryTitle is null)
		{
			fields.Add(ActionParameter.Text(SteamConfigKeys.Name,
				AppStrings.Integrations.Detail.ConfigurationNameLabel(),
				placeholder: Strings.NamePlaceholder()));
		}

		fields.AddRange([
			ActionParameter.Secret(SteamConfigKeys.ApiKey,
				Strings.ApiKeyLabel(),
				Strings.ApiKeyDescription()),
			ActionParameter.Text(SteamConfigKeys.SteamId,
				Strings.SteamIdLabel(),
				Strings.SteamIdDescription(),
				Strings.SteamIdPlaceholder())
		]);

		return new ConfigFlowStep
		{
			StepId = SettingsStepId,
			Title = Strings.Title(),
			Description = Strings.Description(),
			Instructions =
			[
				new ConfigFlowInstruction { Text = Strings.InstructionKey() },
				new ConfigFlowInstruction { Text = Strings.InstructionSteamId() }
			],
			Links = [new ConfigFlowLink { Label = Strings.ApiKeyLinkLabel(), Url = ApiKeyUrl }],
			Fields = fields,
			AdvancedFields =
			[
				ActionParameter.Array(SteamConfigKeys.LibraryFolders,
					ActionParameter.Folder("folder", Strings.LibraryFolderLabel()),
					Strings.LibraryFoldersLabel(),
					Strings.LibraryFoldersDescription())
			]
		};
	}
}
