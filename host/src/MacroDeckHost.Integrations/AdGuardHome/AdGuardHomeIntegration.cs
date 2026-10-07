using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.AdGuardHome;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.AdGuardHome.Actions;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AdGuardHome;

namespace MacroDeckHost.Integrations.AdGuardHome;

[MacroDeckIntegration]
public sealed partial class AdGuardHomeIntegration
	: IIntegration, IVariableProvider, IConfigFlowProvider, IIntegrationIconProvider, IWidgetTypeProvider,
		IVariableRefreshSignalConsumer, IAdGuardHomeSinkConsumer, IAdGuardHomeActionTarget
{
	public const string IntegrationId = AdGuardHomeWidgetType.OwnerId;

	private static readonly ILogger _logger = IntegrationLog.For<AdGuardHomeIntegration>(IntegrationId);

	private static readonly byte[] _icon = LoadIcon();

	private static readonly IReadOnlyList<VariableDefinition> _templateVariables =
		AdGuardHomeVariables.Declare(VariableNameTemplate.Placeholder("instance"));

	private static readonly string[] _nullableString = ["string", "null"];

	private readonly Func<AdGuardHomeConnectionSettings, IAdGuardHomeClient> _clientFactory;
	private readonly TimeProvider _time;

	private IIntegrationContext? _context;
	private IAdGuardHomeSink? _sink;
	private IVariableRefreshSignal? _refreshSignal;
	private volatile IReadOnlyList<AdGuardHomeInstance> _instances = [];

	public AdGuardHomeIntegration()
		: this(settings => new AdGuardHomeClient(settings), TimeProvider.System)
	{
	}

	internal AdGuardHomeIntegration(Func<AdGuardHomeConnectionSettings, IAdGuardHomeClient> clientFactory,
		TimeProvider time)
	{
		_clientFactory = clientFactory;
		_time = time;
		Actions = AdGuardHomeActions.Create(this);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => "AdGuard Home";

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public bool AllowsMultipleConfigurations => true;

	public bool VariablesDependOnConfiguration => true;

	public IReadOnlyList<VariableDefinition> Variables
		=>
		[
			.. _instances.Select(instance => instance.Snapshot)
				.SelectMany(snapshot => AdGuardHomeVariables.Declare(snapshot.VariableKey,
					new VariableConfiguration(snapshot.EntryId, snapshot.Title)))
		];

	public IReadOnlyList<VariableDefinition> DeclaredVariables
		=> Variables is { Count: > 0 } provided ? provided : _templateVariables;

	public IReadOnlyList<AdGuardHomeSnapshot> Snapshots => [.. _instances.Select(instance => instance.Snapshot)];

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow() => new AdGuardHomeConfigFlow(_clientFactory);

	public void UseVariableRefreshSignal(IVariableRefreshSignal signal) => _refreshSignal = signal;

	public void UseAdGuardHomeSink(IAdGuardHomeSink sink) => _sink = sink;

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		_sink?.UseExecutor(ExecuteAsync);
		await ReloadConfigurationsAsync(CancellationToken.None);
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		IsInitialized = false;
		_sink?.UseExecutor(null);
		await ReplaceInstancesAsync([]);
	}

	public Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);
		return context.RegisterWidgetTypeAsync(WidgetType(), cancellationToken);
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => [WidgetType()];

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		if (AdGuardHomeVariables.SplitDefinitionId(localId) is not { } split)
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		var snapshot = _instances.Select(instance => instance.Snapshot)
			.FirstOrDefault(candidate => string.Equals(candidate.VariableKey, split.VariableKey, StringComparison.Ordinal));
		return ValueTask.FromResult(AdGuardHomeVariables.Read(snapshot, split.Name));
	}

	public async Task ReloadConfigurationsAsync(CancellationToken cancellationToken)
	{
		if (_context is not { } context)
		{
			return;
		}

		var entries = await context.Config.GetEntriesAsync(cancellationToken);
		var usedKeys = new HashSet<string>(StringComparer.Ordinal);
		var instances = new List<AdGuardHomeInstance>(entries.Count);
		var previous = _instances;

		foreach (var entry in entries)
		{
			var settings = await ReadSettingsAsync(context.Config, entry.Id, cancellationToken);
			if (settings is null)
			{
				_logger.Warning("AdGuard Home config entry {EntryId} has no valid address; skipping", entry.Id);
				continue;
			}

			var storedKey = await context.Config.GetStringAsync(entry.Id, AdGuardHomeConfigKeys.VariableKey,
				cancellationToken);
			var key = storedKey is { Length: > 0 } && usedKeys.Add(storedKey)
				? storedKey
				: UniqueKey(entry.Title, usedKeys);
			var entryId = entry.Id.ToString("D");

			var unchanged = previous.FirstOrDefault(candidate => candidate.Snapshot.EntryId == entryId &&
				candidate.Snapshot.Title == entry.Title &&
				candidate.Snapshot.VariableKey == key &&
				candidate.Settings == settings);
			if (unchanged is not null)
			{
				instances.Add(unchanged);
				continue;
			}

			AdGuardHomeInstance? created = null;
			created = new AdGuardHomeInstance(entryId,
				entry.Title,
				key,
				settings,
				_clientFactory(settings),
				_time,
				snapshot => Publish(created!, snapshot));
			instances.Add(created);
		}

		await ReplaceInstancesAsync(instances);
	}

	public async Task<AdGuardHomeCommandOutcome> ExecuteAsync(
		string entryId,
		AdGuardHomeCommand command,
		CancellationToken cancellationToken)
	{
		var instance = _instances.FirstOrDefault(candidate => candidate.Snapshot.EntryId == entryId);
		if (instance is null)
		{
			return AdGuardHomeCommandOutcome.NotFound;
		}

		var client = instance.Client;
		try
		{
			switch (command.Kind)
			{
				case AdGuardHomeCommandKind.EnableProtection:
					await client.SetProtectionAsync(true, null, cancellationToken);
					break;
				case AdGuardHomeCommandKind.DisableProtection:
					await client.SetProtectionAsync(false, null, cancellationToken);
					break;
				case AdGuardHomeCommandKind.PauseProtection:
					await client.SetProtectionAsync(false, command.Duration, cancellationToken);
					break;
				case AdGuardHomeCommandKind.ToggleProtection:
					var status = await client.GetStatusAsync(cancellationToken);
					await client.SetProtectionAsync(!status.ProtectionEnabled, null, cancellationToken);
					break;
				case AdGuardHomeCommandKind.EnableFiltering:
				case AdGuardHomeCommandKind.DisableFiltering:
				case AdGuardHomeCommandKind.ToggleFiltering:
					var filtering = await client.GetFilteringStatusAsync(cancellationToken);
					var enabled = command.Kind switch
					{
						AdGuardHomeCommandKind.EnableFiltering => true,
						AdGuardHomeCommandKind.DisableFiltering => false,
						_ => !filtering.Enabled
					};
					await client.SetFilteringAsync(enabled, filtering.Interval, cancellationToken);
					break;
				case AdGuardHomeCommandKind.RefreshFilters:
					await client.RefreshFiltersAsync(cancellationToken);
					break;
				default:
					return AdGuardHomeCommandOutcome.Failed;
			}
		}
		catch (AdGuardHomeException exception)
		{
			_logger.Warning("AdGuard Home command {Command} on {Title} failed: {Message}",
				command.Kind,
				instance.Snapshot.Title,
				exception.Message);
			instance.RequestPoll();
			return exception.Failure switch
			{
				AdGuardHomeConnection.Unauthorized => AdGuardHomeCommandOutcome.Unauthorized,
				AdGuardHomeConnection.Timeout => AdGuardHomeCommandOutcome.Timeout,
				AdGuardHomeConnection.Incompatible => AdGuardHomeCommandOutcome.Incompatible,
				AdGuardHomeConnection.Redirected => AdGuardHomeCommandOutcome.Redirected,
				_ => AdGuardHomeCommandOutcome.Unreachable
			};
		}

		try
		{
			await instance.PollAsync(includeStatistics: false, cancellationToken);
		}
		catch (Exception exception) when (exception is ObjectDisposedException ||
			(exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
		{
			// A reload replaced the instance while the command ran; the command itself still succeeded.
		}

		return AdGuardHomeCommandOutcome.Succeeded;
	}

	internal static WidgetTypeDescriptor WidgetType()
		=> new(AdGuardHomeWidgetType.LocalId,
			Strings.Widget.Name(),
			Strings.Widget.Description(),
			DefaultData: DefaultWidgetData,
			DataSchema: WidgetDataSchema,
			HasConfiguration: true)
		{
			AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor]
		};

	private static string DefaultWidgetData
		=> JsonSerializer.Serialize(new Dictionary<string, object>
		{
			[AdGuardHomeWidgetType.InstanceKey] = string.Empty,
			[AdGuardHomeWidgetType.DisplayNameKey] = string.Empty,
			[AdGuardHomeWidgetType.ViewKey] = AdGuardHomeWidgetType.ControlView,
			[AdGuardHomeWidgetType.StatisticsKey] = AdGuardHomeWidgetType.DefaultStatistics,
			[AdGuardHomeWidgetType.DurationsKey] = AdGuardHomeWidgetType.DefaultDurations,
			[AdGuardHomeWidgetType.ShowVersionKey] = true,
			[AdGuardHomeWidgetType.ShowStatusKey] = true,
		});

	private static string WidgetDataSchema
		=> JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["type"] = "object",
			["properties"] = new Dictionary<string, object>
			{
				[AdGuardHomeWidgetType.InstanceKey] = new { type = "string" },
				[AdGuardHomeWidgetType.DisplayNameKey] = new { type = "string" },
				[AdGuardHomeWidgetType.ViewKey] = new { type = "string", @enum = AdGuardHomeWidgetType.Views },
				[AdGuardHomeWidgetType.StatisticsKey] = new
				{
					type = "array", items = new { type = "string", @enum = AdGuardHomeWidgetType.Statistics }
				},
				[AdGuardHomeWidgetType.DurationsKey] = new
				{
					type = "array", items = new { type = "string", @enum = AdGuardHomeWidgetType.DurationIds }
				},
				[AdGuardHomeWidgetType.ShowVersionKey] = new { type = "boolean" },
				[AdGuardHomeWidgetType.ShowStatusKey] = new { type = "boolean" },
				[AdGuardHomeWidgetType.BackgroundColorKey] = new
				{
					type = _nullableString, description = "#rrggbb or transparent"
				},
			},
		});

	private void Publish(AdGuardHomeInstance source, AdGuardHomeSnapshot snapshot)
	{
		if (!_instances.Contains(source))
		{
			return;
		}

		_sink?.Update(snapshot);
		_refreshSignal?.RequestEagerRefresh(IntegrationId);
	}

	private async Task ReplaceInstancesAsync(IReadOnlyList<AdGuardHomeInstance> instances)
	{
		var previous = _instances;
		_instances = instances;
		_sink?.Replace([.. instances.Select(instance => instance.Snapshot)]);

		foreach (var instance in previous.Where(instance => !instances.Contains(instance)))
		{
			await instance.DisposeAsync();
		}

		foreach (var instance in instances)
		{
			instance.Start();
		}

		_refreshSignal?.RequestEagerRefresh(IntegrationId);
	}

	private static async Task<AdGuardHomeConnectionSettings?> ReadSettingsAsync(
		IIntegrationConfig config,
		Guid entryId,
		CancellationToken cancellationToken)
	{
		var controlUrl = AdGuardHomeEndpoint.TryBuild(
			await config.GetStringAsync(entryId, AdGuardHomeConfigKeys.BaseUrl, cancellationToken));
		if (controlUrl is null)
		{
			return null;
		}

		var username = await config.GetStringAsync(entryId, AdGuardHomeConfigKeys.Username, cancellationToken);
		var password = await config.GetSecretAsync(entryId, AdGuardHomeConfigKeys.Password, cancellationToken);
		var acceptUntrusted = await config.GetStringAsync(entryId, AdGuardHomeConfigKeys.AcceptUntrustedCertificate,
			cancellationToken);

		return new AdGuardHomeConnectionSettings(controlUrl,
			string.IsNullOrWhiteSpace(username) ? null : username.Trim(),
			password,
			string.Equals(acceptUntrusted, "true", StringComparison.OrdinalIgnoreCase));
	}

	internal static string UniqueKey(string title, HashSet<string> used)
	{
		var baseKey = Slugify(title);
		if (string.IsNullOrEmpty(baseKey))
		{
			baseKey = "instance";
		}

		var key = baseKey;
		var suffix = 2;
		while (!used.Add(key))
		{
			key = $"{baseKey}_{suffix++}";
		}

		return key;
	}

	internal static string Slugify(string title)
	{
		var builder = new StringBuilder(title.Length + 4);
		foreach (var ch in title.ToLowerInvariant())
		{
			switch (ch)
			{
				case 'ä':
					builder.Append("ae");
					break;
				case 'ö':
					builder.Append("oe");
					break;
				case 'ü':
					builder.Append("ue");
					break;
				case 'ß':
					builder.Append("ss");
					break;
				case >= 'a' and <= 'z':
				case >= '0' and <= '9':
					builder.Append(ch);
					break;
				default:
					builder.Append('_');
					break;
			}
		}

		return CollapseUnderscoresRegex().Replace(builder.ToString(), "_").Trim('_');
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(AdGuardHomeIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("adguard-home-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}

	[GeneratedRegex("_+")]
	private static partial Regex CollapseUnderscoresRegex();
}
