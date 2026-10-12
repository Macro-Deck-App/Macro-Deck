using System.Globalization;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Locale;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteLocaleApi(IHostInvoker invoker) : ILocaleApi
{
	public async Task<TimeOfDayFormat> GetTimeOfDayFormatAsync(CancellationToken cancellationToken = default)
	{
		JsonElement? result;
		try
		{
			result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Locale,
				HostOperations.Locale.TimeOfDayFormat,
				null,
				cancellationToken);
		}
		catch (HostInvocationException)
		{
			return await LocalLocaleApi.Instance.GetTimeOfDayFormatAsync(cancellationToken);
		}

		var answer = result?.Deserialize<LocaleTimeOfDayFormatResult>(PluginProtocolJson.Options);
		return new TimeOfDayFormat(CultureOf(answer?.Culture),
			answer?.HourCycle is HourCycles.H12 ? HourCycles.H12 : HourCycles.H23);
	}

	private static CultureInfo CultureOf(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return CultureInfo.CurrentCulture;
		}

		try
		{
			return CultureInfo.GetCultureInfo(name);
		}
		catch (CultureNotFoundException)
		{
			return CultureInfo.CurrentCulture;
		}
	}
}
