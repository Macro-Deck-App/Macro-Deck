using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Batteries;

public sealed partial class BatteryVariablePublisher : IHostedService
{
	private const string LevelSuffix = "level";
	private const string ChargingSuffix = "charging";
	private const string ConnectedSuffix = "connected";
	private const string StateSuffix = "state";
	private const string TimeRemainingSuffix = "time_remaining_minutes";
	private const string TimeToFullSuffix = "time_to_full_minutes";
	private const string HealthSuffix = "health";

	private const string DisconnectedState = "disconnected";
	private const string PercentUnit = "%";

	private const string Prefix = "battery_";
	private const string FallbackSlug = "device";
	private const int MaxSlugLength = 40;
	private const int HashLength = 8;

	private static readonly (string Suffix, object Value)[] _withdrawnValues =
	[
		(ChargingSuffix, false),
		(ConnectedSuffix, false),
		(StateSuffix, DisconnectedState),
		(TimeRemainingSuffix, 0),
		(TimeToFullSuffix, 0)
	];

	private readonly IBatteryRegistry _batteries;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly List<string> _pendingRemoved = [];
	private readonly HashSet<string> _owners = new(StringComparer.Ordinal);
	private Task _worker = Task.CompletedTask;
	private bool _pending;
	private bool _running;
	private bool _stopped;

	public BatteryVariablePublisher(IBatteryRegistry batteries, IServiceScopeFactory scopeFactory, ILogger logger)
	{
		_batteries = batteries;
		_scopeFactory = scopeFactory;
		_logger = logger.ForContext<BatteryVariablePublisher>();
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_batteries.Changed += OnChanged;
		Request([]);
		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		_batteries.Changed -= OnChanged;
		Task worker;
		lock (_gate)
		{
			_stopped = true;
			worker = _worker;
		}

		await worker.WaitAsync(cancellationToken);
	}

	private void OnChanged(object? sender, BatteryDevicesChangedEventArgs args)
	{
		Request(args.RemovedIds);
	}

	// One writer at a time: changes that arrive while it writes are coalesced into its next pass, and
	// removed ids accumulate so none is lost to the coalescing.
	private void Request(IReadOnlyList<string> removedIds)
	{
		lock (_gate)
		{
			if (_stopped)
			{
				return;
			}

			_pendingRemoved.AddRange(removedIds);
			_pending = true;
			if (!_running)
			{
				_running = true;
				_worker = Task.Run(DrainAsync);
			}
		}
	}

	private async Task DrainAsync()
	{
		while (true)
		{
			string[] removedIds;
			lock (_gate)
			{
				if (!_pending || _stopped)
				{
					_running = false;
					return;
				}

				_pending = false;
				removedIds = [.. _pendingRemoved.Distinct(StringComparer.Ordinal)];
				_pendingRemoved.Clear();
			}

			try
			{
				await SyncAsync(removedIds);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Warning(exception, "Could not update the battery variables");
			}
		}
	}

