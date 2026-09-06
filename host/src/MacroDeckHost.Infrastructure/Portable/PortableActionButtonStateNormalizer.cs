using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Infrastructure.Portable;

/// <summary>
/// A portable archive can predate the #612 state-mode upgrade by a long time - import is the path most
/// likely to reintroduce legacy Action Button data long after release, so every import runs the same
/// upgrade and structural normalization every other host-controlled write does.
/// </summary>
internal static class PortableActionButtonStateNormalizer
{
	public static string? Normalize(string type, string? data)
	{
		if (type != WidgetTypeIds.ActionButton || string.IsNullOrWhiteSpace(data))
		{
			return data;
		}

		return ActionButtonStateJson.Normalize(ActionButtonStateJson.ParseDataBag(data)).ToJsonString();
	}
}
