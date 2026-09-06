using System.Globalization;
using System.Security;
using System.Text;

namespace MacroDeckHost.Infrastructure.Autostart;

public static class AutostartEntryContent
{
	public const string LaunchAgentLabel = "app.macro-deck.macrodeck";

	public const string DesktopEntryName = "macro-deck";

	public const string WindowsRunValueName = "MacroDeck";

	public const string AutostartArgument = "--autostart";

	public const string MinimizedArgument = "--minimized";

	public static string BuildLaunchAgentPlist(AutostartRegistration registration)
	{
		var builder = new StringBuilder();
		builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
		builder.AppendLine(
			"<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
		builder.AppendLine("<plist version=\"1.0\">");
		builder.AppendLine("<dict>");
		builder.AppendLine("\t<key>Label</key>");
		builder.AppendLine(CultureInfo.InvariantCulture, $"\t<string>{LaunchAgentLabel}</string>");
		builder.AppendLine("\t<key>ProgramArguments</key>");
		builder.AppendLine("\t<array>");
		foreach (var argument in BuildArguments(registration))
		{
			builder.AppendLine(CultureInfo.InvariantCulture,
				$"\t\t<string>{SecurityElement.Escape(argument)}</string>");
		}

		builder.AppendLine("\t</array>");
		builder.AppendLine("\t<key>RunAtLoad</key>");
		builder.AppendLine("\t<true/>");
		builder.AppendLine("</dict>");
		builder.AppendLine("</plist>");
		return builder.ToString();
	}

	public static string BuildDesktopEntry(AutostartRegistration registration)
	{
		var builder = new StringBuilder();
		builder.AppendLine("[Desktop Entry]");
		builder.AppendLine("Type=Application");
		builder.AppendLine("Name=Macro Deck");
		builder.AppendLine(CultureInfo.InvariantCulture, $"Exec={BuildCommandLine(registration)}");
		builder.AppendLine("X-GNOME-Autostart-enabled=true");
		builder.AppendLine("Terminal=false");
		return builder.ToString();
	}

	public static string BuildCommandLine(AutostartRegistration registration)
	{
		var builder = new StringBuilder();
		builder.Append('"').Append(registration.ExecutablePath).Append('"');
		builder.Append(' ').Append(AutostartArgument);
		if (registration.OpenMinimized)
		{
			builder.Append(' ').Append(MinimizedArgument);
		}

		return builder.ToString();
	}

	public static IReadOnlyList<string> BuildArguments(AutostartRegistration registration)
	{
		return registration.OpenMinimized
			? [registration.ExecutablePath, AutostartArgument, MinimizedArgument]
			: [registration.ExecutablePath, AutostartArgument];
	}

	public static AutostartRegistration? ParseCommandLine(string? commandLine)
	{
		if (string.IsNullOrWhiteSpace(commandLine))
		{
			return null;
		}

		var trimmed = commandLine.Trim();
		string executable;
		string rest;
		if (trimmed.StartsWith('"'))
		{
			var closing = trimmed.IndexOf('"', 1);
			if (closing < 0)
			{
				return null;
			}

			executable = trimmed[1..closing];
			rest = trimmed[(closing + 1)..];
		}
		else
		{
			var space = trimmed.IndexOf(' ');
			executable = space < 0 ? trimmed : trimmed[..space];
			rest = space < 0 ? string.Empty : trimmed[space..];
		}

		if (executable.Length == 0 || !rest.Contains(AutostartArgument, StringComparison.Ordinal))
		{
			return null;
		}

		return new AutostartRegistration(executable, rest.Contains(MinimizedArgument, StringComparison.Ordinal));
	}

	public static AutostartRegistration? ParseLaunchAgentPlist(string? content)
	{
		if (string.IsNullOrWhiteSpace(content))
		{
			return null;
		}

		var arguments = new List<string>();
		foreach (var line in content.Split('\n'))
		{
			var value = line.Trim();
			if (value.StartsWith("<string>", StringComparison.Ordinal) &&
				value.EndsWith("</string>", StringComparison.Ordinal))
			{
				arguments.Add(SecurityElementUnescape(value["<string>".Length..^"</string>".Length]));
			}
		}

		if (arguments.Count < 3 || !arguments.Contains(AutostartArgument))
		{
			return null;
		}

		return new AutostartRegistration(arguments[1], arguments.Contains(MinimizedArgument));
	}

	private static string SecurityElementUnescape(string value)
	{
		return value
			.Replace("&lt;", "<", StringComparison.Ordinal)
			.Replace("&gt;", ">", StringComparison.Ordinal)
			.Replace("&quot;", "\"", StringComparison.Ordinal)
			.Replace("&apos;", "'", StringComparison.Ordinal)
			.Replace("&amp;", "&", StringComparison.Ordinal);
	}
}
