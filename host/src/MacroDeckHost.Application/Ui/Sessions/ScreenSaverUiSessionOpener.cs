using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Ui.Sessions;

public interface IScreenSaverUiSessionOpener
{
	Task<OpenScreenSaverUiSessionResponse> OpenAsync(Guid? deviceId, CancellationToken cancellationToken);
}

// The broker's provider id is the owning integration's real id, as for folder views: a plugin publishes
// under its own id, and a synthetic per-device id would fail the ownership check on the first patch.
public sealed class ScreenSaverUiSessionOpener : IScreenSaverUiSessionOpener
{
	private static readonly JsonElement _emptyObject = JsonDocument.Parse("{}").RootElement;

	private readonly IServiceScopeFactory _scopes;
	private readonly IScreenSaverRegistry _screenSavers;
	private readonly UiSessionRegistry _registry;
	private readonly IUiSessionBroker _broker;

	public ScreenSaverUiSessionOpener(
		IServiceScopeFactory scopes,
		IScreenSaverRegistry screenSavers,
		UiSessionRegistry registry,
		IUiSessionBroker broker)
	{
		_scopes = scopes;
		_screenSavers = screenSavers;
		_registry = registry;
		_broker = broker;
	}

	public async Task<OpenScreenSaverUiSessionResponse> OpenAsync(Guid? deviceId, CancellationToken cancellationToken)
	{
		if (deviceId is not { } id || await LoadDevice(id) is not { } device)
		{
			return Rejected(string.Empty, UiSessionErrorCodes.ProviderUnavailable, "That device does not exist.");
		}

		var selected = string.IsNullOrEmpty(device.ScreenSaverId) ? BuiltInScreenSavers.Clock : device.ScreenSaverId;
		var configuration = device.ScreenSaverConfiguration;

		if (!_screenSavers.TryResolve(selected, out var entry))
		{
			selected = BuiltInScreenSavers.Clock;
			configuration = null;

			if (!_screenSavers.TryResolve(selected, out entry))
			{
				return Rejected(selected, UiSessionErrorCodes.ProviderUnavailable, "No screensaver is available.");
			}
		}

		var ownerPrincipal = id.ToString();
		var interactive = entry.Descriptor.Interactive;
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiScreenSaverSurfaceAttributes.DeviceId] = JsonSerializer.SerializeToElement(ownerPrincipal),
			[UiScreenSaverSurfaceAttributes.ScreenSaverId] = JsonSerializer.SerializeToElement(selected),
			[UiScreenSaverSurfaceAttributes.Configuration] = ParseStoredConfiguration(configuration)
		};

		if (FindExisting(entry.ProviderId, ownerPrincipal, attributes) is { } existing)
		{
			return Accepted(existing, selected, interactive);
		}

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.ScreenSaver,
			SessionMode = UiSessionModes.Shared,
			Attributes = attributes
		};

		var ticket = _broker.Open(entry.ProviderId, surface, ownerPrincipal);

		return ticket.Accepted
			? Accepted(ticket.SessionId, selected, interactive)
			: Rejected(selected, ticket.Code ?? UiSessionErrorCodes.ProviderUnavailable, ticket.Message);
	}

	private async Task<DeviceEntity?> LoadDevice(Guid id)
	{
		await using var scope = _scopes.CreateAsyncScope();
		return await scope.ServiceProvider.GetRequiredService<IDeviceRepository>().GetById(id);
	}

	// A session the client let go of is still draining when the device's settings change, and what it
	// draws is fixed at open time: only a session for the very same selection and configuration is reused.
	private string? FindExisting(string providerId, string ownerPrincipal, IReadOnlyDictionary<string, JsonElement> wanted)
	{
		foreach (var session in _registry.SessionsForProvider(providerId))
		{
			if (!string.Equals(session.OwnerPrincipal, ownerPrincipal, StringComparison.Ordinal) ||
				session.State is UiSessionState.Closed or UiSessionState.Invalidated ||
				!string.Equals(session.Surface.Kind, UiSurfaceKinds.ScreenSaver, StringComparison.Ordinal))
			{
				continue;
			}

			if (wanted.All(pair => session.Surface.Attributes.TryGetValue(pair.Key, out var candidate) &&
					string.Equals(candidate.GetRawText(), pair.Value.GetRawText(), StringComparison.Ordinal)))
			{
				return session.SessionId;
			}
		}

		return null;
	}

	private static JsonElement ParseStoredConfiguration(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return _emptyObject;
		}

		try
		{
			using var document = JsonDocument.Parse(json);
			return document.RootElement.ValueKind == JsonValueKind.Object
				? document.RootElement.Clone()
				: _emptyObject;
		}
		catch (JsonException)
		{
			return _emptyObject;
		}
	}

	private static OpenScreenSaverUiSessionResponse Accepted(string sessionId, string screenSaverId, bool interactive)
		=> new() { Accepted = true, SessionId = sessionId, ScreenSaverId = screenSaverId, Interactive = interactive };

	private static OpenScreenSaverUiSessionResponse Rejected(string screenSaverId, string code, string? message)
		=> new()
		{
			Accepted = false, SessionId = string.Empty, ScreenSaverId = screenSaverId, Code = code, Message = message
		};
}
