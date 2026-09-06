using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetIconPacksRequestMessageHandler : IUiTransportMessageHandler<GetIconPacksRequest, GetIconPacksResponse>
{
	private readonly IIconPackCache _iconPackCache;
	private readonly IIconPackOwnerRegistry _ownerRegistry;

	public GetIconPacksRequestMessageHandler(IIconPackCache iconPackCache, IIconPackOwnerRegistry ownerRegistry)
	{
		_iconPackCache = iconPackCache;
		_ownerRegistry = ownerRegistry;
	}

	public ValueTask<GetIconPacksResponse> Handle(GetIconPacksRequest request, CancellationToken cancellationToken)
	{
		var response = new GetIconPacksResponse();
		foreach (var pack in _iconPackCache
			.GetAllPacks()
			.OrderByDescending(p => p.IsDefault)
			.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
		{
			response.Packs.Add(IconMapper.ToDto(pack,
				_iconPackCache.GetIconCount(pack.Id),
				_ownerRegistry.Describe(pack)));
		}

		return ValueTask.FromResult(response);
	}
}
