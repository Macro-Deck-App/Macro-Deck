using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/settings/color-palette")]
public class ColorPaletteController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetColorPaletteRequest, GetColorPaletteResponse> _getPalette;

	private readonly IUiTransportMessageHandler<SetColorPaletteEntryRequest, SetColorPaletteEntryResponse>
		_setPaletteEntry;

	private readonly IUiTransportMessageHandler<RestoreDefaultColorPaletteRequest, GetColorPaletteResponse>
		_restoreDefaults;

	public ColorPaletteController(
		IUiTransportMessageHandler<GetColorPaletteRequest, GetColorPaletteResponse> getPalette,
		IUiTransportMessageHandler<SetColorPaletteEntryRequest, SetColorPaletteEntryResponse> setPaletteEntry,
		IUiTransportMessageHandler<RestoreDefaultColorPaletteRequest, GetColorPaletteResponse> restoreDefaults)
	{
		_getPalette = getPalette;
		_setPaletteEntry = setPaletteEntry;
		_restoreDefaults = restoreDefaults;
	}

	[HttpGet]
	public Task<GetColorPaletteResponse> Get(CancellationToken ct)
		=> _getPalette.Handle(new GetColorPaletteRequest(), ct).AsTask();

	[HttpPut]
	public Task<SetColorPaletteEntryResponse> SetEntry(SetColorPaletteEntryRequest body, CancellationToken ct)
		=> _setPaletteEntry.Handle(body, ct).AsTask();

	[HttpPost("restore-defaults")]
	public Task<GetColorPaletteResponse> RestoreDefaults(CancellationToken ct)
		=> _restoreDefaults.Handle(new RestoreDefaultColorPaletteRequest(), ct).AsTask();
}
