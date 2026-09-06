using System.Security.Claims;
using System.Security.Cryptography;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Auth;

namespace MacroDeckHost.Ui;

public enum UiWebSocketListener
{
	TrustedLoopback,
	Public
}

public sealed record UiWebSocketTicket(string Value);

public interface IUiWebSocketTickets
{
	bool TryCreate(HttpContext context, ClaimsPrincipal principal, out UiWebSocketTicket ticket);
	bool TryRedeem(HttpContext context, string? value, out ClaimsPrincipal principal);
}

public sealed class UiWebSocketTickets(TimeProvider timeProvider) : IUiWebSocketTickets
{
	private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(15);
	private const int PerPrincipalLimit = 4;
	private const int GlobalLimit = 1024;
	private readonly Dictionary<string, Entry> _tickets = new(StringComparer.Ordinal);
	private readonly object _gate = new();

	public bool TryCreate(HttpContext context, ClaimsPrincipal principal, out UiWebSocketTicket ticket)
	{
		ticket = default!;
		if (!TryClassify(context, out var listener))
		{
			return false;
		}

		var owner = Owner(principal);
		var bytes = RandomNumberGenerator.GetBytes(32);
		var value = Base64Url(bytes);
		var hash = Convert.ToHexString(SHA256.HashData(bytes));
		var entry = new Entry(Clone(principal), owner, listener, timeProvider.GetUtcNow() + Lifetime);
		lock (_gate)
		{
			Prune();
			if (_tickets.Count >= GlobalLimit ||
				_tickets.Values.Count(candidate => candidate.Owner == owner) >= PerPrincipalLimit ||
				!_tickets.TryAdd(hash, entry))
			{
				return false;
			}
		}

		ticket = new UiWebSocketTicket(value);
		return true;
	}

	public bool TryRedeem(HttpContext context, string? value, out ClaimsPrincipal principal)
	{
		principal = new ClaimsPrincipal();
		if (value is not { Length: 43 } || !TryClassify(context, out var listener))
		{
			return false;
		}

		byte[] bytes;
		try
		{
			bytes = Base64UrlDecode(value);
		}
		catch (FormatException)
		{
			return false;
		}

		if (bytes.Length != 32)
		{
			return false;
		}

		var hash = Convert.ToHexString(SHA256.HashData(bytes));
		Entry? entry;
		lock (_gate)
		{
			if (!_tickets.Remove(hash, out entry))
			{
				return false;
			}
		}

		if (entry.ExpiresAt <= timeProvider.GetUtcNow() || entry.Listener != listener)
		{
			return false;
		}

		principal = entry.Principal;
		return true;
	}

	private void Prune()
	{
		var now = timeProvider.GetUtcNow();
		foreach (var key in _tickets.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
		{
			_tickets.Remove(key);
		}
	}

	private static bool TryClassify(HttpContext context, out UiWebSocketListener listener)
	{
		if (LoopbackConnection.IsTrusted(context))
		{
			listener = UiWebSocketListener.TrustedLoopback;
			return true;
		}

		if (HostEndpoints.IsPublicPort(context.Connection.LocalPort))
		{
			listener = UiWebSocketListener.Public;
			return true;
		}

		listener = default;
		return false;
	}

	private static string Owner(ClaimsPrincipal principal)
		=> string.Join("|",
			principal.Claims
				.Where(claim => claim.Type is "sub" or AuthDefaults.ScopeClaim or AuthDefaults.DeviceClaim)
				.OrderBy(claim => claim.Type, StringComparer.Ordinal)
				.Select(claim => $"{claim.Type}:{claim.Value}"));

	private static ClaimsPrincipal Clone(ClaimsPrincipal principal)
		=> new(principal.Identities.Select(identity =>
			new ClaimsIdentity(identity.Claims,
				identity.AuthenticationType,
				identity.NameClaimType,
				identity.RoleClaimType)));

	private static string Base64Url(byte[] bytes)
		=> Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private static byte[] Base64UrlDecode(string value)
	{
		var padded = value.Replace('-', '+').Replace('_', '/');
		return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
	}

	private sealed record Entry(
		ClaimsPrincipal Principal,
		string Owner,
		UiWebSocketListener Listener,
		DateTimeOffset ExpiresAt);
}
