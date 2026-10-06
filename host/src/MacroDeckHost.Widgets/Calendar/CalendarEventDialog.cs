using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Calendar;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Applications;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Localization;
using ILogger = Serilog.ILogger;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Calendar;

namespace MacroDeckHost.Widgets.Calendar;

internal sealed record CalendarDialogContent(
	CalendarEventSummary? Summary,
	string? Description,
	IReadOnlyList<CalendarParticipant> Participants,
	bool Loaded)
{
	public bool IsMissing => Loaded && Summary is null;
}

// The UI tree limit counts encoded bytes and the wire escapes non-ASCII to six, so texts are bounded in bytes.
internal static class CalendarDialogText
{
	private const int MaxDescriptionBytes = 24 * 1024;

	public static string Title(string text) => CalendarText.ClipToJsonBytes(text, ProtocolLimits.MaxCalendarTitleLength);

	public static string? Location(string? text)
		=> text is null ? null : CalendarText.ClipToJsonBytes(text, ProtocolLimits.MaxCalendarLocationLength);

	public static string? Description(string? text)
		=> text is null ? null : CalendarText.ClipToJsonBytes(text, MaxDescriptionBytes);
}

internal sealed record CalendarDialogParticipant(string Key, string Name, LocalizedText Response, bool IsOrganizer);

internal sealed class CalendarEventDialogSession : IUiSession
{
	private readonly IExternalUrlOpener _opener;
	private readonly ILogger _logger;
	private readonly UiAsyncState<CalendarDialogContent> _content;
	private readonly UiState<LocalizedText> _feedback = new(default);
	private readonly UiView _view;

	public CalendarEventDialogSession(
		UiSurface surface,
		ICalendarEventCache cache,
		IExternalUrlOpener opener,
		CalendarFormat format,
		string calendarKey,
		string eventId,
		ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(cache);
		ArgumentNullException.ThrowIfNull(format);

		_opener = opener;
		_logger = logger;

		_content = ContentOf(cache, calendarKey, eventId, cache.Snapshot.FindEvent(calendarKey, eventId));

		_view = new UiView(surface, CalendarEventDialogView.Build(_content, _feedback, format, Join));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_content.Reload();
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

	public ValueTask DisposeAsync()
	{
		_view.Dispose();

		return ValueTask.CompletedTask;
	}

	private void Join()
	{
		var message = JoinMeeting(_opener, _content.Peek().Summary?.MeetingUrl, _logger);

		using (_view.Batch())
		{
			_feedback.Set(message);
		}
	}

	public static UiAsyncState<CalendarDialogContent> ContentOf(
		ICalendarEventCache cache,
		string calendarKey,
		string eventId,
		CalendarEventSummary? cached)
	{
		ArgumentNullException.ThrowIfNull(cache);

		return new UiAsyncState<CalendarDialogContent>(async cancellationToken =>
			{
				var details = await cache.GetEventDetailsAsync(calendarKey, eventId, cancellationToken)
					.ConfigureAwait(false);

				return details is null
					? new CalendarDialogContent(null, null, [], true)
					: new CalendarDialogContent(details.Summary,
						CalendarDialogText.Description(details.Description),
						details.Participants,
						true);
			},
			new CalendarDialogContent(cached, null, [], false));
	}

	public static LocalizedText JoinMeeting(IExternalUrlOpener opener, string? url, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(opener);
		ArgumentNullException.ThrowIfNull(logger);

		LocalizedText message;

		try
		{
			var opened = opener.Open(url);

			message = opened.Success
				? Strings.Details.MeetingOpened()
				: opened.Error == ExternalUrlOpenError.HostLocked
					? AppStrings.Integrations.Calendar.Actions.JoinMeeting.HostLocked()
					: AppStrings.Integrations.Calendar.Actions.JoinMeeting.OpenFailed();
		}
#pragma warning disable CA1031 // A press has nobody to report to; the dialog shows the failure instead.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			logger.Warning(exception, "Opening a calendar meeting link from the event dialog failed");
			message = AppStrings.Integrations.Calendar.Actions.JoinMeeting.OpenFailed();
		}

		return message;
	}
}

internal static class CalendarEventDialogView
{
	public const string Root = "calendarEvent";

	private static readonly UiSize _text = UiSize.FromBasis(0.042);
	private static readonly UiSize _smallText = UiSize.FromBasis(0.034);
	private static readonly UiSize _icon = UiSize.FromBasis(0.042);
	private static readonly UiSize _gap = UiSize.FromBasis(0.022);
	private static readonly UiSize _buttonHeight = UiSize.FromBasis(0.12);

