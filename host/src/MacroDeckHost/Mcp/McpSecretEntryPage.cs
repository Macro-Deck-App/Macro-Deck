using System.Net;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Localization;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Mcp;

public static class McpSecretEntryPage
{
	public const int MaxValueLength = 8192;
	private const string FormValueField = "value";
	private const string FormNonceField = "nonce";

	private const string Style = "body{font-family:system-ui,sans-serif;margin:0;padding:48px 16px;background:#f6f7f9;"
		+ "color:#1c1e21}main{max-width:480px;margin:0 auto;background:#fff;border-radius:12px;padding:32px;"
		+ "box-shadow:0 1px 3px rgba(0,0,0,.12)}h1{font-size:20px;margin:0 0 12px}p{line-height:1.5}"
		+ "label{display:block;font-weight:600;margin:24px 0 8px}input{box-sizing:border-box;width:100%;"
		+ "padding:10px 12px;font-size:16px;border:1px solid #c9ccd1;border-radius:8px}"
		+ "button{margin-top:16px;padding:10px 16px;font-size:16px;border:0;border-radius:8px;background:#2f6fec;"
		+ "color:#fff}.error{color:#b42318}@media (prefers-color-scheme:dark){body{background:#17181a;"
		+ "color:#e7e9ec}main{background:#232528}input{background:#17181a;color:#e7e9ec;border-color:#44474c}}";

	private static readonly string _styleHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Style)));

	public static void MapMcpSecretEntryPage(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet(McpPaths.SecretEntry + "/{id}", Show).AllowAnonymous();
		endpoints.MapPost(McpPaths.SecretEntry + "/{id}", Submit).AllowAnonymous().DisableAntiforgery();
	}

	private static async Task Show(
		string id,
		HttpContext context,
		IMcpSecretEntryStore store,
		IAppPreferenceService preferences,
		ILocalizationResolver resolver)
	{
		var culture = (await preferences.GetLocalization()).Culture;
		var entry = store.Find(id);
		if (entry is null)
		{
			await WriteInvalid(context, resolver, culture);
			return;
		}

		await WriteForm(context, resolver, culture, entry, store.IssueFormNonce(entry), null);
	}

	private static async Task Submit(
		string id,
		HttpContext context,
		IMcpSecretEntryStore store,
		ISecretService secrets,
		IMcpSecretLedger ledger,
		IAppPreferenceService preferences,
		ILocalizationResolver resolver)
	{
		var culture = (await preferences.GetLocalization()).Culture;
		if (!context.Request.HasFormContentType)
		{
			await WriteInvalid(context, resolver, culture);
			return;
		}

		var form = await context.Request.ReadFormAsync(context.RequestAborted);
		var value = form[FormValueField].ToString();
		var pending = store.Find(id);
		if (pending is not null && (value.Length == 0 || value.Length > MaxValueLength))
		{
			await WriteForm(context,
				resolver,
				culture,
				pending,
				store.IssueFormNonce(pending),
				AppStrings.Mcp.SecretEntry.EmptyValue());
			return;
		}

		if (!store.TryConsume(id, form[FormNonceField].ToString(), out var entry) || entry is null)
		{
			await WriteInvalid(context, resolver, culture);
			return;
		}

		var secretId = await secrets.Create(value, entry.Kind);
		ledger.Grant(entry.TokenId, secretId);
		store.Complete(entry, secretId);

		await WritePage(context,
			resolver.Resolve(AppStrings.Mcp.SecretEntry.SavedHeading(), culture),
			$"<h1>{Encode(resolver.Resolve(AppStrings.Mcp.SecretEntry.SavedHeading(), culture))}</h1>"
			+ $"<p>{Encode(resolver.Resolve(AppStrings.Mcp.SecretEntry.SavedMessage(entry.ClientName), culture))}</p>",
			StatusCodes.Status200OK);
	}

	private static Task WriteForm(
		HttpContext context,
		ILocalizationResolver resolver,
		string culture,
		McpSecretEntry entry,
		string nonce,
		LocalizedString? error)
	{
		var title = resolver.Resolve(AppStrings.Mcp.SecretEntry.PageTitle(), culture);
		var lead = resolver.Resolve(AppStrings.Mcp.SecretEntry.Lead(entry.ClientName, entry.Label), culture);
		var privacy = resolver.Resolve(AppStrings.Mcp.SecretEntry.Privacy(entry.ClientName), culture);
		var valueLabel = resolver.Resolve(AppStrings.Mcp.SecretEntry.ValueLabel(), culture);
		var save = resolver.Resolve(AppStrings.Mcp.SecretEntry.SaveAction(), culture);
		var errorLine = error is { } message
			? $"<p class=\"error\">{Encode(resolver.Resolve(message, culture))}</p>"
			: string.Empty;
		string[] parts =
		[
			$"<h1>{Encode(title)}</h1><p>{Encode(lead)}</p><p>{Encode(privacy)}</p>",
			$"<form method=\"post\" action=\"{McpPaths.SecretEntry}/{Encode(entry.Id)}\" autocomplete=\"off\">",
			$"<input type=\"hidden\" name=\"{FormNonceField}\" value=\"{Encode(nonce)}\">",
			$"<label for=\"{FormValueField}\">{Encode(valueLabel)}</label>",
			$"<input id=\"{FormValueField}\" name=\"{FormValueField}\" type=\"password\" required autofocus>",
			errorLine,
			$"<button type=\"submit\">{Encode(save)}</button></form>"
		];

		return WritePage(context, title, string.Concat(parts), StatusCodes.Status200OK);
	}

	private static Task WriteInvalid(HttpContext context, ILocalizationResolver resolver, string culture)
	{
		var heading = resolver.Resolve(AppStrings.Mcp.SecretEntry.InvalidHeading(), culture);
		return WritePage(context,
			heading,
			$"<h1>{Encode(heading)}</h1>"
			+ $"<p>{Encode(resolver.Resolve(AppStrings.Mcp.SecretEntry.InvalidMessage(), culture))}</p>",
			StatusCodes.Status404NotFound);
	}

	private static async Task WritePage(HttpContext context, string title, string body, int status)
	{
		var headers = context.Response.Headers;
		headers.CacheControl = "no-store";
		headers["Content-Security-Policy"] = $"default-src 'none'; style-src 'sha256-{_styleHash}'; "
			+ "form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
		headers["Referrer-Policy"] = "no-referrer";
		headers["X-Content-Type-Options"] = "nosniff";
		context.Response.StatusCode = status;
		context.Response.ContentType = "text/html; charset=utf-8";

		await context.Response.WriteAsync("<!doctype html><html><head><meta charset=\"utf-8\">"
			+ "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
			+ $"<title>{Encode(title)} - Macro Deck</title><style>{Style}</style></head>"
			+ $"<body><main>{body}</main></body></html>");
	}

	private static string Encode(string? text)
	{
		return WebUtility.HtmlEncode(text ?? string.Empty);
	}
}
