using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Calendar;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Calendar;

namespace MacroDeck.Plugin.Hosting.Capabilities.Calendar;

internal sealed class CalendarCapabilityHandler(IEnumerable<IPluginIntegration> integrations, PluginMetadata metadata)
	: ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly IReadOnlyList<ICalendarProvider> _providers = [.. integrations.OfType<ICalendarProvider>()];

	public string Kind => CapabilityKinds.Calendar;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> _providers.Count == 0
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Calendar, LocalId = ProviderCapabilityId.LocalId, VersionRange = _version
				}
			];

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		if (string.Equals(invocation.Operation, CapabilityOperations.Calendar.Describe, StringComparison.Ordinal))
		{
			return Task.FromResult(Describe());
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No calendar provider '{invocation.LocalId}' is registered in this plugin."));
		}

		return invocation.Operation switch
		{
			CapabilityOperations.Calendar.Accounts => Task.FromResult(
				CapabilityInvocationResult.Ok(new CalendarAccountsResult { Accounts = BuildAccounts() })),
			CapabilityOperations.Calendar.Calendars => CalendarsAsync(invocation, cancellationToken),
			CapabilityOperations.Calendar.Events => EventsAsync(invocation, cancellationToken),
			CapabilityOperations.Calendar.Event => EventAsync(invocation, cancellationToken),
			_ => Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The calendar capability has no operation '{invocation.Operation}'."))
		};
	}

	private CapabilityInvocationResult Describe()
		=> CapabilityInvocationResult.Ok(new CalendarDescribePayload
		{
			ProviderName = _providers.Select(p => p.ProviderName).FirstOrDefault(name => !string.IsNullOrEmpty(name)) ??
				metadata.Name,
			Accounts = BuildAccounts()
		});

	// An account id announced by two providers belongs to the first one, as FindProvider routes it and the
	// host registry keeps it.
	private IReadOnlyList<CalendarAccountDto> BuildAccounts()
		=> [.. _providers.SelectMany(provider => provider.GetAccounts())
			.DistinctBy(account => account.Id, StringComparer.Ordinal)
			.Select(CalendarDescriptorMapper.ToDto)];

	private async Task<CapabilityInvocationResult> CalendarsAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<CalendarAccountArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return MissingArguments(invocation.Operation);
		}

		var provider = FindProvider(arguments.AccountId);
		if (provider is null)
		{
			return UnknownAccount(arguments.AccountId);
		}

		var calendars = await provider.GetCalendarsAsync(arguments.AccountId, cancellationToken)
			.ConfigureAwait(false);

		return CapabilityInvocationResult.Ok(new CalendarListResult
		{
			Calendars = [.. calendars.Select(CalendarDescriptorMapper.ToDto)]
		});
	}

	private async Task<CapabilityInvocationResult> EventsAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<CalendarEventsArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return MissingArguments(invocation.Operation);
		}

		var provider = FindProvider(arguments.AccountId);
		if (provider is null)
		{
			return UnknownAccount(arguments.AccountId);
		}

		var events = await provider.GetEventsAsync(arguments.AccountId,
				new CalendarEventQuery
				{
					From = arguments.From, To = arguments.To, CalendarIds = [.. arguments.CalendarIds]
				},
				cancellationToken)
			.ConfigureAwait(false);

		return CapabilityInvocationResult.Ok(CalendarDescriptorMapper.ToEventsResult(events));
	}

	private async Task<CapabilityInvocationResult> EventAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<CalendarEventArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return MissingArguments(invocation.Operation);
		}

		var provider = FindProvider(arguments.AccountId);
		if (provider is null)
		{
			return UnknownAccount(arguments.AccountId);
		}

		var calendarEvent = await provider
			.GetEventAsync(arguments.AccountId, arguments.CalendarId, arguments.EventId, cancellationToken)
			.ConfigureAwait(false);

		return CapabilityInvocationResult.Ok(new CalendarEventResult
		{
			Event = calendarEvent is null ? null : CalendarDescriptorMapper.ToDto(calendarEvent)
		});
	}

	private ICalendarProvider? FindProvider(string accountId)
		=> _providers.FirstOrDefault(provider => provider.GetAccounts()
			.Any(account => string.Equals(account.Id, accountId, StringComparison.Ordinal)));

	private static CapabilityInvocationResult MissingArguments(string operation)
		=> CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
			$"The {operation} operation requires arguments.");

	private static CapabilityInvocationResult UnknownAccount(string accountId)
		=> CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
			$"No calendar account '{accountId}' is currently available.");
}
