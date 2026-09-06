using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetIconsRequestMessageHandler : IUiTransportMessageHandler<GetIconsRequest, GetIconsResponse>
{
	private readonly IIconPackCache _iconPackCache;

	public GetIconsRequestMessageHandler(IIconPackCache iconPackCache)
	{
		_iconPackCache = iconPackCache;
	}

	public ValueTask<GetIconsResponse> Handle(GetIconsRequest request, CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.PackId, out var packId))
		{
			return ValueTask.FromResult(new GetIconsResponse());
		}

		var response = new GetIconsResponse();
		response.Icons.AddRange(_iconPackCache.GetIconsByPackId(packId).Select(IconMapper.ToDto));

		return ValueTask.FromResult(response);
	}
}
