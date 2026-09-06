using System.Globalization;
using MacroDeckHost.Integrations.Voicemeeter.Actions;
using MacroDeckHost.Integrations.Voicemeeter.Native;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Voicemeeter;

[MacroDeckIntegration(Platforms = MacroDeckPlatform.Windows, EnabledByDefault = false)]
public sealed class VoicemeeterIntegration
	: IIntegration,
		IVariableProvider,
		IIntegrationIconProvider,
		IEventProvider,
		IDynamicEventOptionsProvider,
		IIntegrationIssueProvider,
		IMigrationProvider,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.voicemeeter";

	internal const string UnavailableIssueId = "remote-api-unavailable";

	private static readonly byte[] _icon = LoadIcon();

	private readonly VoicemeeterVariableAccessor _variableAccessor = new();
	private readonly Func<IVoicemeeterRemote> _remoteFactory;

	private VoicemeeterConnection? _connection;

	public VoicemeeterIntegration()
		: this(VoicemeeterRemoteFactory.Create)
	{
	}

	internal VoicemeeterIntegration(Func<IVoicemeeterRemote> remoteFactory)
	{
		_remoteFactory = remoteFactory;
		Actions = VoicemeeterActions.Create(() => _connection, _variableAccessor);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => "Voicemeeter";

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public IReadOnlyList<EventDefinition> EventDefinitions => VoicemeeterEventDefinitions.All;

	public IReadOnlyList<VariableDefinition> Variables
		=> _connection?.IsAvailable == true ? VoicemeeterVariables.All : [];

	public IReadOnlyList<VariableDefinition> DeclaredVariables => VoicemeeterVariables.All;

	internal VoicemeeterConnection? Connection => _connection;

	public byte[] GetIcon() => _icon;

	public Task InitializeAsync(IIntegrationContext context)
	{
		_variableAccessor.Current = context.Variables;

		_connection = new VoicemeeterConnection(_remoteFactory(), new VoicemeeterEventEmitter(context.Events));
		_connection.Start();
		IsInitialized = true;

		return Task.CompletedTask;
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

	// A definition id is the canonical name with '_' swapped for '-', and a canonical variable name can never
	// contain '-', so swapping the separator back recovers the name exactly.
	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var state = _connection?.State ?? VoicemeeterState.Disconnected;
		return ValueTask.FromResult(VoicemeeterVariables.Read(state, localId.Replace('-', '_')));
	}

	public ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
	{
		if (_connection is not { IsAvailable: true } connection)
		{
			return ValueTask.FromResult(VariableWriteResult.Unavailable());
		}

		if (!TryReadNumber(value, out var decibels))
		{
			return ValueTask.FromResult(VariableWriteResult.InvalidValue());
		}

		return ValueTask.FromResult(VoicemeeterVariables.Write(connection, localId.Replace('-', '_'), decibels));
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

	public Task<DynamicOptionsResult> GetEventOptionsAsync(
		EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		var catalog = _connection?.Catalog ?? VoicemeeterChannelCatalog.Unknown;

		var result = context.EventId == VoicemeeterEventIds.StripRoutingChanged &&
			context.ParameterName == VoicemeeterActionValues.BusParameter
				? VoicemeeterOptions.BusAssignments(catalog)
				: VoicemeeterOptions.ForParameter(catalog, context.ParameterName);

		return Task.FromResult(result);
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		var connection = _connection;
		IReadOnlyList<IntegrationIssue> issues = connection is null || connection.IsAvailable
			? []
			:
			[
				new IntegrationIssue
				{
					Id = UnavailableIssueId,
					Title = AppStrings.Integrations.Voicemeeter.Issues.UnavailableTitle(),
					Description = connection.UnavailableReason ?? default,
					Severity = IntegrationIssueSeverity.Error
				}
			];

		return Task.FromResult(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(IssueResolution.Failed(AppStrings.Integrations.Voicemeeter.Issues.InstallInstructions()));

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new VoicemeeterMacroDeck2Migration()];

	private static byte[] LoadIcon()
	{
		var assembly = typeof(VoicemeeterIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("voicemeeter-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
