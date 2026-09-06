using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Deck;

public static class ApplicationIdentityMatcher
{
	public static string Normalize(ApplicationIdentityKind kind, string value)
	{
		var trimmed = value.Trim();
		if (kind != ApplicationIdentityKind.ExecutablePath || trimmed.Length == 0)
		{
			return trimmed;
		}

		try
		{
			var full = Path.GetFullPath(trimmed);
			return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}
		catch
		{
			return trimmed;
		}
	}

	public static bool Matches(FolderFocusRule rule, FocusedApplication app)
	{
		if (!rule.Enabled)
		{
			return false;
		}

		var candidate = rule.IdentityKind switch
		{
			ApplicationIdentityKind.ExecutablePath => app.ExecutablePath,
			ApplicationIdentityKind.ProcessName => app.ProcessName,
			ApplicationIdentityKind.BundleId => app.BundleId,
			_ => null
		};

		return candidate is not null &&
			string.Equals(rule.ApplicationIdentity, candidate, StringComparison.OrdinalIgnoreCase);
	}

	public static bool SameTarget(FolderFocusRule a, FolderFocusRule b)
		=> a.DeviceId == b.DeviceId &&
			a.IdentityKind == b.IdentityKind &&
			string.Equals(a.ApplicationIdentity, b.ApplicationIdentity, StringComparison.OrdinalIgnoreCase);
}
