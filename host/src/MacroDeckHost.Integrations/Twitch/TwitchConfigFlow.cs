using System.Globalization;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Twitch;

public sealed class TwitchConfigFlow : IConfigFlow
{
	private const string LinkStepId = "link";

	private static readonly ILogger _logger = IntegrationLog.For<TwitchConfigFlow>(TwitchIntegration.IntegrationId);

	private static readonly TimeSpan _pollWindow = TimeSpan.FromSeconds(90);
	private static readonly TimeSpan _slowDownStep = TimeSpan.FromSeconds(5);

	private readonly Func<ITwitchOAuthClient> _clientFactory;
	private readonly Func<TimeSpan, CancellationToken, Task> _delay;

	private string _clientId = TwitchOAuthEndpoints.MacroDeckClientId;
	private string _enteredClientId = string.Empty;
	private TwitchDeviceCode? _deviceCode;
	private TimeSpan _interval = TimeSpan.FromSeconds(5);

	public TwitchConfigFlow()
		: this(() => new TwitchOAuthClient(), Task.Delay)
	{
	}

	internal TwitchConfigFlow(Func<ITwitchOAuthClient> clientFactory, Func<TimeSpan, CancellationToken, Task> delay)
	{
		_clientFactory = clientFactory;
		_delay = delay;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> RequestDeviceCode(cancellationToken);

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		if (stepId is not LinkStepId)
		{
			return ConfigFlowResult.Error(RetryStep(), AppStrings.Integrations.Twitch.Config.UnknownStep());
		}

		var entered = (input.GetValueOrDefault(TwitchConfigKeys.ClientId) as string ?? string.Empty).Trim();
		_enteredClientId = entered;
		var effective = entered.Length > 0 ? entered : TwitchOAuthEndpoints.MacroDeckClientId;

		if (!string.Equals(effective, _clientId, StringComparison.Ordinal) || _deviceCode is null)
		{
			_clientId = effective;
			return await RequestDeviceCode(cancellationToken);
		}

		return await PollForToken(cancellationToken);
	}

	private async Task<ConfigFlowResult> RequestDeviceCode(CancellationToken cancellationToken)
	{
		using var client = CreateClient();
		try
		{
			_deviceCode = await client.RequestDeviceCodeAsync(_clientId, TwitchScopes.Requested, cancellationToken);
			_interval = _deviceCode.Interval;
			return ConfigFlowResult.Step(LinkStep(_deviceCode));
		}
		catch (TwitchOAuthRejectedException ex)
		{
			_logger.Warning(ex, "Twitch refused the device authorization request");

			return ConfigFlowResult.Error(RetryStep(),
				UsesOwnApplication
					? AppStrings.Integrations.Twitch.Config.ClientIdRejected()
					: AppStrings.Integrations.Twitch.Config.DefaultApplicationRejected());
		}
		catch (TwitchOAuthTransientException ex)
		{
			_logger.Warning(ex, "Could not start the Twitch device authorization");
			return ConfigFlowResult.Error(RetryStep(),
				AppStrings.Integrations.Twitch.Config.CouldNotBeReached());
		}
	}

