using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.Imap.Protocol;
using MacroDeckHost.Localization;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Imap.Config;

namespace MacroDeckHost.Integrations.Imap.Configuration;

internal sealed class ImapConfigFlow : IConfigFlow
{
	internal const string StepId = "account";

	private static readonly ILogger _logger = IntegrationLog.For<ImapConfigFlow>(ImapIntegration.IntegrationId);
	private static readonly TimeSpan _defaultTestTimeout = TimeSpan.FromSeconds(20);

	private readonly IImapSessionFactory _factory;
	private readonly TimeSpan _testTimeout;

	internal ImapConfigFlow(IImapSessionFactory factory, TimeSpan? testTimeout = null)
	{
		_factory = factory;
		_testTimeout = testTimeout ?? _defaultTestTimeout;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
	{
		return Task.FromResult(ConfigFlowResult.Step(AccountStep(context)));
	}

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		return stepId == StepId
			? await SubmitAccountAsync(input, context, cancellationToken).ConfigureAwait(false)
			: ConfigFlowResult.Error(AccountStep(context), Strings.UnknownStep());
	}

	private async Task<ConfigFlowResult> SubmitAccountAsync(
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		var name = (context as IConfigFlowEntryContext)?.EntryTitle
			?? Read(input, ImapConfigKeys.ConfigurationName).Trim();
		var preset = ImapProviderPresets.Find(Read(input, ImapConfigKeys.Provider));
		var host = preset?.Host ?? Read(input, ImapConfigKeys.Host).Trim();
		var port = preset?.Port ?? ImapAccountSettings.ParsePort(input.GetValueOrDefault(ImapConfigKeys.Port));
		var security = preset?.Security ?? ImapAccountSettings.ParseSecurity(Read(input, ImapConfigKeys.Security));
		var username = Read(input, ImapConfigKeys.Username).Trim();
		var password = Read(input, ImapConfigKeys.Password);
		var folder = ImapAccountSettings.NormalizeFolder(Read(input, ImapConfigKeys.Folder));

		var fieldErrors = new Dictionary<string, LocalizedText>();
		if (string.IsNullOrWhiteSpace(name))
		{
			fieldErrors[ImapConfigKeys.ConfigurationName] = AppStrings.Errors.Config.TitleRequired();
		}

		if (!IsHostName(host))
		{
			fieldErrors[ImapConfigKeys.Host] = Strings.EnterHost();
		}

		if (port is not (>= 1 and <= 65535))
		{
			fieldErrors[ImapConfigKeys.Port] = Strings.EnterValidPort();
		}

		if (username.Length == 0)
		{
			fieldErrors[ImapConfigKeys.Username] = Strings.EnterUsername();
		}

		if (password.Length == 0)
		{
			fieldErrors[ImapConfigKeys.Password] = Strings.EnterPassword();
		}

		if (fieldErrors.Count > 0)
		{
			return ConfigFlowResult.Error(AccountStep(context), fieldErrors: fieldErrors);
		}

		var settings = new ImapAccountSettings(host, port!.Value, security, username, password, folder);
		if (await TestAsync(settings, cancellationToken).ConfigureAwait(false) is { } failure)
		{
			return failure.Field is { } field
				? ConfigFlowResult.Error(AccountStep(context),
					fieldErrors: new Dictionary<string, LocalizedText> { [field] = failure.Message })
				: ConfigFlowResult.Error(AccountStep(context), failure.Message);
		}

		return ConfigFlowResult.Complete(name,
			new Dictionary<string, ConfigFlowValue>
			{
				[ImapConfigKeys.Provider] = ConfigFlowValue.Plain(
					ImapProviderPresets.Matching(host, port.Value, security)?.Id ?? ImapConfigKeys.ProviderCustom),
				[ImapConfigKeys.Host] = ConfigFlowValue.Plain(host),
				[ImapConfigKeys.Port] = ConfigFlowValue.Plain(port.Value.ToString(CultureInfo.InvariantCulture)),
				[ImapConfigKeys.Security] = ConfigFlowValue.Plain(ImapAccountSettings.FormatSecurity(security)),
				[ImapConfigKeys.Username] = ConfigFlowValue.Plain(username),
				[ImapConfigKeys.Folder] = ConfigFlowValue.Plain(folder)
			});
	}

	private async Task<TestFailure?> TestAsync(ImapAccountSettings settings, CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_testTimeout);

