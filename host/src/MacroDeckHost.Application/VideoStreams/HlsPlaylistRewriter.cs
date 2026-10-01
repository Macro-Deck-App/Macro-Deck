using System.Text;

namespace MacroDeckHost.Application.VideoStreams;

public static class HlsPlaylistRewriter
{
	public const int MaxPlaylistBytes = 1024 * 1024;

	private const string Signature = "#EXTM3U";
	private const char ByteOrderMark = '﻿';

	public static bool HasPlaylistSignature(ReadOnlySpan<byte> start)
	{
		if (start.Length >= 3 && start[0] == 0xEF && start[1] == 0xBB && start[2] == 0xBF)
		{
			start = start[3..];
		}

		return start.Length >= Signature.Length && start[..Signature.Length].SequenceEqual("#EXTM3U"u8);
	}

	public static bool TryRewrite(string playlist, string token, Uri pinnedOrigin, Uri playlistUrl, out string rewritten)
	{
		ArgumentNullException.ThrowIfNull(playlist);
		ArgumentException.ThrowIfNullOrEmpty(token);
		ArgumentNullException.ThrowIfNull(pinnedOrigin);
		ArgumentNullException.ThrowIfNull(playlistUrl);

		rewritten = string.Empty;
		var output = new StringBuilder(playlist.Length + 256);
		var position = 0;
		var first = true;
		while (position < playlist.Length)
		{
			var newline = playlist.IndexOf('\n', position);
			var end = newline < 0 ? playlist.Length : newline;
			var contentEnd = end > position && playlist[end - 1] == '\r' ? end - 1 : end;
			var line = playlist[position..contentEnd];

			if (first)
			{
				first = false;
				if (line.StartsWith(ByteOrderMark))
				{
					output.Append(ByteOrderMark);
					line = line[1..];
				}

				if (!string.Equals(line.TrimEnd(' ', '\t'), Signature, StringComparison.Ordinal))
				{
					return false;
				}

				output.Append(line);
			}
			else if (!TryRewriteLine(line, token, pinnedOrigin, playlistUrl, output))
			{
				return false;
			}

			output.Append(playlist, contentEnd, end - contentEnd);
			if (newline >= 0)
			{
				output.Append('\n');
			}

			position = end + 1;
		}

		if (first)
		{
			return false;
		}

		rewritten = output.ToString();
		return true;
	}

	private static bool TryRewriteLine(string line, string token, Uri pinnedOrigin, Uri playlistUrl, StringBuilder output)
	{
		var trimmed = line.Trim();
		if (trimmed.Length == 0)
		{
			output.Append(line);
			return true;
		}

		if (trimmed[0] != '#')
		{
			if (!TryRewriteUri(trimmed, token, pinnedOrigin, playlistUrl, out var relayed))
			{
				return false;
			}

			output.Append(relayed);
			return true;
		}

		// An EXTINF title is free text that a player never treats as a URI.
		if (!trimmed.StartsWith("#EXT", StringComparison.Ordinal) ||
			trimmed.StartsWith("#EXTINF", StringComparison.Ordinal))
		{
			output.Append(line);
			return true;
		}

		return TryRewriteTag(line, token, pinnedOrigin, playlistUrl, output);
	}

	private static bool TryRewriteTag(string line, string token, Uri pinnedOrigin, Uri playlistUrl, StringBuilder output)
	{
		var colon = line.IndexOf(':', StringComparison.Ordinal);
		if (colon < 0)
		{
			output.Append(line);
			return true;
		}

		output.Append(line, 0, colon + 1);
		var index = colon + 1;
		var parsed = true;
		var replacements = new StringBuilder();
		while (index < line.Length)
		{
			var nameStart = index;
			while (index < line.Length && (char.IsAsciiLetterOrDigit(line[index]) || line[index] == '-'))
			{
				index++;
			}

			if (index == nameStart || index >= line.Length || line[index] != '=')
			{
				parsed = false;
				break;
			}

			var name = line[nameStart..index];
			index++;
			string value;
			var quoted = index < line.Length && line[index] == '"';
			if (quoted)
			{
				var close = line.IndexOf('"', index + 1);
				if (close < 0)
				{
					parsed = false;
					break;
				}

				value = line[(index + 1)..close];
				index = close + 1;
			}
			else
			{
				var comma = line.IndexOf(',', index);
				var valueEnd = comma < 0 ? line.Length : comma;
				value = line[index..valueEnd];
				index = valueEnd;
			}

			if (string.Equals(name, "URI", StringComparison.OrdinalIgnoreCase))
			{
				if (!quoted || !TryRewriteUri(value, token, pinnedOrigin, playlistUrl, out var relayed))
				{
					return false;
				}

				replacements.Append(name).Append("=\"").Append(relayed).Append('"');
			}
			else
			{
				replacements.Append(name).Append('=');
				if (quoted)
				{
					replacements.Append('"').Append(value).Append('"');
				}
				else
				{
					replacements.Append(value);
				}
			}

			if (index < line.Length)
			{
				if (line[index] != ',')
				{
					parsed = false;
					break;
				}

				replacements.Append(',');
				index++;
			}
		}

		if (!parsed)
		{
			// A payload that is not an attribute list, such as a duration, has no URI; a malformed one that
			// mentions URI could hide one from this rewrite but not from the player.
			if (line.Contains("URI", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			output.Length -= colon + 1;
			output.Append(line);
			return true;
		}

		output.Append(replacements);
		return true;
	}

	private static bool TryRewriteUri(string reference, string token, Uri pinnedOrigin, Uri playlistUrl, out string result)
	{
		result = reference;
		if (reference.Length == 0 || !Uri.TryCreate(playlistUrl, reference, out var resolved))
		{
			return false;
		}

		if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps)
		{
			return resolved.Scheme is "data" or "skd" &&
				reference.StartsWith(resolved.Scheme + ":", StringComparison.OrdinalIgnoreCase) &&
				!reference.Any(char.IsControl) &&
				!reference.Contains(' ', StringComparison.Ordinal);
		}

		if (resolved.Scheme != pinnedOrigin.Scheme ||
			resolved.Port != pinnedOrigin.Port ||
			!string.Equals(resolved.Host, pinnedOrigin.Host, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		result = VideoStreamRelay.PathPrefix + token + resolved.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped);
		return true;
	}
}
