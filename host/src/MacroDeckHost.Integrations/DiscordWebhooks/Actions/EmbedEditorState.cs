using MacroDeck.Ui.Runtime;
using MacroDeckHost.Integrations.DiscordWebhooks.Models;

namespace MacroDeckHost.Integrations.DiscordWebhooks.Actions;

internal sealed class EmbedEditorState
{
	public const string TimestampNone = "none";
	public const string TimestampFixed = "fixed";
	public const string TimestampNow = "now";

	public EmbedEditorState(DiscordEmbedDraft draft)
	{
		Title = new UiState<string>(draft.Title);
		Description = new UiState<string>(draft.Description);
		Url = new UiState<string>(draft.Url);
		Color = new UiState<string>(draft.Color);
		TimestampMode = new UiState<string>(draft.Timestamp.Length == 0
			? TimestampNone
			: draft.Timestamp == DiscordMessageMap.TimestampNow
				? TimestampNow
				: TimestampFixed);
		TimestampValue = new UiState<string>(draft.Timestamp == DiscordMessageMap.TimestampNow
			? string.Empty
			: draft.Timestamp);
		AuthorName = new UiState<string>(draft.AuthorName);
		AuthorUrl = new UiState<string>(draft.AuthorUrl);
		AuthorIconUrl = new UiState<string>(draft.AuthorIconUrl);
		FooterText = new UiState<string>(draft.FooterText);
		FooterIconUrl = new UiState<string>(draft.FooterIconUrl);
		Image = new UiState<string>(draft.Image);
		Thumbnail = new UiState<string>(draft.Thumbnail);
		Fields = new UiState<IReadOnlyList<FieldEditorState>>(draft.Fields.Select(field => new FieldEditorState(field))
			.ToList());
	}

	public string Id { get; } = EditorIds.Next();

	public UiState<string> Title { get; }

	public UiState<string> Description { get; }

	public UiState<string> Url { get; }

	public UiState<string> Color { get; }

	public UiState<string> TimestampMode { get; }

	public UiState<string> TimestampValue { get; }

	public UiState<string> AuthorName { get; }

	public UiState<string> AuthorUrl { get; }

	public UiState<string> AuthorIconUrl { get; }

	public UiState<string> FooterText { get; }

	public UiState<string> FooterIconUrl { get; }

	public UiState<string> Image { get; }

	public UiState<string> Thumbnail { get; }

	public UiState<IReadOnlyList<FieldEditorState>> Fields { get; }

	public DiscordEmbedDraft ToDraft()
	{
		return new DiscordEmbedDraft
		{
			Title = Title.Value,
			Description = Description.Value,
			Url = Url.Value.Trim(),
			Color = Color.Value.Trim(),
			Timestamp = TimestampMode.Value switch
			{
				TimestampNow => DiscordMessageMap.TimestampNow,
				TimestampFixed => TimestampValue.Value.Trim(),
				_ => string.Empty
			},
			AuthorName = AuthorName.Value,
			AuthorUrl = AuthorUrl.Value.Trim(),
			AuthorIconUrl = AuthorIconUrl.Value.Trim(),
			FooterText = FooterText.Value,
			FooterIconUrl = FooterIconUrl.Value.Trim(),
			Image = Image.Value.Trim(),
			Thumbnail = Thumbnail.Value.Trim(),
			Fields = Fields.Value.Select(field => field.ToDraft()).ToList()
		};
	}
}

internal sealed class FieldEditorState
{
	public FieldEditorState(DiscordFieldDraft draft)
	{
		Name = new UiState<string>(draft.Name);
		Value = new UiState<string>(draft.Value);
		Inline = new UiState<bool>(draft.Inline);
	}

	public string Id { get; } = EditorIds.Next();

	public UiState<string> Name { get; }

	public UiState<string> Value { get; }

	public UiState<bool> Inline { get; }

	public DiscordFieldDraft ToDraft()
	{
		return new DiscordFieldDraft(Name.Value, Value.Value, Inline.Value);
	}
}

internal static class EditorIds
{
	public static string Next()
	{
		return Guid.NewGuid().ToString("N")[..12];
	}
}