		var session = _factory.Create();
		try
		{
			await session.OpenAsync(settings, timeout.Token).ConfigureAwait(false);
			return null;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return new TestFailure(null, Strings.ConnectionTimedOut(settings.Host));
		}
		catch (ImapSessionException ex)
		{
			_logger.Information("IMAP connection test for {Account} failed ({Failure}): {Message}",
				settings,
				ex.Failure,
				ex.Message);
			return ex.Failure switch
			{
				ImapFailure.Authentication => new TestFailure(ImapConfigKeys.Password, Strings.SignInRejected()),
				ImapFailure.FolderNotFound => new TestFailure(ImapConfigKeys.Folder,
					Strings.FolderNotFound(settings.Folder)),
				ImapFailure.Certificate => new TestFailure(null, Strings.CertificateFailed(settings.Host)),
				_ => new TestFailure(null,
					Strings.ConnectionFailed(settings.Host,
						settings.Port.ToString(CultureInfo.InvariantCulture)))
			};
		}
		finally
		{
			try
			{
				await session.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Closing the IMAP test session failed");
			}
		}
	}

	private static bool IsHostName(string host)
	{
		return host.Length > 0 && Uri.CheckHostName(host) != UriHostNameType.Unknown;
	}

	private static string Read(IReadOnlyDictionary<string, object?> input, string key)
	{
		return input.GetValueOrDefault(key) as string ?? string.Empty;
	}

	private static ConfigFlowStep AccountStep(IConfigFlowContext context)
	{
		var fields = new List<ActionParameter>();
		if ((context as IConfigFlowEntryContext)?.EntryTitle is null)
		{
			fields.Add(ActionParameter.Text(ImapConfigKeys.ConfigurationName,
				AppStrings.Integrations.Detail.ConfigurationNameLabel(),
				placeholder: Strings.ConfigurationNamePlaceholder(),
				required: true));
		}

		fields.AddRange([
			ActionParameter.Choice(ImapConfigKeys.Provider,
				[
					new ActionParameterOption
						{ Value = ImapConfigKeys.ProviderCustom, Label = Strings.ProviderCustom() },
					.. ImapProviderPresets.All.Select(preset =>
						new ActionParameterOption { Value = preset.Id, Label = preset.Name })
				],
				Strings.ProviderLabel(),
				Strings.ProviderDescription(),
				ImapConfigKeys.ProviderCustom,
				true),
			ActionParameter.Text(ImapConfigKeys.Host,
					Strings.HostLabel(),
					Strings.HostDescription(),
					"imap.example.com",
					required: true)
				.OnlyWhen(ImapConfigKeys.Provider, ImapConfigKeys.ProviderCustom),
			ActionParameter.Number(ImapConfigKeys.Port,
					Strings.PortLabel(),
					Strings.PortDescription(),
					1,
					65535,
					1,
					ImapAccountSettings.DefaultPort,
					true)
				.OnlyWhen(ImapConfigKeys.Provider, ImapConfigKeys.ProviderCustom),
			ActionParameter.Choice(ImapConfigKeys.Security,
					[
						new ActionParameterOption
							{ Value = ImapConfigKeys.SecuritySslTls, Label = Strings.SecuritySslTls() },
						new ActionParameterOption
							{ Value = ImapConfigKeys.SecurityStartTls, Label = Strings.SecurityStartTls() }
					],
					Strings.SecurityLabel(),
					Strings.SecurityDescription(),
					ImapConfigKeys.SecuritySslTls,
					true)
				.OnlyWhen(ImapConfigKeys.Provider, ImapConfigKeys.ProviderCustom),
			ActionParameter.Text(ImapConfigKeys.Username,
				Strings.UsernameLabel(),
				Strings.UsernameDescription(),
				required: true),
			ActionParameter.Secret(ImapConfigKeys.Password,
				Strings.PasswordLabel(),
				Strings.PasswordDescription()),
			ActionParameter.Text(ImapConfigKeys.Folder,
				Strings.FolderLabel(),
				Strings.FolderDescription(),
				ImapAccountSettings.DefaultFolder,
				ImapAccountSettings.DefaultFolder)
		]);

		return new ConfigFlowStep
		{
			StepId = StepId,
			Title = Strings.ConnectTitle(),
			Description = Strings.ConnectDescription(),
			Instructions = [new ConfigFlowInstruction { Text = Strings.AppPasswordInstruction() }],
			Fields = fields
		};
	}

	private sealed record TestFailure(string? Field, LocalizedText Message);
}