	private async Task<ConfigFlowResult> PollForToken(CancellationToken cancellationToken)
	{
		var deviceCode = _deviceCode!;
		using var client = CreateClient();

		var waited = TimeSpan.Zero;

		while (true)
		{
			TwitchTokenPollResult poll;
			try
			{
				poll = await client.PollTokenAsync(_clientId,
					TwitchScopes.Requested,
					deviceCode.DeviceCode,
					cancellationToken);
			}
			catch (TwitchOAuthTransientException ex)
			{
				_logger.Warning(ex, "Polling the Twitch device authorization failed");
				return ConfigFlowResult.Error(LinkStep(deviceCode),
					AppStrings.Integrations.Twitch.Config.PollingFailed());
			}

			switch (poll.Status)
			{
				case TwitchTokenPollStatus.Success when poll.Tokens is not null:
					return await CompleteAsync(client, deviceCode, poll.Tokens, cancellationToken);

				case TwitchTokenPollStatus.Denied:
					return ConfigFlowResult.Error(LinkStep(deviceCode),
						AppStrings.Integrations.Twitch.Config.RequestDeclined());

				case TwitchTokenPollStatus.Expired:
					// A spent or timed-out code cannot be polled again; start a fresh one so the user
					// only has to approve, not restart the whole setup.
					return await RequestExpiredReplacement(cancellationToken);

				case TwitchTokenPollStatus.SlowDown:
					_interval += _slowDownStep;
					break;

				case TwitchTokenPollStatus.Success:
				case TwitchTokenPollStatus.Pending:
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

	private async Task<ConfigFlowResult> RequestExpiredReplacement(CancellationToken cancellationToken)
	{
		var result = await RequestDeviceCode(cancellationToken);

		return _deviceCode is null || result.Kind is not ConfigFlowResultKind.Step
			? result
			: ConfigFlowResult.Step(LinkStep(_deviceCode, expired: true));
	}

	private async Task<ConfigFlowResult> CompleteAsync(
		ITwitchOAuthClient client,
		TwitchDeviceCode deviceCode,
		TwitchTokens tokens,
		CancellationToken cancellationToken)
	{
		TwitchTokenIdentity identity;
		try
		{
			identity = await client.ValidateAsync(tokens.AccessToken, cancellationToken);
		}
		catch (Exception ex) when (ex is TwitchOAuthTransientException or TwitchOAuthRejectedException)
		{
			_logger.Warning(ex, "Could not resolve the account behind a freshly issued Twitch token");
			return ConfigFlowResult.Error(LinkStep(deviceCode),
				AppStrings.Integrations.Twitch.Config.AccountResolutionFailed());
		}

		var granted = tokens.Scopes.Count > 0 ? tokens.Scopes : identity.Scopes;

		var values = new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
		{
			[TwitchConfigKeys.ClientId] = ConfigFlowValue.Plain(_clientId),
			[TwitchConfigKeys.UserId] = ConfigFlowValue.Plain(identity.UserId),
			[TwitchConfigKeys.Login] = ConfigFlowValue.Plain(identity.Login),

			[TwitchConfigKeys.DisplayName] = ConfigFlowValue.Plain(identity.Login),
			[TwitchConfigKeys.Scopes] = ConfigFlowValue.Plain(string.Join(' ', granted)),
			[TwitchConfigKeys.ConnectedAt] =
				ConfigFlowValue.Plain(DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
			[TwitchConfigKeys.ExpiresAt] =
				ConfigFlowValue.Plain(tokens.ExpiresAt.ToString("o", CultureInfo.InvariantCulture)),
			[TwitchConfigKeys.AccessToken] = ConfigFlowValue.Secret(tokens.AccessToken),
			[TwitchConfigKeys.RefreshToken] = ConfigFlowValue.Secret(tokens.RefreshToken)
		};

		return ConfigFlowResult.Complete($"Twitch ({identity.Login})", values);
	}

	private bool UsesOwnApplication
		=> !string.Equals(_clientId, TwitchOAuthEndpoints.MacroDeckClientId, StringComparison.Ordinal);

	private ITwitchOAuthClient CreateClient() => _clientFactory();

	private ConfigFlowStep LinkStep(TwitchDeviceCode deviceCode, bool expired = false)
		=> new()
		{
			StepId = LinkStepId,
			Title = AppStrings.Integrations.Twitch.Config.LinkTitle(),
			Description = expired
				? AppStrings.Integrations.Twitch.Config.LinkDescriptionExpired(count: Minutes(deviceCode.ExpiresIn))
				: AppStrings.Integrations.Twitch.Config.LinkDescription(count: Minutes(deviceCode.ExpiresIn)),
			Instructions =
			[
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.Twitch.Config.OpenTwitchCodeFilledInstruction() },
				new ConfigFlowInstruction
				{
					Text = AppStrings.Integrations.Twitch.Config.EnterCodeInstruction(),
					Values =
					[
						new ConfigFlowCopyValue
						{
							Label = AppStrings.Integrations.Twitch.Config.DeviceCodeLabel(), Value = deviceCode.UserCode
						}
					]
				},
				new ConfigFlowInstruction { Text = AppStrings.Integrations.Twitch.Config.ApproveAccessInstruction() },
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.Twitch.Config.ComeBackPressContinueInstruction() }
			],
			Links =
			[
				new ConfigFlowLink
				{
					Label = AppStrings.Integrations.Twitch.Config.OpenTwitchApproveLinkLabel(),
					Url = deviceCode.VerificationUri
				}
			],
			Fields = [],
			AdvancedFields = [ClientIdField()]
		};

	private ConfigFlowStep WaitingStep(TwitchDeviceCode deviceCode)
		=> new()
		{
			StepId = LinkStepId,
			Title = AppStrings.Integrations.Twitch.Config.WaitingTitle(),
			Description = AppStrings.Integrations.Twitch.Config.WaitingDescription(),
			Instructions =
			[
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.Twitch.Config.OpenTwitchApproveInstruction() },
				new ConfigFlowInstruction
				{
					Text = AppStrings.Integrations.Twitch.Config.EnterCodeInstruction(),
					Values =
					[
						new ConfigFlowCopyValue
						{
							Label = AppStrings.Integrations.Twitch.Config.DeviceCodeLabel(), Value = deviceCode.UserCode
						}
					]
				},
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.Twitch.Config.ThenPressContinueInstruction() }
			],
			Links =
			[
				new ConfigFlowLink
				{
					Label = AppStrings.Integrations.Twitch.Config.OpenTwitchApproveLinkLabel(),
					Url = deviceCode.VerificationUri
				}
			],
			Fields = [],
			AdvancedFields = [ClientIdField()]
		};

	private ConfigFlowStep RetryStep()
		=> new()
		{
			StepId = LinkStepId,
			Title = AppStrings.Integrations.Twitch.Config.LinkTitle(),
			Description = AppStrings.Integrations.Twitch.Config.RetryDescription(),
			Links =
			[
				new ConfigFlowLink
				{
					Label = AppStrings.Integrations.Twitch.Config.DeveloperConsoleLinkLabel(),
					Url = TwitchOAuthEndpoints.DeveloperConsole
				}
			],
			Fields = [],
			AdvancedFields = [ClientIdField()]
		};

	private ActionParameter ClientIdField()
		=> ActionParameter.Text(TwitchConfigKeys.ClientId,
			label: AppStrings.Integrations.Twitch.Config.ClientIdLabel(),
			description: AppStrings.Integrations.Twitch.Config.ClientIdDescription(),
			defaultValue: _enteredClientId);

	private static int Minutes(TimeSpan span) => Math.Max(1, (int)Math.Round(span.TotalMinutes));
}
