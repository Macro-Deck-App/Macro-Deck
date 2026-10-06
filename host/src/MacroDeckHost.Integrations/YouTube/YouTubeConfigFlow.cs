using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.YouTube.Auth;
using MacroDeckHost.Integrations.YouTube.Protocol;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.YouTube.Config;

namespace MacroDeckHost.Integrations.YouTube;

public sealed class YouTubeConfigFlow : IConfigFlow
{
	internal const string ClientStepId = "client";
	internal const string LinkStepId = "link";

	internal const string CredentialsUrl = "https://console.cloud.google.com/apis/credentials";
	internal const string ApiLibraryUrl = "https://console.cloud.google.com/apis/library/youtube.googleapis.com";
	internal const string GuideUrl = "https://docs.macro-deck.app/guide/youtube/";

	private static readonly ILogger _logger = IntegrationLog.For<YouTubeConfigFlow>(YouTubeIntegration.IntegrationId);

	private static readonly TimeSpan _pollWindow = TimeSpan.FromSeconds(90);
	private static readonly TimeSpan _slowDownStep = TimeSpan.FromSeconds(5);

	private readonly Func<IYouTubeOAuthClient> _clientFactory;
	private readonly Func<string, int, string, IYouTubeApiClient> _apiFactory;
	private readonly Func<TimeSpan, CancellationToken, Task> _delay;
	private readonly TimeProvider _time;

	private string _clientId = string.Empty;
	private string _clientSecret = string.Empty;
	private int _dailyQuota = YouTubeQuotaBudget.DefaultDailyLimit;
	private YouTubeDeviceCode? _deviceCode;
	private YouTubeTokens? _pendingTokens;
	private TimeSpan _interval = TimeSpan.FromSeconds(5);

	public YouTubeConfigFlow()
		: this(() => new YouTubeOAuthClient(), CreateApiClient, Task.Delay, TimeProvider.System)
	{
	}

	internal YouTubeConfigFlow(
		Func<IYouTubeOAuthClient> clientFactory,
		Func<string, int, string, IYouTubeApiClient> apiFactory,
		Func<TimeSpan, CancellationToken, Task> delay,
		TimeProvider time)
	{
		_clientFactory = clientFactory;
		_apiFactory = apiFactory;
		_delay = delay;
		_time = time;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(ClientStep()));

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
		=> stepId switch
		{
			ClientStepId => await SubmitClientAsync(input, cancellationToken),
			LinkStepId when _deviceCode is not null => await ContinueLinkAsync(cancellationToken),
			_ => ConfigFlowResult.Error(ClientStep(), Strings.UnknownStep())
		};

	private async Task<ConfigFlowResult> SubmitClientAsync(
		IReadOnlyDictionary<string, object?> input,
		CancellationToken cancellationToken)
	{
		_clientId = ReadText(input, YouTubeConfigKeys.ClientId);
		_clientSecret = ReadText(input, YouTubeConfigKeys.ClientSecret);

		var fieldErrors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		if (_clientId.Length == 0)
		{
			fieldErrors[YouTubeConfigKeys.ClientId] = Strings.EnterClientId();
		}

		if (_clientSecret.Length == 0)
		{
			fieldErrors[YouTubeConfigKeys.ClientSecret] = Strings.EnterClientSecret();
		}

		if (ReadQuota(input) is { } quota)
		{
			_dailyQuota = quota;
		}
		else
		{
			fieldErrors[YouTubeConfigKeys.DailyQuota] = Strings.DailyQuotaInvalid();
		}

		if (fieldErrors.Count > 0)
		{
			return ConfigFlowResult.Error(ClientStep(), fieldErrors: fieldErrors);
		}

		_pendingTokens = null;
		return await RequestDeviceCodeAsync(expired: false, cancellationToken);
	}

	private async Task<ConfigFlowResult> RequestDeviceCodeAsync(bool expired, CancellationToken cancellationToken)
	{
		using var client = _clientFactory();
		try
		{
			_deviceCode = await client.RequestDeviceCodeAsync(_clientId, cancellationToken);
			_interval = _deviceCode.Interval;
			return ConfigFlowResult.Step(LinkStep(_deviceCode, expired));
		}
		catch (YouTubeOAuthRejectedException ex)
		{
			_logger.Warning("Google refused the device authorization request ({ErrorCode})", ex.ErrorCode);
			_deviceCode = null;
			return ConfigFlowResult.Error(ClientStep(), Strings.ClientRejected());
		}
		catch (YouTubeOAuthTransientException ex)
		{
			_logger.Warning(ex, "Could not start the YouTube device authorization");
			_deviceCode = null;
			return ConfigFlowResult.Error(ClientStep(), Strings.CouldNotBeReached());
		}
	}

