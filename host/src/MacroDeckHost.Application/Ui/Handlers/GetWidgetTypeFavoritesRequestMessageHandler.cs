using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class GetWidgetTypeFavoritesRequestMessageHandler
	: IUiTransportMessageHandler<GetWidgetTypeFavoritesRequest, GetWidgetTypeFavoritesResponse>
{
	private readonly IWidgetTypeFavoritesService _favorites;

	public GetWidgetTypeFavoritesRequestMessageHandler(IWidgetTypeFavoritesService favorites)
	{
		_favorites = favorites;
	}

	public async ValueTask<GetWidgetTypeFavoritesResponse> Handle(
		GetWidgetTypeFavoritesRequest request,
		CancellationToken cancellationToken)
		=> new() { TypeIds = await _favorites.GetFavorites(cancellationToken) };
}
