using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Calendar;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// The <c>calendar</c> capability. A calendar provider may serve several accounts; the arguments of
/// <see cref="GetCalendarsAsync" />, <see cref="GetEventsAsync" /> and <see cref="GetEventAsync" /> are where
/// the account id being asked about lives, not the wire local id.
/// </summary>
public sealed class CalendarTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal CalendarTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>
	/// Reads a <c>CalendarDescribePayload</c>: the first non-empty provider name of the plugin's calendar
	/// integrations, or the manifest name when none sets one, and every account id once.
	/// </summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Calendar,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Calendar.Describe,
			null,
			options);

	/// <summary>
	/// Reads a <c>CalendarAccountsResult</c> with the same account list <see cref="DescribeAsync" /> carries;
	/// an id two integrations announce is listed once, for the first of them.
	/// </summary>
	public Task<CapabilityInvocationOutcome> GetAccountsAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Calendar,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Calendar.Accounts,
			null,
			options);

	/// <summary>
	/// Reads a <c>CalendarListResult</c> from the integration that owns the account. An unknown account fails
	/// with <c>ProtocolErrorCodes.CapabilityUnavailable</c>; a provider that throws fails with
	/// <c>ProtocolErrorCodes.InternalError</c> rather than answering an empty list.
	/// </summary>
	public Task<CapabilityInvocationOutcome> GetCalendarsAsync(CalendarAccountArguments arguments,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Calendar,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Calendar.Calendars,
			arguments,
			options);

	/// <summary>
	/// Reads a <c>CalendarEventsResult</c>: event summaries without description and participants, ordered by
	/// start and cut to <c>ProtocolLimits.MaxCalendarReplyBytes</c> with <c>Truncated</c> set when events were
	/// left out. Fails like <see cref="GetCalendarsAsync" />.
	/// </summary>
	public Task<CapabilityInvocationOutcome> GetEventsAsync(CalendarEventsArguments arguments,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Calendar,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Calendar.Events,
			arguments,
			options);

	/// <summary>
	/// Reads a <c>CalendarEventResult</c> with the full event, or a null <c>Event</c> when the provider no
	/// longer has it. Fails like <see cref="GetCalendarsAsync" />.
	/// </summary>
	public Task<CapabilityInvocationOutcome> GetEventAsync(CalendarEventArguments arguments,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Calendar,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Calendar.Event,
			arguments,
			options);
}