	public static UiElement Build(
		UiAsyncState<CalendarDialogContent> content,
		UiState<LocalizedText> feedback,
		CalendarFormat format,
		Action join)
	{
		ArgumentNullException.ThrowIfNull(content);
		ArgumentNullException.ThrowIfNull(feedback);
		ArgumentNullException.ThrowIfNull(format);
		ArgumentNullException.ThrowIfNull(join);

		return new UiStack
		{
			Key = Root,
			Direction = UiComponentDirections.Vertical,
			Padding = 0.04,
			Gap = _gap,
			Children = Body(content, feedback, format, join),
		};
	}

	public static UiElement[] Body(
		UiAsyncState<CalendarDialogContent> content,
		UiState<LocalizedText> feedback,
		CalendarFormat format,
		Action join)
		=>
		[
			new UiWhen
			{
				Key = "missingGate",
				Condition = () => content.Value.IsMissing,
				Content = () => new UiTextRun
				{
					Key = "missing",
					Text = Strings.Details.Unavailable(),
					Size = _text,
					Role = UiComponentTextRoles.Secondary,
					Align = UiComponentAlignments.Center,
					Wrap = true,
					Fill = true,
				},
			},
			new UiWhen
			{
				Key = "eventGate",
				Condition = () => content.Value.Summary is not null,
				Content = () => Event(content, feedback, format, join),
			},
		];

	private static UiList Event(
		UiAsyncState<CalendarDialogContent> content,
		UiState<LocalizedText> feedback,
		CalendarFormat format,
		Action join)
		=> new()
		{
			Key = "event",
			Fill = true,
			Gap = _gap,
			Children =
			[
				IconLine("when",
					UiIcons.ClockType,
					UiText.Optional(() => content.Value.Summary is { } summary ? format.DetailsRange(summary) : default)),
				new UiStack
				{
					Key = "source",
					Direction = UiComponentDirections.Horizontal,
					Align = UiComponentAlignments.Center,
					Gap = _gap,
					Children =
					[
						new UiStack
						{
							Key = "color",
							MainSize = _icon,
							Background = UiValue.From(() => content.Value.Summary is { } summary
								? CalendarWidgetStates.Color(summary) ?? CalendarWidgetParts.DefaultBarColor
								: CalendarWidgetParts.DefaultBarColor),
						},
						new UiTextRun
						{
							Key = "text",
							Text = UiText.Optional(() => content.Value.Summary is { } summary
								? Source(summary)
								: default(LocalizedText)),
							Size = _smallText,
							Role = UiComponentTextRoles.Secondary,
							Wrap = true,
							Fill = true,
						},
					],
				},
				new UiWhen
				{
					Key = "locationGate",
					Condition = () => content.Value.Summary?.Location is not null,
					Content = () => IconLine("location",
						UiIcons.Pin,
						UiText.From(() => CalendarDialogText.Location(content.Value.Summary?.Location))),
				},
				new UiWhen
				{
					Key = "joinGate",
					Condition = () => content.Value.Summary?.MeetingUrl is not null,
					Content = () => Join(content, join),
				},
				new UiWhen
				{
					Key = "feedbackGate",
					Condition = () => !feedback.Value.IsEmpty,
					Content = () => new UiTextRun
					{
						Key = "feedback",
						Text = UiText.Optional(() => feedback.Value),
						Size = _text,
						Wrap = true,
					},
				},
				new UiWhen
				{
					Key = "descriptionGate",
					Condition = () => content.Value.Description is not null,
					Content = () => new UiStack
					{
						Key = "description",
						Direction = UiComponentDirections.Vertical,
						Gap = _gap,
						Children =
						[
							Heading("heading", Strings.Details.Description()),
							new UiTextRun
							{
								Key = "text",
								Text = UiText.From(() => content.Value.Description),
								Size = _text,
								Wrap = true,
							},
						],
					},
				},
				new UiWhen
				{
					Key = "participantsGate",
					Condition = () => Participants(content.Value).Count > 0,
					Content = () => new UiStack
					{
						Key = "participants",
						Direction = UiComponentDirections.Vertical,
						Gap = _gap,
						Children =
						[
							Heading("heading", Strings.Details.Participants()),
							new UiRepeat<CalendarDialogParticipant>
							{
								Key = "people",
								Items = UiValue.From(() => Participants(content.Value)),
								KeySelector = participant => participant.Key,
								Template = (participant, _) => Participant(participant),
							},
						],
					},
				},
			],
		};

