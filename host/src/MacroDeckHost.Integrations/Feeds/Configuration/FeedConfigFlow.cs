using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.Feeds.Protocol;
using MacroDeckHost.Localization;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.Feeds.Errors;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Feeds.Config;

namespace MacroDeckHost.Integrations.Feeds.Configuration;

internal static class FeedErrorText
{
	public static LocalizedText Describe(string? error)
	{
		return error switch
		{
			FeedErrorCodes.Timeout => Errors.Timeout(),
			FeedErrorCodes.BlockedAddress => Errors.BlockedAddress(),
			FeedErrorCodes.InvalidFeed => Errors.InvalidFeed(),
			FeedErrorCodes.TooLarge => Errors.TooLarge(),
			{ } code when code.StartsWith("http-", StringComparison.Ordinal) => Errors.HttpStatus(code[5..]),
			_ => Errors.Network()
		};
	}
}

public sealed class FeedConfigFlow : IConfigFlow
{
	private const string StepId = "feed";

	private readonly IFeedFetcher _fetcher;

	internal FeedConfigFlow(IFeedFetcher fetcher)
	{
		_fetcher = fetcher;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		IntegrationHttp.Use(context.Http);
		return Task.FromResult(ConfigFlowResult.Step(FeedStep(context)));
	}

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNull(context);

		if (stepId != StepId)
		{
			return ConfigFlowResult.Error(FeedStep(context), Strings.UnknownStep());
		}

		var url = FeedSettings.TryParseUrl(FeedSettings.ReadText(input.GetValueOrDefault(FeedConfigKeys.Url)));
		if (url is null)
		{
			return ConfigFlowResult.Error(FeedStep(context),
				fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
				{
					[FeedConfigKeys.Url] = Strings.EnterUrl(FeedSettings.ExampleUrl)
				});
		}

		var allowLocal = FeedSettings.ReadBool(input.GetValueOrDefault(FeedConfigKeys.AllowLocalNetwork));
		var fetch = await _fetcher.FetchAsync(new FeedRequest(url, allowLocal), cancellationToken)
			.ConfigureAwait(false);

		if (fetch.Status != FeedFetchStatus.Ok)
		{
			return Failed(context, fetch.Error);
		}

		ParsedFeed parsed;
		try
		{
			parsed = FeedParser.Parse(fetch.Content ?? [], fetch.FinalUrl ?? url, cancellationToken);
		}
		catch (FeedParseException)
		{
			return Failed(context, FeedErrorCodes.InvalidFeed);
		}

		var entryTitle = (context as IConfigFlowEntryContext)?.EntryTitle;
		var title = entryTitle
			?? FeedSettings.ReadText(input.GetValueOrDefault(FeedConfigKeys.Name))
			?? parsed.Title
			?? url.Host;
		var requested = FeedSettings.ReadNumber(input.GetValueOrDefault(FeedConfigKeys.RefreshMinutes));
		var refresh = FeedSettings.ClampRefresh(requested);

		return ConfigFlowResult.Complete(FeedText.Clip(title, 100),
			new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
			{
				[FeedConfigKeys.Url] = ConfigFlowValue.Plain(url.AbsoluteUri),
				[FeedConfigKeys.RefreshMinutes] = ConfigFlowValue.Plain(refresh.ToString(CultureInfo.InvariantCulture))
			});
	}

	private static ConfigFlowResult Failed(IConfigFlowContext context, string? error)
	{
		var message = FeedErrorText.Describe(error);
		return error == FeedErrorCodes.BlockedAddress
			? ConfigFlowResult.Error(FeedStep(context),
				fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
				{
					[FeedConfigKeys.AllowLocalNetwork] = message
				})
			: ConfigFlowResult.Error(FeedStep(context), message);
	}

	private static ConfigFlowStep FeedStep(IConfigFlowContext context)
	{
		var fields = new List<ActionParameter>
		{
			ActionParameter.Url(FeedConfigKeys.Url,
				Strings.UrlLabel(),
				Strings.UrlDescription(),
				FeedSettings.ExampleUrl,
				true,
				true)
		};

		if ((context as IConfigFlowEntryContext)?.EntryTitle is null)
		{
			fields.Add(ActionParameter.Text(FeedConfigKeys.Name,
				AppStrings.Integrations.Detail.ConfigurationNameLabel(),
				Strings.NameDescription()));
		}

		fields.AddRange([
			ActionParameter.Text(FeedConfigKeys.Group, Strings.GroupLabel(), Strings.GroupDescription()),
			ActionParameter.Number(FeedConfigKeys.RefreshMinutes,
				Strings.RefreshLabel(),
				Strings.RefreshDescription(),
				FeedSettings.MinRefreshMinutes,
				FeedSettings.MaxRefreshMinutes,
				1,
				FeedSettings.DefaultRefreshMinutes)
		]);

		return new ConfigFlowStep
		{
			StepId = StepId,
			Title = Strings.Title(),
			Description = Strings.Description(),
			Fields = fields,
			AdvancedFields =
			[
				ActionParameter.Text(FeedConfigKeys.IncludeKeywords,
					Strings.IncludeLabel(),
					Strings.IncludeDescription()),
				ActionParameter.Text(FeedConfigKeys.ExcludeKeywords,
					Strings.ExcludeLabel(),
					Strings.ExcludeDescription()),
				ActionParameter.Text(FeedConfigKeys.Categories,
					Strings.CategoriesLabel(),
					Strings.CategoriesDescription()),
				ActionParameter.Toggle(FeedConfigKeys.AllowLocalNetwork,
					Strings.AllowLocalNetworkLabel(),
					Strings.AllowLocalNetworkDescription())
			]
		};
	}
}