	private async Task SyncAsync(IReadOnlyList<string> removedIds)
	{
		var devices = _batteries.Devices;
		foreach (var device in devices)
		{
			_owners.Add(device.ProviderId);
		}

		foreach (var removedId in removedIds)
		{
			if (OwnerOf(removedId) is { } owner)
			{
				_owners.Add(owner);
			}
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var variables = scope.ServiceProvider.GetRequiredService<IVariableService>();
		foreach (var owner in _owners.ToList())
		{
			try
			{
				var remaining = await SyncOwnerAsync(variables,
					owner,
					devices.Where(device => string.Equals(device.ProviderId, owner, StringComparison.Ordinal)).ToList(),
					removedIds.Where(id => string.Equals(OwnerOf(id), owner, StringComparison.Ordinal)).ToList());
				if (remaining == 0)
				{
					_owners.Remove(owner);
				}
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Warning(exception, "Could not update the battery variables of '{IntegrationId}'", owner);
			}
		}
	}

	private async Task<int> SyncOwnerAsync(
		IVariableService variables,
		string owner,
		IReadOnlyList<BatteryDeviceSnapshot> devices,
		IReadOnlyList<string> removedIds)
	{
		var groups = GroupsOf(await variables.GetByOwnerIntegration(owner));

		foreach (var removedId in removedIds)
		{
			if (groups.Remove(HashOf(removedId), out var removed))
			{
				foreach (var variable in removed.Variables.Values)
				{
					await variables.DeleteIntegrationVariable(owner, variable.Id);
				}
			}
		}

		var listed = new HashSet<string>(StringComparer.Ordinal);
		foreach (var device in devices)
		{
			var hash = HashOf(device.Id);
			listed.Add(hash);
			if (!groups.TryGetValue(hash, out var group))
			{
				group = new Group($"{Prefix}{Slug(device.Name)}_{hash}");
				groups[hash] = group;
			}

			await WriteAsync(variables, owner, group, device);
		}

		foreach (var (hash, group) in groups)
		{
			if (listed.Contains(hash))
			{
				continue;
			}

			foreach (var (suffix, value) in _withdrawnValues)
			{
				if (group.Variables.TryGetValue(suffix, out var variable))
				{
					await ReportAsync(variables, owner, variable, value);
				}
			}
		}

		return groups.Count;
	}

	private async Task WriteAsync(IVariableService variables, string owner, Group group, BatteryDeviceSnapshot device)
	{
		var name = device.Name;
		var connected = device.IsConnected;

		await UpsertAsync(variables,
			owner,
			group,
			LevelSuffix,
			VariableType.Numeric,
			0,
			AppStrings.Integrations.Batteries.Variables.Level(name),
			device.LevelPercent);
		await UpsertAsync(variables,
			owner,
			group,
			ChargingSuffix,
			VariableType.Boolean,
			null,
			AppStrings.Integrations.Batteries.Variables.Charging(name),
			connected && device.ChargeState == BatteryChargeState.Charging);
		await UpsertAsync(variables,
			owner,
			group,
			ConnectedSuffix,
			VariableType.Boolean,
			null,
			AppStrings.Integrations.Batteries.Variables.Connected(name),
			connected);
		await UpsertAsync(variables,
			owner,
			group,
			StateSuffix,
			VariableType.Text,
			null,
			AppStrings.Integrations.Batteries.Variables.State(name),
			connected ? ChargeStateOf(device.ChargeState) : DisconnectedState);
		await UpsertAsync(variables,
			owner,
			group,
			TimeRemainingSuffix,
			VariableType.Numeric,
			0,
			AppStrings.Integrations.Batteries.Variables.TimeRemaining(name),
			connected ? WholeMinutes(device.TimeRemaining) : 0);
		await UpsertAsync(variables,
			owner,
			group,
			TimeToFullSuffix,
			VariableType.Numeric,
			0,
			AppStrings.Integrations.Batteries.Variables.TimeToFull(name),
			connected ? WholeMinutes(device.TimeToFull) : 0);
		await UpsertAsync(variables,
			owner,
			group,
			HealthSuffix,
			VariableType.Text,
			null,
			AppStrings.Integrations.Batteries.Variables.Health(name),
			HealthOf(device.Health));
	}

	private async Task UpsertAsync(
		IVariableService variables,
		string owner,
		Group group,
		string suffix,
		VariableType type,
		int? decimalPlaces,
		LocalizedText displayName,
		object? value)
	{
		var unit = UnitOf(suffix);
		var declaration = new VariableDeclaration
		{
			Presentation = new VariablePresentation(displayName, null, default),
			DecimalPlaces = decimalPlaces,
			Unit = unit
		};
		var name = $"{group.Stem}_{suffix}";
		if (group.Variables.TryGetValue(suffix, out var existing))
		{
			// A variable created before it had a unit gets it by re-declaring it, which keeps its identity.
			if (!string.Equals(existing.Unit, unit, StringComparison.Ordinal))
			{
				await DeclareAsync(variables, owner, name, type, DefaultOf(type), decimalPlaces, declaration);
			}

			if (value is not null)
			{
				await ReportAsync(variables, owner, existing, value);
			}

			return;
		}

		var created = await DeclareAsync(variables,
			owner,
			name,
			type,
			value ?? DefaultOf(type),
			decimalPlaces,
			declaration);
		if (!created.Success || created.Data is null)
		{
			_logger.Warning("Could not create the battery variable '{Name}' of '{IntegrationId}': {Error}",
				name,
				owner,
				created.ErrorMessage);
			return;
		}

		group.Variables[suffix] = created.Data;
		if (value is not null)
		{
			await ReportAsync(variables, owner, created.Data, value);
		}
	}

	private static Task<Result<VariableEntity, VariableError>> DeclareAsync(
		IVariableService variables,
		string owner,
		string name,
		VariableType type,
		object initialValue,
		int? decimalPlaces,
		VariableDeclaration declaration)
	{
		return variables.CreateIntegrationVariable(owner,
			name,
			VariableScope.Global,
			null,
			type,
			initialValue,
			decimalPlaces,
			declaration: declaration,
			// Pushed, not polled: an unchanged level is never reported again, so a polled one would go stale.
			updateMode: VariableUpdateMode.Pushed,
			publishedByHost: true);
	}

	private static string? UnitOf(string suffix)
	{
		return suffix == LevelSuffix ? PercentUnit : null;
	}

	private async Task ReportAsync(IVariableService variables, string owner, VariableEntity variable, object value)
	{
		if (string.Equals(variable.Value,
			VariableValueSerializer.Serialize(variable.Type, value, variable.DecimalPlaces),
			StringComparison.Ordinal))
		{
			return;
		}

		var reported = await variables.ReportIntegrationVariableValue(owner, variable.Id, value);
		if (!reported.Success && reported.Error != VariableError.NotFound)
		{
			_logger.Warning("Could not update the battery variable '{Name}' of '{IntegrationId}': {Error}",
				variable.Name,
				owner,
				reported.ErrorMessage);
		}
	}

	private static Dictionary<string, Group> GroupsOf(IReadOnlyList<VariableEntity> variables)
	{
		var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
		foreach (var variable in variables)
		{
			if (!variable.PublishedByHost || VariableNameRegex().Match(variable.Name) is not { Success: true } match)
			{
				continue;
			}

			var hash = match.Groups["hash"].Value;
			if (!groups.TryGetValue(hash, out var group))
			{
				group = new Group(match.Groups["stem"].Value);
				groups[hash] = group;
			}

			group.Variables[match.Groups["suffix"].Value] = variable;
		}

		return groups;
	}

	private static string? OwnerOf(string qualifiedId)
	{
		var separator = qualifiedId.IndexOf(':');
		return separator > 0 ? qualifiedId[..separator] : null;
	}

	private static string HashOf(string qualifiedId)
	{
		return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(qualifiedId)))[..HashLength];
	}

	private static string ChargeStateOf(BatteryChargeState state)
	{
		return state switch
		{
			BatteryChargeState.Discharging => BatteryChargeStates.Discharging,
			BatteryChargeState.Charging => BatteryChargeStates.Charging,
			BatteryChargeState.Full => BatteryChargeStates.Full,
			BatteryChargeState.NotCharging => BatteryChargeStates.NotCharging,
			_ => BatteryChargeStates.Unknown
		};
	}

	private static string HealthOf(BatteryHealth health)
	{
		return health switch
		{
			BatteryHealth.Good => BatteryHealthStates.Good,
			BatteryHealth.Overheat => BatteryHealthStates.Overheat,
			BatteryHealth.Dead => BatteryHealthStates.Dead,
			BatteryHealth.OverVoltage => BatteryHealthStates.OverVoltage,
			BatteryHealth.Cold => BatteryHealthStates.Cold,
			BatteryHealth.Failure => BatteryHealthStates.Failure,
			_ => BatteryHealthStates.Unknown
		};
	}

	private static int WholeMinutes(TimeSpan? span)
	{
		return span is { } value && value > TimeSpan.Zero
			? (int)Math.Min(int.MaxValue, Math.Ceiling(value.TotalMinutes))
			: 0;
	}

	private static object DefaultOf(VariableType type)
	{
		return type switch
		{
			VariableType.Numeric => 0,
			VariableType.Boolean => false,
			_ => string.Empty
		};
	}

	private static string Slug(string name)
	{
		var builder = new StringBuilder(name.Length);
		foreach (var character in name)
		{
			var mapped = char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_';
			if (mapped != '_' || builder.Length > 0 && builder[^1] != '_')
			{
				builder.Append(mapped);
			}
		}

		var slug = builder.ToString().Trim('_');
		if (slug.Length > MaxSlugLength)
		{
			slug = slug[..MaxSlugLength].TrimEnd('_');
		}

		return slug.Length == 0 ? FallbackSlug : slug;
	}

	[GeneratedRegex(
		"^(?<stem>battery_(?:[a-z0-9_]*_)?(?<hash>[0-9a-f]{8}))_(?<suffix>level|charging|connected|state|time_remaining_minutes|time_to_full_minutes|health)$")]
	private static partial Regex VariableNameRegex();

	private sealed class Group(string stem)
	{
		public string Stem { get; } = stem;

		public Dictionary<string, VariableEntity> Variables { get; } = new(StringComparer.Ordinal);
	}
}
