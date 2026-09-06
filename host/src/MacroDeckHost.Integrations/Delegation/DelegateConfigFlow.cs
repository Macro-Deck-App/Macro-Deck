using System.Text.Json;
using MacroDeck.Localization;
using MacroDeckHost.Integrations.Delegation.Protocol;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Delegation;

public sealed class DelegateConfigFlow : IConfigFlow
{
	private const string ConnectionStepId = "connection";

	private readonly Func<IDelegateClient> _clientFactory;
	private readonly IIntegrationConfig? _config;
	private readonly TimeProvider _time;

	public DelegateConfigFlow()
		: this(() => new DelegateClient(), null, TimeProvider.System)
	{
	}

	internal DelegateConfigFlow(Func<IDelegateClient> clientFactory, IIntegrationConfig? config, TimeProvider time)
	{
		_clientFactory = clientFactory;
		_config = config;
		_time = time;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(ConnectionStep()));

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
		=> stepId switch
		{
			ConnectionStepId => SubmitConnection(input, cancellationToken),
			_ => Task.FromResult(ConfigFlowResult.Error(ConnectionStep(),
				AppStrings.Integrations.Delegation.Config.UnknownStepError()))
		};

	private static ConfigFlowStep ConnectionStep()
		=> new()
		{
			StepId = ConnectionStepId,
			Title = AppStrings.Integrations.Delegation.Config.ConnectionTitle(),
			Description = AppStrings.Integrations.Delegation.Config.ConnectionDescription(),
			Fields =
			[
				// Plain http by default: the listener this talks to is typically reached over a LAN, not
				// the public internet, so the field must not silently rewrite a bare address to https.
				ActionParameter.Url(DelegateConfigKeys.BaseUrl,
					label: AppStrings.Integrations.Delegation.Config.AddressLabel(),
					description: AppStrings.Integrations.Delegation.Config.AddressDescription(
						example: DelegateEndpoint.ExampleUrl),
					placeholder: DelegateEndpoint.ExampleUrl,
					required: true,
					autoPrefixHttps: false),
				ActionParameter.Text(DelegateConfigKeys.Username,
					label: AppStrings.Integrations.Delegation.Config.UsernameLabel(),
					description: AppStrings.Integrations.Delegation.Config.UsernameDescription(),
					required: true),
				ActionParameter.Secret(DelegateConfigKeys.Password,
					label: AppStrings.Integrations.Delegation.Config.PasswordLabel(),
					description: AppStrings.Integrations.Delegation.Config.PasswordDescription(),
					required: true)
			]
		};

