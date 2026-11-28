using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Localization;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Application.Devices;

public sealed class ProviderDeviceStarterProfiles : IDisposable
{
	private static readonly string[] _exampleWidgetTypes =
		[WidgetTypeIds.Clock, WidgetTypeIds.Stopwatch, WidgetTypeIds.ActionButton];

	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ProviderDeviceGates _gates;
	private readonly ILayoutRegistry _layouts;
	private readonly StartupReadiness _readiness;
	private readonly ILogger _logger;

	public ProviderDeviceStarterProfiles(
		IServiceScopeFactory scopeFactory,
		ProviderDeviceGates gates,
		ILayoutRegistry layouts,
		StartupReadiness readiness,
		ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_gates = gates;
		_layouts = layouts;
		_readiness = readiness;
		_logger = logger.ForContext<ProviderDeviceStarterProfiles>();
	}

	public void Dispose()
	{
		_gate.Dispose();
	}

	public async Task<bool> ProvideAsync(Guid deviceId)
	{
		// Until the caches are ready no profile can be created or found; the startup pass picks it up.
		if (!_readiness.WhenCachesReady.IsCompletedSuccessfully)
		{
			return false;
		}

		await _gate.WaitAsync();
		try
		{
			return await ProvideCoreAsync(deviceId);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception, "Failed to give device {DeviceId} a starter profile", deviceId);
			return false;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task ProvidePendingAsync()
	{
		if (!_readiness.WhenCachesReady.IsCompletedSuccessfully)
		{
			return;
		}

		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();
			var pending = (await scope.ServiceProvider.GetRequiredService<IDeviceRepository>().GetAll())
				.Where(device => device is { StarterProfilePending: true, IsProviderDevice: true })
				.Select(device => device.Id)
				.ToList();

			foreach (var deviceId in pending)
			{
				if (await ProvideAsync(deviceId))
				{
					await scope.ServiceProvider.GetRequiredService<IMediator>()
						.Publish(new DeviceChangedNotification(deviceId));
				}
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception, "Failed to give pending devices a starter profile");
		}
	}

	private async Task<bool> ProvideCoreAsync(Guid deviceId)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var services = scope.ServiceProvider;
		var devices = services.GetRequiredService<IDeviceRepository>();

		if (await devices.GetById(deviceId) is not { StarterProfilePending: true, ProviderId: { } providerId } device)
		{
			return false;
		}

		if (device.StartupProfileId is not null)
		{
			await devices.ClaimStarterProfile(deviceId);
			return false;
		}

		var (layout, resolvedLive) = LayoutOf(device);
		if (device.LayoutReference is not null && layout is null)
		{
			return false;
		}

		// Claimed before anything is created: a crash or a second caller can then cost the device its
		// starter profile, but never give it a second one.
		if (!await devices.ClaimStarterProfile(deviceId))
		{
			return false;
		}

		var profileId = layout?.PrimaryGrid?.Grid is { Rows: >= 1, Columns: >= 1 } grid
			? await CreateAsync(services, device.Name, grid)
			: FirstProfileId(services);
		if (profileId is null)
		{
			return false;
		}

		var snapshot = resolvedLive ? LayoutSnapshotSerializer.Serialize(layout!) : null;

		return await _gates.Serialized(providerId,
			() => devices.AssignStarterProfile(deviceId, profileId, snapshot),
			CancellationToken.None);
	}

	private (LayoutDescriptor? Layout, bool ResolvedLive) LayoutOf(DeviceEntity device)
	{
		if (LayoutSnapshotSerializer.TryDeserialize(device.LayoutSnapshot, out var stored))
		{
			return (stored, false);
		}

		if (device.LayoutReference is not null && _layouts.TryResolve(device.LayoutReference, out var live))
		{
			return (live, true);
		}

		return (null, false);
	}

	private async Task<string?> CreateAsync(IServiceProvider services, string name, LayoutGrid grid)
	{
		var created = await services.GetRequiredService<IProfileService>()
			.Create(name, ProfileLayoutType.Grid, grid.Rows, grid.Columns);
		if (!created.Success)
		{
			_logger.Warning("Could not create the starter profile {Name}: {Error}", name, created.ErrorMessage);
			return null;
		}

		var profile = created.Data!;
		var home = services.GetRequiredService<IFolderCache>()
			.GetFoldersByProfileId(profile.Id)
			.FirstOrDefault(folder => folder.IsDefault);
		if (home is not null)
		{
			await AddExampleWidgetsAsync(services, home.Id, grid);
		}

		return profile.Id.ToString();
	}

	private async Task AddExampleWidgetsAsync(IServiceProvider services, Guid folderId, LayoutGrid grid)
	{
		var widgets = services.GetRequiredService<IWidgetService>();
		var types = services.GetRequiredService<IWidgetTypeRegistry>();
		var label = await ActiveLocalization.Resolve(services, AppStrings.Profiles.StarterExampleButtonLabel());

		var count = Math.Min(_exampleWidgetTypes.Length, grid.Rows * grid.Columns);
		for (var index = 0; index < count; index++)
		{
			var type = _exampleWidgetTypes[index];
			var widget = new WidgetEntity
			{
				Type = type,
				PositionX = index % grid.Columns,
				PositionY = index / grid.Columns,
				Width = 1,
				Height = 1,
				Data = ExampleData(types, type, label)
			};

			var result = await widgets.Create(folderId, widget);
			if (!result.Success)
			{
				_logger.Warning("Could not add the example {Type} widget to a starter profile: {Error}",
					type,
					result.ErrorMessage);
			}
		}
	}

	private static string ExampleData(IWidgetTypeRegistry types, string type, string label)
	{
		var data = types.TryResolve(type, out var entry) && entry.Descriptor.DefaultData is { } defaults
			? ParseObject(defaults)
			: new JsonObject();

		switch (type)
		{
			case WidgetTypeIds.Clock:
				data["showDate"] = false;
				break;
			case WidgetTypeIds.ActionButton:
				data["label"] = label;
				break;
		}

		return data.ToJsonString();
	}

	private static JsonObject ParseObject(string json)
	{
		try
		{
			return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
		}
		catch (JsonException)
		{
			return new JsonObject();
		}
	}

	private static string? FirstProfileId(IServiceProvider services)
	{
		return services.GetRequiredService<IProfileCache>()
			.GetAll()
			.OrderBy(profile => profile.Order)
			.FirstOrDefault()
			?.Id.ToString();
	}
}