	private static UiStack Join(UiAsyncState<CalendarDialogContent> content, Action join)
		=> new()
		{
			Key = "join",
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = _gap,
			Children =
			[
				new UiButton
				{
					Key = "joinButton",
					Direction = UiComponentDirections.Horizontal,
					Justify = UiComponentJustify.Center,
					Align = UiComponentAlignments.Center,
					Gap = _gap,
					MainSize = UiSize.FromBasis(0.5),
					Padding = _gap,
					Events = [UiEventHandler.On(UiComponentEvents.Press, join)],
					Children =
					[
						new UiIcon { Key = "icon", Icon = UiIcons.ExternalLink, Size = _icon, MainSize = _icon },
						new UiTextRun
						{
							Key = "label",
							Text = Strings.Details.JoinMeeting(),
							Size = _text,
							Weight = UiComponentTextWeights.Medium,
							MaxLines = 1,
						},
					],
				},
				new UiTextRun
				{
					Key = "host",
					Text = UiText.From(() => HostOf(content.Value.Summary?.MeetingUrl)),
					Size = _smallText,
					Role = UiComponentTextRoles.Secondary,
					MaxLines = 1,
					Fill = true,
				},
			],
		};

	private static UiStack Participant(CalendarDialogParticipant participant)
	{
		var status = new List<UiElement>();

		if (participant.IsOrganizer)
		{
			status.Add(new UiTextRun
			{
				Key = "organizer",
				Text = Strings.Details.Organizer(),
				Size = _smallText,
				Role = UiComponentTextRoles.Secondary,
				MaxLines = 1,
			});
		}

		if (!participant.Response.IsEmpty)
		{
			status.Add(new UiTextRun
			{
				Key = "response",
				Text = participant.Response,
				Size = _smallText,
				Role = UiComponentTextRoles.Secondary,
				MaxLines = 1,
			});
		}

		var lines = new List<UiElement>
		{
			new UiTextRun { Key = "name", Text = participant.Name, Size = _text, MaxLines = 1 },
		};

		if (status.Count > 0)
		{
			lines.Add(new UiStack
			{
				Key = "status",
				Direction = UiComponentDirections.Horizontal,
				Gap = _gap,
				Children = status,
			});
		}

		return new UiStack
		{
			Key = participant.Key,
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = _gap,
			Children =
			[
				new UiIcon
				{
					Key = "icon", Icon = UiIcons.User, Size = _icon, MainSize = _icon, Role = UiComponentTextRoles.Secondary,
				},
				new UiStack
				{
					Key = "lines",
					Direction = UiComponentDirections.Vertical,
					Fill = true,
					Children = lines,
				},
			],
		};
	}

	private static LocalizedText Source(CalendarEventSummary summary)
		=> string.Equals(summary.CalendarName, summary.AccountName, StringComparison.OrdinalIgnoreCase)
			? Strings.Config.AccountProvider(account: summary.AccountName, provider: summary.ProviderName)
			: Strings.Details.Source(calendar: summary.CalendarName,
				account: summary.AccountName,
				provider: summary.ProviderName);

	private static UiStack IconLine(string key, string icon, UiText text)
		=> new()
		{
			Key = key,
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = _gap,
			Children =
			[
				new UiIcon
				{
					Key = "icon", Icon = icon, Size = _icon, MainSize = _icon, Role = UiComponentTextRoles.Secondary,
				},
				new UiTextRun { Key = "text", Text = text, Size = _text, Wrap = true, Fill = true },
			],
		};

	private static UiTextRun Heading(string key, LocalizedString text)
		=> new()
		{
			Key = key,
			Text = text,
			Size = _smallText,
			Weight = UiComponentTextWeights.SemiBold,
			Role = UiComponentTextRoles.Muted,
		};

	private static IReadOnlyList<CalendarDialogParticipant> Participants(CalendarDialogContent content)
		=> [.. content.Participants
			.Select((participant, index) => (participant, index, name: Name(participant)))
			.Where(entry => entry.name is not null)
			.Select(entry => new CalendarDialogParticipant("p" + entry.index.ToString(System.Globalization.CultureInfo.InvariantCulture),
				entry.name!,
				Response(entry.participant.Response),
				entry.participant.IsOrganizer))];

	private static string? Name(CalendarParticipant participant)
		=> !string.IsNullOrWhiteSpace(participant.Name) ? CalendarDialogText.Title(participant.Name.Trim())
			: !string.IsNullOrWhiteSpace(participant.Email) ? CalendarDialogText.Title(participant.Email.Trim())
			: null;

	private static LocalizedText Response(CalendarResponseStatus response)
		=> response switch
		{
			CalendarResponseStatus.Accepted => Strings.Details.ResponseAccepted(),
			CalendarResponseStatus.Declined => Strings.Details.ResponseDeclined(),
			CalendarResponseStatus.Tentative => Strings.Details.ResponseTentative(),
			CalendarResponseStatus.NeedsAction => Strings.Details.ResponseNeedsAction(),
			_ => default,
		};

	private static string? HostOf(string? url)
		=> Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
}
