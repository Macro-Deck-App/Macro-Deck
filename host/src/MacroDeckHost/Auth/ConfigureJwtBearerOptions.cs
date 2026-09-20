using MacroDeckHost.Application.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MacroDeckHost.Auth;

public class ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
	private readonly ISigningKeyProvider _signingKeyProvider;

	public ConfigureJwtBearerOptions(ISigningKeyProvider signingKeyProvider)
	{
		_signingKeyProvider = signingKeyProvider;
	}

	public void Configure(string? name, JwtBearerOptions options)
	{
		if (name != JwtBearerDefaults.AuthenticationScheme)
		{
			return;
		}

		Configure(options);
	}

	public void Configure(JwtBearerOptions options)
	{
		options.MapInboundClaims = false;
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidIssuer = AuthDefaults.Issuer,
			ValidAudience = AuthDefaults.Audience,
			IssuerSigningKeyResolver = (_, _, _, _) =>
				[new SymmetricSecurityKey(_signingKeyProvider.GetKey())],
			NameClaimType = JwtRegisteredClaimNames.Name,
			ClockSkew = TimeSpan.FromSeconds(30)
		};
		options.Events = new JwtBearerEvents
		{
			OnTokenValidated = context =>
			{
				if (context.Principal is not { } principal)
				{
					return Task.CompletedTask;
				}

				var services = context.HttpContext.RequestServices;
				if (services.GetService<AccessTokenCutoff>()?.Rejects(principal) == true)
				{
					context.Fail("The access token was issued before the password was reset.");
				}
				else if (services.GetService<DeviceSessionGuard>()?.Rejects(principal) == true)
				{
					// Checked on every request, not only on the socket: a client-scope token lives long
					// enough that waiting for it to expire would leave a signed-out device working for weeks.
					context.Fail("The device this access token names was signed out or removed.");
				}

				return Task.CompletedTask;
			},
			OnChallenge = context =>
			{
				context.Response.Headers.CacheControl = "no-store";
				return Task.CompletedTask;
			},
			OnForbidden = context =>
			{
				context.Response.Headers.CacheControl = "no-store";
				return Task.CompletedTask;
			},
			OnMessageReceived = context =>
			{
				if (!string.IsNullOrEmpty(context.Request.Headers.Authorization))
				{
					return Task.CompletedTask;
				}

				// Media fallback for <img>/font URLs that cannot carry headers. The cookie
				// only ever authenticates safe methods, so it cannot be abused for CSRF.
				if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
				{
					var cookieToken = AuthCookies.ReadAccessToken(context.HttpContext);
					if (!string.IsNullOrEmpty(cookieToken))
					{
						context.Token = cookieToken;
					}
				}

				return Task.CompletedTask;
			}
		};
	}
}
