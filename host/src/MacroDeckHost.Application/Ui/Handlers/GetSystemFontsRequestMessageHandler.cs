using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.System;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetSystemFontsRequestMessageHandler
	: IUiTransportMessageHandler<GetSystemFontsRequest, GetSystemFontsResponse>
{
	private readonly IFontCatalog _fontCatalog;

	public GetSystemFontsRequestMessageHandler(IFontCatalog fontCatalog)
	{
		_fontCatalog = fontCatalog;
	}

	public ValueTask<GetSystemFontsResponse> Handle(
		GetSystemFontsRequest request,
		CancellationToken cancellationToken)
	{
		var response = new GetSystemFontsResponse();
		response.Faces.AddRange(_fontCatalog.GetFaces()
			.Select(face => new SystemFontFace
			{
				FaceId = face.FaceId,
				Family = face.Family,
				Weight = face.Weight,
				Width = face.Width,
				Slant = face.Slant,
				StyleName = face.StyleName,
				RemoteRenderable = face.RemoteRenderable
			}));
		return ValueTask.FromResult(response);
	}
}
