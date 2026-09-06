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
