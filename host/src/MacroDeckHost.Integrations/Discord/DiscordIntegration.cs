using System.Globalization;
using MacroDeckHost.Integrations.Discord.Actions;
using MacroDeckHost.Integrations.Discord.Rpc;
using MacroDeckHost.Integrations.Discord.Webhooks;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Discord;

[MacroDeckIntegration]
public sealed class DiscordIntegration
	: IIntegration,
		IConfigFlowProvider,
		IVariableProvider,
		IIntegrationIconProvider,
		IEventProvider,
		IDynamicEventOptionsProvider,
		IIntegrationIssueProvider,
		IMigrationProvider,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.discord";

	internal const string ReauthorizationIssueId = "reauthorization-required";
	internal const string PrivilegeMismatchIssueId = "privilege-mismatch";
	internal const string VoiceSettingsIgnoredIssueId = "voice-settings-ignored";

	private const string InputVolumeId = "discord-input-volume";
	private const string OutputVolumeId = "discord-output-volume";

	private const string PercentUnit = "%";

	// Discord's own limits: an input is attenuation only, while an output may be amplified to double.
	private const double MaxInputVolume = 100d;
	private const double MaxOutputVolume = 200d;

	private static readonly ILogger _logger = IntegrationLog.For<DiscordIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private readonly IDiscordOAuthClient _oauth;

	private IIntegrationContext? _context;
	private DiscordConnection? _connection;
	private DiscordEventEmitter? _events;
	private Guid? _entryId;

	public DiscordIntegration()
		: this(new DiscordOAuthClient(), new DiscordWebhookClient())
	{
	}

	internal DiscordIntegration(IDiscordOAuthClient oauth, IDiscordWebhookClient webhooks)
	{
		_oauth = oauth;
		Actions = DiscordActions.Create(() => _connection, webhooks);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => "Discord";

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public IReadOnlyList<EventDefinition> EventDefinitions => DiscordEventDefinitions.All;

	public bool AllowsMultipleConfigurations => false;

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("discord_is_connected", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.IsConnected()
			},
		VariableDefinition.Eager("discord_user_name", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(30))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.UserName()
			},
		VariableDefinition.Eager("discord_is_self_muted",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(1))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.IsSelfMuted()
			},
		VariableDefinition.Eager("discord_is_self_deafened",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(1))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.IsSelfDeafened()
			},
		VariableDefinition.Eager("discord_is_server_muted",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(1))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.IsServerMuted()
			},
		VariableDefinition.Eager("discord_is_server_deafened",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(1))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.IsServerDeafened()
			},
		VariableDefinition.Eager("discord_is_muted", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(1))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.IsMuted()
			},
		VariableDefinition.Eager("discord_is_deafened", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(1))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.IsDeafened()
			},
		VariableDefinition.Eager("discord_is_speaking", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(1))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.IsSpeaking()
			},
		VariableDefinition.Eager("discord_is_in_voice_channel",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.IsInVoiceChannel()
			},
		VariableDefinition.Eager("discord_voice_channel", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.VoiceChannel()
			},
		VariableDefinition.Eager("discord_voice_server", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.VoiceServer()
			},
		VariableDefinition.Eager("discord_voice_connection_state",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.VoiceConnectionState()
			},
		VariableDefinition.Eager("discord_voice_ping", VariableType.Numeric, 0, TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.VoicePing()
			},
		VariableDefinition.Eager("discord_input_volume", VariableType.Numeric, 0, TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.InputVolume(),
				Unit = PercentUnit,
				SemanticKind = VariableSemanticKinds.Percentage,
				Write = new VariableWriteCapability()
			},
		VariableDefinition.Eager("discord_output_volume", VariableType.Numeric, 0, TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.OutputVolume(),
				Unit = PercentUnit,
				SemanticKind = VariableSemanticKinds.Percentage,
				Write = new VariableWriteCapability()
			},
		VariableDefinition.Eager("discord_voice_mode", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.VoiceMode()
			},
		VariableDefinition.Eager("discord_noise_suppression",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.NoiseSuppression()
			},
		VariableDefinition.Eager("discord_echo_cancellation",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.EchoCancellation()
			},
		VariableDefinition.Eager("discord_automatic_gain_control",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.Discord.Variables.AutomaticGainControl()
			}
	];

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow() => new DiscordConfigFlow();

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new DiscordMacroDeck2Migration()];

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		_events = new DiscordEventEmitter(context.Events);
		await ConnectFromConfigAsync(context).ConfigureAwait(false);
		IsInitialized = true;
	}

	public Task ShutdownAsync()
	{
		_connection?.Dispose();
		_connection = null;
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		_connection?.Dispose();
		_connection = null;
	}

	public async Task<DynamicOptionsResult> GetEventOptionsAsync(
		EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		var connection = _connection;
		var options = new List<ActionParameterOption>();

		if (connection is not null && context.ParameterName == "channelName")
		{
			foreach (var guild in await connection.GetGuildsAsync(cancellationToken).ConfigureAwait(false))
			{
				var channels = await connection
					.GetChannelsAsync(guild.Id,
						[DiscordChannelTypes.GuildVoice, DiscordChannelTypes.GuildStageVoice],
						cancellationToken)
					.ConfigureAwait(false);

				options.AddRange(channels.Select(c => new ActionParameterOption
				{
					Value = c.Name,
					Label = $"{c.Name} ({guild.Name})"
				}));
			}
		}

		return new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = true,
			CacheSeconds = 30
		};
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var connection = _connection;
		var state = connection?.State ?? DiscordState.Disconnected;
		var value = ReadVariable(state, localId);

		return ValueTask.FromResult(localId switch
		{
			InputVolumeId => VariableReading.Of(value, 0, MaxInputVolume, 1),
			OutputVolumeId => VariableReading.Of(value, 0, MaxOutputVolume, 1),
			_ => VariableReading.Of(value)
		});
	}

	public async ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
	{
		var output = localId switch
		{
			InputVolumeId => false,
			OutputVolumeId => true,
			_ => (bool?)null
		};

		if (output is not { } isOutput)
		{
			return VariableWriteResult.NotWritable();
		}

		if (_connection is not { } connection)
		{
			return VariableWriteResult.Unavailable();
		}

		if (!TryReadNumber(value, out var percent))
		{
			return VariableWriteResult.InvalidValue();
		}

		var device = new DiscordVoiceDevicePatch
		{
			Volume = Math.Clamp(percent, 0d, isOutput ? MaxOutputVolume : MaxInputVolume)
		};

		var patch = isOutput
			? new DiscordVoiceSettingsPatch { Output = device }
			: new DiscordVoiceSettingsPatch { Input = device };

		var result = await connection.SetVoiceSettingsAsync(patch, cancellationToken).ConfigureAwait(false);

		return result.Outcome switch
		{
			DiscordVoiceSettingsOutcome.Confirmed => VariableWriteResult.Applied(),
			DiscordVoiceSettingsOutcome.NotConnected => VariableWriteResult.Unavailable(),
			_ => VariableWriteResult.Failed()
		};
	}

	private static bool TryReadNumber(object? value, out double number)
	{
		number = value switch
		{
			double d => d,
			float f => f,
			int i => i,
			long l => l,
			decimal m => (double)m,
			string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) =>
				parsed,
			_ => double.NaN
		};

		return double.IsFinite(number);
	}

	internal static object? ReadVariable(DiscordState state, string localId)
	{
		if (localId == "discord-is-connected")
		{
			return state.IsConnected;
		}

		// Everything else is only meaningful while Discord is reachable. Returning null marks the variable
		// unavailable instead of asserting a value we cannot know.
		if (!state.IsConnected)
		{
			return null;
		}

		return localId switch
		{
			"discord-user-name" => state.UserName,
			"discord-is-self-muted" => state.SelfMuted,
			"discord-is-self-deafened" => state.SelfDeafened,
			"discord-is-server-muted" => state.ServerMuted,
			"discord-is-server-deafened" => state.ServerDeafened,
			"discord-is-muted" => state.EffectivelyMuted,
			"discord-is-deafened" => state.Deafened,
			"discord-is-speaking" => state.SelfSpeaking,
			"discord-is-in-voice-channel" => state.InVoiceChannel,
			"discord-voice-channel" => state.VoiceChannelName,
			"discord-voice-server" => state.VoiceGuildName,
			"discord-voice-connection-state" => state.VoiceConnectionState,
			"discord-voice-ping" => state.AveragePing,
			"discord-input-volume" => Math.Round(state.InputVolume),
			"discord-output-volume" => Math.Round(state.OutputVolume),
			"discord-voice-mode" => state.VoiceMode,
			"discord-noise-suppression" => state.NoiseSuppression,
			"discord-echo-cancellation" => state.EchoCancellation,
			"discord-automatic-gain-control" => state.AutomaticGainControl,
			_ => null
		};
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		var connection = _connection;
		if (connection is null)
		{
			return Task.FromResult<IReadOnlyList<IntegrationIssue>>([]);
		}

		return Task.FromResult(BuildIssues(connection.NeedsReauthorization,
			connection.AccessDenied,
			connection.IgnoredVoiceSettings));
	}

	internal static IReadOnlyList<IntegrationIssue> BuildIssues(
		bool needsReauthorization,
		bool accessDenied,
		IReadOnlyList<string> ignoredVoiceSettings)
	{
		var issues = new List<IntegrationIssue>();

		if (needsReauthorization)
		{
			issues.Add(new IntegrationIssue
			{
				Id = ReauthorizationIssueId,
				Title = AppStrings.Integrations.Discord.Issues.ReauthorizationTitle(),
				Description = AppStrings.Integrations.Discord.Issues.ReauthorizationDescription(),
				Severity = IntegrationIssueSeverity.Error,
				ActionLabel = AppStrings.Integrations.Discord.Issues.ReconnectAction()
			});
		}

		if (accessDenied)
		{
			issues.Add(new IntegrationIssue
			{
				Id = PrivilegeMismatchIssueId,
				Title = AppStrings.Integrations.Discord.Issues.PrivilegeMismatchTitle(),
				Description = AppStrings.Integrations.Discord.Issues.PrivilegeMismatchDescription(),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		if (ignoredVoiceSettings.Count > 0)
		{
			issues.Add(new IntegrationIssue
			{
				Id = VoiceSettingsIgnoredIssueId,
				Title = AppStrings.Integrations.Discord.Issues.VoiceSettingsIgnoredTitle(),
				Description = AppStrings.Integrations.Discord.Issues.VoiceSettingsIgnoredDescription(
					settings: string.Join(", ", ignoredVoiceSettings)),
				Severity = IntegrationIssueSeverity.Warning
				// No ActionLabel: the host cannot fix this on the user's behalf, same as privilege-mismatch.
			});
		}

		return issues;
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(issueId switch
		{
			ReauthorizationIssueId => IssueResolution.Ok(AppStrings.Integrations.Discord.Issues.ReauthorizeResolution(),
				IssueResolutionFollowUp.StartConfigFlow),
			PrivilegeMismatchIssueId => IssueResolution.Failed(AppStrings.Integrations.Discord.Issues
				.PrivilegeMismatchResolution()),
			VoiceSettingsIgnoredIssueId => IssueResolution.Failed(AppStrings.Integrations.Discord.Issues
				.VoiceSettingsIgnoredResolution()),
			_ => IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue())
		});

	private static byte[] LoadIcon()
	{
		var assembly = typeof(DiscordIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("discord-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}

	private async Task ConnectFromConfigAsync(IIntegrationContext context)
	{
		_connection?.Dispose();
		_connection = null;
		_entryId = null;

		var entries = await context.Config.GetEntriesAsync().ConfigureAwait(false);
		var entry = entries.Count > 0 ? entries[0] : null;
		if (entry is null)
		{
			_logger.Information("Discord is not set up; only the webhook action is available");
			return;
		}

		var clientId = await context.Config.GetStringAsync(entry.Id, DiscordConfigKeys.ClientId).ConfigureAwait(false);
		var clientSecret = await context.Config
			.GetSecretAsync(entry.Id, DiscordConfigKeys.ClientSecret)
			.ConfigureAwait(false);
		var accessToken = await context.Config
			.GetSecretAsync(entry.Id, DiscordConfigKeys.AccessToken)
			.ConfigureAwait(false);

		if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(accessToken))
		{
			_logger.Warning("Discord config entry {EntryId} is incomplete; skipping the connection", entry.Id);
			return;
		}

		var refreshToken = await context.Config
			.GetSecretAsync(entry.Id, DiscordConfigKeys.RefreshToken)
			.ConfigureAwait(false);
		var scope = await context.Config.GetStringAsync(entry.Id, DiscordConfigKeys.Scope).ConfigureAwait(false);
		var expiresAt = ParseExpiry(await context.Config.GetStringAsync(entry.Id, DiscordConfigKeys.ExpiresAt)
			.ConfigureAwait(false));

		_entryId = entry.Id;
		_connection = new DiscordConnection(() => new DiscordRpcClient(new DiscordIpcTransport()),
			_oauth,
			clientId,
			clientSecret,
			new DiscordTokens(accessToken, refreshToken, expiresAt, scope),
			PersistTokensAsync,
			_events);
		_connection.Start();
	}

	private static DateTimeOffset? ParseExpiry(string? raw)
		=> DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
			? parsed
			: null;

	private async Task PersistTokensAsync(DiscordTokens tokens, CancellationToken cancellationToken)
	{
		var context = _context;
		var entryId = _entryId;
		if (context is null || entryId is null)
		{
			return;
		}

		try
		{
			await context.Config
				.SetSecretAsync(entryId.Value, DiscordConfigKeys.AccessToken, tokens.AccessToken, cancellationToken)
				.ConfigureAwait(false);

			if (tokens.RefreshToken is not null)
			{
				await context.Config
					.SetSecretAsync(entryId.Value,
						DiscordConfigKeys.RefreshToken,
						tokens.RefreshToken,
						cancellationToken)
					.ConfigureAwait(false);
			}

			await context.Config
				.SetStringAsync(entryId.Value,
					DiscordConfigKeys.ExpiresAt,
					tokens.ExpiresAt?.ToString("o", CultureInfo.InvariantCulture),
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			// The refreshed tokens stay usable in memory; a storage problem (e.g. the entry was deleted
			// mid-refresh) must not take the connection's work loop down with it.
			_logger.Warning(ex, "Failed to persist refreshed Discord tokens");
		}
	}
}
