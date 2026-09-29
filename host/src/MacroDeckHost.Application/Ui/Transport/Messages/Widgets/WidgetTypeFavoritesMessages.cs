namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class GetWidgetTypeFavoritesRequest
{
}

public class GetWidgetTypeFavoritesResponse
{
	public bool Success { get; set; } = true;
	public TransportError? Error { get; set; }

	public IReadOnlyList<string> TypeIds { get; set; } = [];
}

public class SetWidgetTypeFavoriteRequest
{
	public string WidgetTypeId { get; set; } = string.Empty;

	public bool Favorite { get; set; }
}

public class SetWidgetTypeFavoriteResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }

	public IReadOnlyList<string> TypeIds { get; set; } = [];
}

public sealed record WidgetTypeFavoritesChangedEvent
{
	public IReadOnlyList<string> TypeIds { get; init; } = [];
}
