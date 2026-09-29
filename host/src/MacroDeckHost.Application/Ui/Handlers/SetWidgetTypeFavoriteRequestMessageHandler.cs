using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class SetWidgetTypeFavoriteRequestMessageHandler
	: IUiTransportMessageHandler<SetWidgetTypeFavoriteRequest, SetWidgetTypeFavoriteResponse>
{
	public const string UnknownTypeCode = "UNKNOWN_WIDGET_TYPE";
	public const string LimitReachedCode = "FAVORITES_LIMIT";

	private readonly IWidgetTypeFavoritesService _favorites;

	public SetWidgetTypeFavoriteRequestMessageHandler(IWidgetTypeFavoritesService favorites)
	{
		_favorites = favorites;
	}

	public async ValueTask<SetWidgetTypeFavoriteResponse> Handle(
		SetWidgetTypeFavoriteRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _favorites.SetFavorite(request.WidgetTypeId, request.Favorite, cancellationToken);

		var error = result.Outcome switch
		{
			WidgetTypeFavoriteOutcome.UnknownType => new TransportError
				{ Code = UnknownTypeCode, Message = AppStrings.Errors.Widgets.UnknownType() },
			WidgetTypeFavoriteOutcome.LimitReached => new TransportError
			{
				Code = LimitReachedCode,
				Message = AppStrings.Errors.Widgets.TooManyFavorites(max: WidgetTypeFavoritesService.MaxFavorites)
			},
			_ => null
		};

		return new SetWidgetTypeFavoriteResponse { Success = error is null, Error = error, TypeIds = result.TypeIds };
	}
}
