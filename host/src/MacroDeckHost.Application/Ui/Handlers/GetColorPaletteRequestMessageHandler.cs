using MacroDeckHost.Application.ColorPalette;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class GetColorPaletteRequestMessageHandler
	: IUiTransportMessageHandler<GetColorPaletteRequest, GetColorPaletteResponse>
{
	private readonly IColorPaletteService _palette;

	public GetColorPaletteRequestMessageHandler(IColorPaletteService palette)
	{
		_palette = palette;
	}

	public async ValueTask<GetColorPaletteResponse> Handle(
		GetColorPaletteRequest request,
		CancellationToken cancellationToken)
		=> new() { Colors = await _palette.GetColors(cancellationToken) };
}
