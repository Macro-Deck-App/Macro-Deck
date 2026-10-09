using MacroDeckHost.Application.ColorPalette;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class SetColorPaletteEntryRequestMessageHandler
	: IUiTransportMessageHandler<SetColorPaletteEntryRequest, SetColorPaletteEntryResponse>
{
	public const string InvalidColorCode = "INVALID_COLOR";
	public const string LimitReachedCode = "COLOR_PALETTE_LIMIT";

	private readonly IColorPaletteService _palette;

	public SetColorPaletteEntryRequestMessageHandler(IColorPaletteService palette)
	{
		_palette = palette;
	}

	public async ValueTask<SetColorPaletteEntryResponse> Handle(
		SetColorPaletteEntryRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _palette.SetColor(request.Color, request.Present, cancellationToken);

		var error = result.Outcome switch
		{
			ColorPaletteOutcome.InvalidColor => new TransportError
				{ Code = InvalidColorCode, Message = AppStrings.Errors.ColorPalette.InvalidColor() },
			ColorPaletteOutcome.LimitReached => new TransportError
			{
				Code = LimitReachedCode,
				Message = AppStrings.Errors.ColorPalette.Full(max: ColorPaletteService.MaxColors)
			},
			_ => null
		};

		return new SetColorPaletteEntryResponse { Success = error is null, Error = error, Colors = result.Colors };
	}
}
