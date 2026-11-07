using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed record AskQuestionContent(
	UiNode Content,
	int SizeBytes,
	string? ChoiceId,
	string? ChoiceRowId,
	string? TextId);

internal static class AskQuestionDialog
{
	public const string YesActionId = "yes";
	public const string NoActionId = "no";
	public const string OkActionId = "ok";
	public const string CancelActionId = "cancel";

	public const int MaxChoices = 50;
	public const int MaxChoiceLength = 100;
	public const int MaxTextLength = 1000;

	// The documented size limit of a notification dialog's content tree.
	public const int MaxContentBytes = 32 * 1024;

	private const double TextSize = 0.07;

	private static readonly UiSurface _surface = new()
	{
		Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared
	};

	public static IReadOnlyList<string> ParseChoices(string raw)
	{
		var trimmed = raw.Trim();
		if (trimmed.StartsWith('[') && TryParseArray(trimmed, out var items))
		{
			return items;
		}

		return raw.Split('\n')
			.Select(line => line.Trim())
			.Where(line => line.Length > 0)
			.ToList();
	}

	public static IReadOnlyList<NotificationAction> Actions(string kind)
	{
		return kind == AskQuestionKinds.Confirm
			?
			[
				new NotificationAction
				{
					Id = YesActionId, Label = MacroDeckStrings.Common.Yes(), Style = NotificationActionStyle.Primary
				},
				new NotificationAction { Id = NoActionId, Label = MacroDeckStrings.Common.No() }
			]
			:
			[
				new NotificationAction
				{
					Id = OkActionId, Label = AppStrings.Feedback.Notification.Ok(),
					Style = NotificationActionStyle.Primary
				},
				new NotificationAction { Id = CancelActionId, Label = MacroDeckStrings.Common.Cancel() }
			];
	}

	public static AskQuestionContent Build(
		string kind,
		IReadOnlyList<string> choices,
		string defaultText,
		string placeholder,
		TimeSpan timeout)
	{
		var hint = new UiTextRun
		{
			Key = "hint", Text = TimeoutHint(timeout), Role = UiComponentTextRoles.Secondary, Size = TextSize
		};

		UiElement root = kind switch
		{
			AskQuestionKinds.Choose => new UiStack
			{
				Key = "ask", Gap = 0.03, Children = [ChoiceList(choices), hint]
			},
			AskQuestionKinds.Text => new UiStack
			{
				Key = "ask", Gap = 0.03, Children = [TextField(defaultText, placeholder), hint]
			},
			_ => new UiStack { Key = "ask", Children = [hint] }
		};

		var tree = UiViewBuilder.Build(_surface, root).Root;
		var choice = Find(tree, UiComponents.Segmented);

		return new AskQuestionContent(tree,
			UiCanonicalJson.SerializeToUtf8Bytes(tree).Length,
			choice?.Id,
			choice?.Fallback?.Id,
			Find(tree, UiComponents.TextField)?.Id);
	}

	private static UiList ChoiceList(IReadOnlyList<string> choices)
	{
		return new UiList
		{
			Key = "choices",
			Fill = true,
			Children =
			[
				new UiSegmented
				{
					Key = "choice",
					Direction = UiComponentDirections.Vertical,
					RequiredComponentVersion = 2,
					Selected = 0,
					Events = [ChangeEvent()],
					Children = Rows(choices),
					Fallback = new UiSegmented
					{
						Key = "choiceRow",
						Selected = 0,
						MainSize = 0.2,
						Events = [ChangeEvent()],
						Children = Labels(choices, "r")
					}
				}
			]
		};
	}

	private static UiTextField TextField(string defaultText, string placeholder)
	{
		return new UiTextField
		{
			Key = "answer",
			Text = defaultText.Length > MaxTextLength ? defaultText[..MaxTextLength] : defaultText,
			Placeholder = placeholder,
			Events = [ChangeEvent()]
		};
	}

	private static IReadOnlyList<UiElement> Rows(IReadOnlyList<string> choices)
	{
		return
		[
			.. choices.Select((choice, index) => new UiStack
			{
				Key = $"c{index}",
				Justify = UiComponentJustify.Center,
				Padding = 0.03,
				Children = [new UiTextRun { Key = "label", Text = choice, Size = TextSize }]
			})
		];
	}

	private static IReadOnlyList<UiElement> Labels(IReadOnlyList<string> choices, string prefix)
	{
		return
		[
			.. choices.Select((choice, index)
				=> new UiTextRun { Key = $"{prefix}{index}", Text = choice, Size = TextSize })
		];
	}

	private static UiEventHandler ChangeEvent()
	{
		return UiEventHandler.On(UiComponentEvents.Change,
			static () =>
			{
			});
	}

	private static LocalizedText TimeoutHint(TimeSpan timeout)
	{
		var seconds = (int)Math.Round(timeout.TotalSeconds);
		return seconds >= 60 && seconds % 60 == 0
			? AppStrings.Integrations.System.Actions.AskQuestion.TimeoutHintMinutes(seconds / 60)
			: AppStrings.Integrations.System.Actions.AskQuestion.TimeoutHintSeconds(seconds);
	}

	private static UiNode? Find(UiNode node, string type)
	{
		if (node.Type == type)
		{
			return node;
		}

		return node.Children.Select(child => Find(child, type)).FirstOrDefault(found => found is not null);
	}

	private static bool TryParseArray(string json, out IReadOnlyList<string> items)
	{
		items = [];
		try
		{
			using var document = JsonDocument.Parse(json);
			if (document.RootElement.ValueKind != JsonValueKind.Array)
			{
				return false;
			}

			var parsed = new List<string>();
			foreach (var element in document.RootElement.EnumerateArray())
			{
				switch (element.ValueKind)
				{
					case JsonValueKind.String:
						parsed.Add(element.GetString()!);
						break;
					case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False:
						parsed.Add(element.GetRawText());
						break;
					default:
						return false;
				}
			}

			items = [.. parsed.Select(item => item.Trim()).Where(item => item.Length > 0)];
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
