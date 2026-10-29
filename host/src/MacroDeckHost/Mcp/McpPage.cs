using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Mcp;

public static class McpPage
{
	private const string Style = "body{font-family:system-ui,sans-serif;margin:0;padding:48px 16px;background:#f6f7f9;"
		+ "color:#1c1e21}main{max-width:480px;margin:0 auto;background:#fff;border-radius:12px;padding:32px;"
		+ "box-shadow:0 1px 3px rgba(0,0,0,.12)}h1{font-size:20px;margin:0 0 12px}p,li{line-height:1.5}"
		+ "label{display:block;font-weight:600;margin:24px 0 8px}input{box-sizing:border-box;width:100%;"
		+ "padding:10px 12px;font-size:16px;border:1px solid #c9ccd1;border-radius:8px}"
		+ "button{margin:16px 8px 0 0;padding:10px 16px;font-size:16px;border:0;border-radius:8px;"
		+ "background:#2f6fec;color:#fff}button.secondary{background:#e4e6eb;color:#1c1e21}"
		+ ".error{color:#b42318}@media (prefers-color-scheme:dark){body{background:#17181a;"
		+ "color:#e7e9ec}main{background:#232528}input{background:#17181a;color:#e7e9ec;border-color:#44474c}"
		+ "button.secondary{background:#3a3d42;color:#e7e9ec}}";

	private static readonly string _styleHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Style)));

	public static async Task Write(HttpContext context, string title, string body, int status)
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

	public static string Encode(string? text)
	{
		return WebUtility.HtmlEncode(text ?? string.Empty);
	}
}
