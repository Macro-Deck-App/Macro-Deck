using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Integrations.Clipboard.State;
using MacroDeckHost.Localization;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Clipboard.Events;

namespace MacroDeckHost.Integrations.Clipboard.Events;

internal static class ClipboardEventIds
{
	public const string ContentChanged = "content-changed";
	public const string TextMatched = "text-matched";
	public const string ContentTypeChanged = "content-type-changed";
}

internal static class ClipboardEventPayload
{
	public const string ContentType = "contentType";
	public const string PreviousContentType = "previousContentType";
	public const string Text = "text";
	public const string Pattern = "pattern";
	public const string Match = "match";
}

internal static class ClipboardEventDefinitions
{
	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		new()
		{
			Id = ClipboardEventIds.ContentChanged,
			Name = Strings.ContentChanged.Name(),
			Description = Strings.ContentChanged.Description(),
			Category = AppStrings.Integrations.Clipboard.Name(),
			IconName = "clipboard",
			ConfigurationParameters = [ContentTypeParameter()],
			PayloadParameters =
			[
				ContentTypeParameter(),
				ActionParameter.Text(ClipboardEventPayload.Text, Strings.TextLabel())
			]
		},
		new()
		{
			Id = ClipboardEventIds.TextMatched,
			Name = Strings.TextMatched.Name(),
			Description = Strings.TextMatched.Description(),
			Category = AppStrings.Integrations.Clipboard.Name(),
			IconName = "search",
			ConfigurationParameters =
			[
				new ActionParameter
				{
					Name = ClipboardEventPayload.Pattern,
					Type = ActionParameterType.String,
					Label = Strings.TextMatched.PatternLabel(),
					Description = Strings.TextMatched.PatternDescription(),
					Required = true,
					LiteralOnly = true
				}
			],
			PayloadParameters =
			[
				ActionParameter.Text(ClipboardEventPayload.Pattern, Strings.TextMatched.PatternLabel()),
				ActionParameter.Text(ClipboardEventPayload.Text, Strings.TextLabel()),
				ActionParameter.Text(ClipboardEventPayload.Match, Strings.TextMatched.MatchLabel())
			]
		},
		new()
		{
			Id = ClipboardEventIds.ContentTypeChanged,
			Name = Strings.ContentTypeChanged.Name(),
			Description = Strings.ContentTypeChanged.Description(),
			Category = AppStrings.Integrations.Clipboard.Name(),
			IconName = "refresh",
			ConfigurationParameters = [ContentTypeParameter()],
			PayloadParameters =
			[
				ContentTypeParameter(),
				ActionParameter.Choice(ClipboardEventPayload.PreviousContentType,
					ClipboardContentType.Options,
					Strings.ContentTypeChanged.PreviousContentTypeLabel())
			]
		}
	];

	private static ActionParameter ContentTypeParameter()
	{
		return ActionParameter.Choice(ClipboardEventPayload.ContentType,
			ClipboardContentType.Options,
			Strings.ContentTypeLabel());
	}
}