	private async Task<ConfigFlowResult> RestartWithErrorAsync(
		LocalizedText message,
		CancellationToken cancellationToken)
	{
		var restarted = await RequestDeviceCodeAsync(expired: false, cancellationToken);

		return _deviceCode is null || restarted.Kind is not ConfigFlowResultKind.Step
			? restarted
			: ConfigFlowResult.Error(LinkStep(_deviceCode), message);
	}

	private async Task<ConfigFlowResult> ContinueLinkAsync(CancellationToken cancellationToken)
		=> _pendingTokens is { } tokens
			? await CompleteAsync(tokens, cancellationToken)
			: await PollForTokenAsync(cancellationToken);

	private async Task<ConfigFlowResult> PollForTokenAsync(CancellationToken cancellationToken)
	{
		var deviceCode = _deviceCode!;
		using var client = _clientFactory();

		var waited = TimeSpan.Zero;

		while (true)
		{
			YouTubeTokenPoll poll;
			try
			{
				poll = await client.PollTokenAsync(_clientId, _clientSecret, deviceCode.DeviceCode, cancellationToken);
			}
			catch (YouTubeOAuthTransientException ex)
			{
				_logger.Warning(ex, "Polling the YouTube device authorization failed");
				return ConfigFlowResult.Error(LinkStep(deviceCode), Strings.PollingFailed());
			}

			switch (poll.Status)
			{
				case YouTubeTokenPollStatus.Success when poll.Tokens is not null:
					_pendingTokens = poll.Tokens;
					return await CompleteAsync(poll.Tokens, cancellationToken);

				case YouTubeTokenPollStatus.Denied:
					return await RestartWithErrorAsync(Strings.RequestDeclined(), cancellationToken);

				case YouTubeTokenPollStatus.Rejected:
					_logger.Warning("Google rejected the YouTube client while polling ({ErrorCode})", poll.ErrorCode);
					_deviceCode = null;
					return ConfigFlowResult.Error(ClientStep(), Strings.ClientRejected());

				case YouTubeTokenPollStatus.Expired:
					return await RequestDeviceCodeAsync(expired: true, cancellationToken);

				case YouTubeTokenPollStatus.SlowDown:
					_interval += _slowDownStep;
					break;

				case YouTubeTokenPollStatus.Success:
				case YouTubeTokenPollStatus.Pending:
				default:
					break;
			}

			if (waited + _interval > _pollWindow)
			{
				return ConfigFlowResult.Step(WaitingStep(deviceCode));
			}

			await _delay(_interval, cancellationToken);
			waited += _interval;
		}
	}

	private async Task<ConfigFlowResult> CompleteAsync(YouTubeTokens tokens, CancellationToken cancellationToken)
	{
		YouTubeChannel? channel;
		var api = _apiFactory(_clientId, _dailyQuota, tokens.AccessToken);
		try
		{
			channel = await api.GetMyChannelAsync(cancellationToken);
		}
		catch (Exception ex) when (ex is YouTubeApiException or YouTubeTransientException)
		{
			_logger.Warning(ex, "Could not read the YouTube channel behind a freshly issued token");
			return ConfigFlowResult.Error(LinkStep(_deviceCode!), Strings.ChannelLookupFailed());
		}
		finally
		{
			(api as IDisposable)?.Dispose();
		}

		if (channel is null)
		{
			_pendingTokens = null;
			return await RestartWithErrorAsync(Strings.NoChannel(), cancellationToken);
		}

		var title = channel.Title.Length > 0 ? channel.Title : channel.Id;
		var values = new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
		{
			[YouTubeConfigKeys.ClientId] = ConfigFlowValue.Plain(_clientId),
			[YouTubeConfigKeys.ClientSecret] = ConfigFlowValue.Secret(_clientSecret),
			[YouTubeConfigKeys.DailyQuota] =
				ConfigFlowValue.Plain(_dailyQuota.ToString(CultureInfo.InvariantCulture)),
			[YouTubeConfigKeys.ChannelId] = ConfigFlowValue.Plain(channel.Id),
			[YouTubeConfigKeys.ChannelTitle] = ConfigFlowValue.Plain(title),
			[YouTubeConfigKeys.ChannelHandle] = ConfigFlowValue.Plain(channel.Handle ?? string.Empty),
			[YouTubeConfigKeys.Scopes] = ConfigFlowValue.Plain(string.Join(' ', tokens.Scopes)),
			[YouTubeConfigKeys.ConnectedAt] =
				ConfigFlowValue.Plain(_time.GetUtcNow().ToString("o", CultureInfo.InvariantCulture)),
			[YouTubeConfigKeys.ExpiresAt] =
				ConfigFlowValue.Plain(tokens.ExpiresAt.ToString("o", CultureInfo.InvariantCulture)),
			[YouTubeConfigKeys.AccessToken] = ConfigFlowValue.Secret(tokens.AccessToken),
			[YouTubeConfigKeys.RefreshToken] = ConfigFlowValue.Secret(tokens.RefreshToken)
		};

		_pendingTokens = null;
		return ConfigFlowResult.Complete($"YouTube ({title})", values);
	}

