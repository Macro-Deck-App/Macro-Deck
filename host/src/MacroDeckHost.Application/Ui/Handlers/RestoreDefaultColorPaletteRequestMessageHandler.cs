using MacroDeckHost.Application.ColorPalette;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class RestoreDefaultColorPaletteRequestMessageHandler
	: IUiTransportMessageHandler<RestoreDefaultColorPaletteRequest, GetColorPaletteResponse>
{
	private readonly IColorPaletteService _palette;

	public RestoreDefaultColorPaletteRequestMessageHandler(IColorPaletteService palette)
	{
		_palette = palette;
	}

	public async ValueTask<GetColorPaletteResponse> Handle(
		RestoreDefaultColorPaletteRequest request,
		CancellationToken cancellationToken)
		=> new() { Colors = await _palette.RestoreDefaults(cancellationToken) };
}
