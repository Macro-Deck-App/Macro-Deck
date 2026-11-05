using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Application.Profiles;

public static class VirtualWidgetIds
{
	private const char Separator = ':';

	public static bool TryCreate(
		string integrationId,
		string profileId,
		string folderId,
		string widgetId,
		out string qualified)
	{
		if (IsUsablePart(profileId)
			&& IsUsablePart(folderId)
			&& IsUsablePart(widgetId)
			&& QualifiedId.TryCreate(integrationId,
				string.Join(Separator, Escape(profileId), Escape(folderId), Escape(widgetId)),
				LocalIdKind.Resource,
				out var id))
		{
			qualified = id.ToString();
			return true;
		}

		qualified = string.Empty;
		return false;
	}

	public static bool TryParse(
		string qualified,
		out string integrationId,
		out string profileId,
		out string folderId,
		out string widgetId)
	{
		integrationId = string.Empty;
		profileId = string.Empty;
		folderId = string.Empty;
		widgetId = string.Empty;

		if (!QualifiedId.TryParse(qualified, out var id))
		{
			return false;
		}

		var parts = id.LocalId.Split(Separator);
		if (parts.Length != 3)
		{
			return false;
		}

		integrationId = id.OwnerId;
		profileId = Uri.UnescapeDataString(parts[0]);
		folderId = Uri.UnescapeDataString(parts[1]);
		widgetId = Uri.UnescapeDataString(parts[2]);
		return true;
	}

	private static bool IsUsablePart(string part)
	{
		return part.Length > 0 && !part.Contains(QualifiedId.Separator, StringComparison.Ordinal);
	}

	private static string Escape(string part)
	{
		return part.Replace("%", "%25", StringComparison.Ordinal).Replace(":", "%3A", StringComparison.Ordinal);
	}
}