	private ConfigFlowStep ClientStep()
		=> new()
		{
			StepId = ClientStepId,
			Title = Strings.ClientTitle(),
			Description = Strings.ClientDescription(),
			Instructions =
			[
				new ConfigFlowInstruction { Text = Strings.CreateProjectInstruction() },
				new ConfigFlowInstruction { Text = Strings.ConsentScreenInstruction() },
				new ConfigFlowInstruction { Text = Strings.CreateClientInstruction() },
				new ConfigFlowInstruction { Text = Strings.CopyCredentialsInstruction() }
			],
			Links =
			[
				new ConfigFlowLink { Label = Strings.ApiLibraryLinkLabel(), Url = ApiLibraryUrl },
				new ConfigFlowLink { Label = Strings.CredentialsLinkLabel(), Url = CredentialsUrl },
				new ConfigFlowLink { Label = Strings.GuideLinkLabel(), Url = GuideUrl }
			],
			Fields =
			[
				ActionParameter.Text(YouTubeConfigKeys.ClientId,
					label: Strings.ClientIdLabel(),
					placeholder: Strings.ClientIdPlaceholder(),
					defaultValue: _clientId,
					required: true),
				ActionParameter.Secret(YouTubeConfigKeys.ClientSecret,
					label: Strings.ClientSecretLabel(),
					description: Strings.ClientSecretDescription(),
					required: true)
			],
			AdvancedFields =
			[
				ActionParameter.Number(YouTubeConfigKeys.DailyQuota,
					label: Strings.DailyQuotaLabel(),
					description: Strings.DailyQuotaDescription(),
					min: 1,
					step: 1,
					defaultValue: _dailyQuota)
			]
		};

	private static ConfigFlowStep LinkStep(YouTubeDeviceCode deviceCode, bool expired = false)
		=> new()
		{
			StepId = LinkStepId,
			Title = Strings.LinkTitle(),
			Description = expired
				? Strings.LinkDescriptionExpired(count: Minutes(deviceCode.ExpiresIn))
				: Strings.LinkDescription(count: Minutes(deviceCode.ExpiresIn)),
			Instructions =
			[
				new ConfigFlowInstruction { Text = Strings.OpenGoogleInstruction() },
				new ConfigFlowInstruction
				{
					Text = Strings.EnterCodeInstruction(),
					Values = [new ConfigFlowCopyValue { Label = Strings.DeviceCodeLabel(), Value = deviceCode.UserCode }]
				},
				new ConfigFlowInstruction { Text = Strings.ApproveAccessInstruction() },
				new ConfigFlowInstruction { Text = Strings.ComeBackPressContinueInstruction() }
			],
			Links = [new ConfigFlowLink { Label = Strings.OpenGoogleLinkLabel(), Url = deviceCode.VerificationUrl }],
			Fields = []
		};

	private static ConfigFlowStep WaitingStep(YouTubeDeviceCode deviceCode)
		=> new()
		{
			StepId = LinkStepId,
			Title = Strings.WaitingTitle(),
			Description = Strings.WaitingDescription(),
			Instructions =
			[
				new ConfigFlowInstruction { Text = Strings.OpenGoogleInstruction() },
				new ConfigFlowInstruction
				{
					Text = Strings.EnterCodeInstruction(),
					Values = [new ConfigFlowCopyValue { Label = Strings.DeviceCodeLabel(), Value = deviceCode.UserCode }]
				},
				new ConfigFlowInstruction { Text = Strings.ThenPressContinueInstruction() }
			],
			Links = [new ConfigFlowLink { Label = Strings.OpenGoogleLinkLabel(), Url = deviceCode.VerificationUrl }],
			Fields = []
		};

	private static string ReadText(IReadOnlyDictionary<string, object?> input, string key)
		=> (input.GetValueOrDefault(key)?.ToString() ?? string.Empty).Trim();

	private static int? ReadQuota(IReadOnlyDictionary<string, object?> input)
	{
		var raw = input.GetValueOrDefault(YouTubeConfigKeys.DailyQuota) switch
		{
			null => null,
			string text when string.IsNullOrWhiteSpace(text) => null,
			IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
			var other => other.ToString()
		};

		if (raw is null)
		{
			return YouTubeQuotaBudget.DefaultDailyLimit;
		}

		return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
			value >= 1 &&
			value <= int.MaxValue &&
			Math.Abs(value - Math.Round(value)) < double.Epsilon
				? (int)value
				: null;
	}

	private static int Minutes(TimeSpan span) => Math.Max(1, (int)Math.Round(span.TotalMinutes));

	private static IYouTubeApiClient CreateApiClient(string clientId, int dailyQuota, string accessToken)
		=> new YouTubeApiClient(_ => Task.FromResult(accessToken),
			_ => Task.CompletedTask,
			YouTubeQuotaBudgets.Shared.For(clientId, dailyQuota),
			_logger);
}