	private async Task<ConfigFlowResult> SubmitConnection(
		IReadOnlyDictionary<string, object?> input,
		CancellationToken cancellationToken)
	{
		var baseUrlRaw = (input.GetValueOrDefault(DelegateConfigKeys.BaseUrl) as string ?? string.Empty).Trim();
		var username = (input.GetValueOrDefault(DelegateConfigKeys.Username) as string ?? string.Empty).Trim();
		var password = input.GetValueOrDefault(DelegateConfigKeys.Password) as string ?? string.Empty;

		var fieldErrors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		var uri = DelegateEndpoint.TryBuild(baseUrlRaw);
		if (uri is null)
		{
			fieldErrors[DelegateConfigKeys.BaseUrl] =
				AppStrings.Integrations.Delegation.Config.AddressRequired(example: DelegateEndpoint.ExampleUrl);
		}

		if (username.Length == 0)
		{
			fieldErrors[DelegateConfigKeys.Username] = AppStrings.Integrations.Delegation.Config.UsernameRequired();
		}

		if (password.Length == 0)
		{
			fieldErrors[DelegateConfigKeys.Password] = AppStrings.Integrations.Delegation.Config.PasswordRequired();
		}

		if (fieldErrors.Count > 0 || uri is null)
		{
			return ConfigFlowResult.Error(ConnectionStep(), fieldErrors: fieldErrors);
		}

		using var client = _clientFactory();

		try
		{
			await client.ProbeAsync(uri, cancellationToken);
		}
		catch (DelegateTlsException)
		{
			return ConfigFlowResult.Error(ConnectionStep(),
				fieldErrors: FieldError(DelegateConfigKeys.BaseUrl,
					AppStrings.Integrations.Delegation.Config.CertificateNotValidated(host: uri.Host)));
		}
		catch (DelegateNotMacroDeckException)
		{
			return ConfigFlowResult.Error(ConnectionStep(),
				fieldErrors: FieldError(DelegateConfigKeys.BaseUrl,
					AppStrings.Integrations.Delegation.Config.NotMacroDeck()));
		}
		catch (DelegateClientException) // unreachable and anything else transport-level
		{
			return ConfigFlowResult.Error(ConnectionStep(),
				fieldErrors: FieldError(DelegateConfigKeys.BaseUrl, Unreachable(uri)));
		}

		DelegateLoginResult login;
		try
		{
			login = await client.LoginAsync(uri, username, password, cancellationToken);
		}
		catch (DelegateUnauthorizedException)
		{
			return ConfigFlowResult.Error(ConnectionStep(),
				fieldErrors: FieldError(DelegateConfigKeys.Password,
					AppStrings.Integrations.Delegation.Config.InvalidCredentials()));
		}
		catch (DelegateRateLimitedException ex)
		{
			return ConfigFlowResult.Error(ConnectionStep(), FormatRateLimited(ex.RetryAfter));
		}
		catch (DelegateClientException)
		{
			return ConfigFlowResult.Error(ConnectionStep(),
				fieldErrors: FieldError(DelegateConfigKeys.BaseUrl, Unreachable(uri)));
		}

		DelegateConnectionInfo info;
		try
		{
			info = await client.GetConnectionInfoAsync(uri, login.AccessToken, cancellationToken);
		}
		catch (DelegateForbiddenException)
		{
			return ConfigFlowResult.Error(ConnectionStep(), AppStrings.Integrations.Delegation.Config.Forbidden());
		}
		catch (DelegateClientException)
		{
			return ConfigFlowResult.Error(ConnectionStep(),
				fieldErrors: FieldError(DelegateConfigKeys.BaseUrl, Unreachable(uri)));
		}

		if (DelegateEndpoint.IsThisMachine(uri, info.InstanceName))
		{
			return ConfigFlowResult.Error(ConnectionStep(), AppStrings.Integrations.Delegation.Config.SameMachine());
		}

		var scripts = new Dictionary<string, DelegateScriptSummary>(StringComparer.Ordinal);
		try
		{
			foreach (var script in await client.GetScriptsAsync(uri, login.AccessToken, cancellationToken))
			{
				scripts[script.Id] = script;
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception)
		{
		}

		var (instanceId, instanceKey) = await ResolveInstanceIdentityAsync(uri, info.InstanceName, cancellationToken);

		var values = new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
		{
			[DelegateConfigKeys.BaseUrl] = ConfigFlowValue.Plain(uri.ToString()),
			[DelegateConfigKeys.Username] = ConfigFlowValue.Plain(username),
			[DelegateConfigKeys.Password] = ConfigFlowValue.Secret(password),
			[DelegateConfigKeys.InstanceId] = ConfigFlowValue.Plain(instanceId),
			[DelegateConfigKeys.MachineName] = ConfigFlowValue.Plain(info.InstanceName),
			[DelegateConfigKeys.InstanceKey] = ConfigFlowValue.Plain(instanceKey),
			[DelegateConfigKeys.ConfiguredAt] = ConfigFlowValue.Plain(_time.GetUtcNow().ToString("o")),
			[DelegateConfigKeys.RemoteScripts]
				= ConfigFlowValue.Plain(JsonSerializer.Serialize(scripts, DelegateJson.Options))
		};

		return ConfigFlowResult.Complete($"Macro Deck ({info.InstanceName})", values);
	}

	private async Task<(string InstanceId, string InstanceKey)> ResolveInstanceIdentityAsync(
		Uri uri,
		string machineName,
		CancellationToken cancellationToken)
	{
		if (_config is not null)
		{
			var entries = await _config.GetEntriesAsync(cancellationToken);
			var candidates = new List<(string InstanceId, DateTimeOffset ConfiguredAt)>();

			foreach (var entry in entries)
			{
				var storedInstanceId =
					await _config.GetStringAsync(entry.Id, DelegateConfigKeys.InstanceId, cancellationToken);
				if (string.IsNullOrEmpty(storedInstanceId))
				{
					continue;
				}

				var storedMachineName =
					await _config.GetStringAsync(entry.Id, DelegateConfigKeys.MachineName, cancellationToken);
				var storedBaseUrl =
					await _config.GetStringAsync(entry.Id, DelegateConfigKeys.BaseUrl, cancellationToken);
				var storedConfiguredAt =
					await _config.GetStringAsync(entry.Id, DelegateConfigKeys.ConfiguredAt, cancellationToken);

				var machineMatches = string.Equals(storedMachineName, machineName, StringComparison.Ordinal);
				var addressMatches = DelegateEndpoint.TryBuild(storedBaseUrl) is { } storedUri &&
					string.Equals(storedUri.Authority, uri.Authority, StringComparison.OrdinalIgnoreCase);

				if (machineMatches || addressMatches)
				{
					candidates.Add((storedInstanceId,
						DelegateRemote.ParseTimestamp(storedConfiguredAt) ?? DateTimeOffset.MinValue));
				}
			}

			if (candidates.Count > 0)
			{
				var instanceId = candidates.OrderByDescending(c => c.ConfiguredAt).First().InstanceId;
				return (instanceId, DelegateSlug.For(machineName, instanceId));
			}
		}

		var freshId = Guid.NewGuid().ToString("N");
		return (freshId, DelegateSlug.For(machineName, freshId));
	}

	private static Dictionary<string, LocalizedText> FieldError(string field, LocalizedText message)
		=> new(StringComparer.Ordinal) { [field] = message };

	private static LocalizedText Unreachable(Uri uri)
		=> AppStrings.Integrations.Delegation.Config.Unreachable(host: uri.Host);

	private static LocalizedText FormatRateLimited(TimeSpan wait)
		=> wait.TotalMinutes >= 1
			? AppStrings.Integrations.Delegation.Config.RateLimitedMinutes(count: (int)Math.Ceiling(wait.TotalMinutes))
			: AppStrings.Integrations.Delegation.Config.RateLimitedSeconds(count: (int)Math.Max(1,
				Math.Ceiling(wait.TotalSeconds)));
}
