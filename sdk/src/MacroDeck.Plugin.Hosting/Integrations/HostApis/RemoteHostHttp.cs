using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Http;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteHostHttp(HostStateCache stateCache) : IHostHttp
{
	public string UserAgent
		=> stateCache.Get<HttpStateDto>(Protocol.Callbacks.HostApis.Http)?.UserAgent is { Length: > 0 } userAgent
			? userAgent
			: DefaultHostHttp.Instance.UserAgent;
}
