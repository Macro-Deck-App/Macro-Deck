using MacroDeck.Localization;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Localization;
using MacroDeckHost.Mcp;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

public record McpTokenBody(
	Guid Id,
	string Name,
	DateTime CreatedAt,
	DateTime? LastUsedAt,
	DateTime? RevokedAt,
	int ConnectionCount);

public record GetMcpTokensResponse(IReadOnlyList<McpTokenBody> Tokens);

public record CreateMcpTokenBody(string Name);

public record CreateMcpTokenResponse(bool Success, TransportError? Error, McpTokenBody? Token, string? Plaintext);

public record McpTokenActionResponse(bool Success, TransportError? Error);

public record McpConnectionBody(
	Guid TokenId,
	string? TokenName,
	string? ClientName,
	string? ClientVersion,
	DateTime ConnectedAt,
	DateTime LastSeenAt);

public record GetMcpConnectionsResponse(IReadOnlyList<McpConnectionBody> Connections);

public record GetMcpEndpointResponse(bool Available, string? Url, string ContractVersion, bool ExtensionAvailable);

public record DownloadClaudeDesktopExtensionBody(string? Token);

public record McpProviderPolicyBody(
	string IntegrationId,
	LocalizedText Name,
	McpRiskMode DefaultMode,
	McpRiskMode? Override,
	IReadOnlyList<LocalizedText> ConfirmedActions);

public record GetMcpPolicyResponse(IReadOnlyList<McpProviderPolicyBody> Providers);

public record SetMcpPolicyBody(string IntegrationId, McpRiskMode? Mode);

[ApiController]
[Route("api/mcp")]
public class McpController : ControllerBase
{
	private readonly IMcpTokenService _tokenService;
	private readonly IMcpConnections _connections;
	private readonly IMcpRiskPolicy _policy;
	private readonly IIntegrationRegistry _integrations;
	private readonly McpTextResolver _text;

	public McpController(
		IMcpTokenService tokenService,
		IMcpConnections connections,
		IMcpRiskPolicy policy,
		IIntegrationRegistry integrations,
		McpTextResolver text)
	{
		_tokenService = tokenService;
		_connections = connections;
		_policy = policy;
		_integrations = integrations;
		_text = text;
	}

	[HttpGet("policy")]
	public async Task<GetMcpPolicyResponse> GetPolicy()
	{
		var names = _integrations.GetActions(false)
			.GroupBy(action => action.OwnerId, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.First().Owner.Name, StringComparer.Ordinal);
		return new GetMcpPolicyResponse((await _policy.Providers())
			.Select(provider => new McpProviderPolicyBody(provider.IntegrationId,
				names.TryGetValue(provider.IntegrationId, out var name) ? name : provider.IntegrationId,
				provider.DefaultMode,
				provider.Override,
				provider.ConfirmedActions
					.Select(actionId => _integrations.FindAction(provider.IntegrationId, actionId)?.Name ?? actionId)
					.ToList()))
			.ToList());
	}

	[HttpPut("policy")]
	public async Task<McpTokenActionResponse> SetPolicy(SetMcpPolicyBody body)
	{
		if (string.IsNullOrWhiteSpace(body.IntegrationId))
		{
			return Failure("invalid_provider", AppStrings.Errors.Mcp.PolicyProviderInvalid());
		}

		await _policy.SetOverride(body.IntegrationId, body.Mode);
		return new McpTokenActionResponse(true, null);
	}

	[HttpGet("endpoint")]
	public GetMcpEndpointResponse Endpoint()
	{
		return new GetMcpEndpointResponse(true,
			$"http://127.0.0.1:{HostEndpoints.PublicPort}{McpPaths.Endpoint}",
			McpContract.Version,
			ClaudeDesktopExtension.IsAvailable());
	}

	[HttpPost("tokens/{id:guid}/claude-desktop-extension")]
	public async Task<IActionResult> DownloadClaudeDesktopExtension(Guid id, DownloadClaudeDesktopExtensionBody body)
	{
		var executable = ClaudeDesktopExtension.ShellExecutable();
		var platform = ClaudeDesktopExtension.CurrentPlatform();
		if (executable is null || platform is null)
		{
			return Conflict(Failure("extension_unavailable", AppStrings.Errors.Mcp.ExtensionUnavailable()));
		}

		var token = string.IsNullOrEmpty(body.Token) ? null : await _tokenService.Find(body.Token);
		if (token is null || token.Id != id)
		{
			return NotFound(Failure("invalid_token", AppStrings.Errors.Mcp.ExtensionTokenInvalid()));
		}

		if (token.RevokedAt is not null)
		{
			return Conflict(Failure("token_revoked", AppStrings.Errors.Mcp.ExtensionTokenInvalid()));
		}

		var description = await _text.Resolve(AppStrings.Mcp.Extension.Description()) ?? string.Empty;
		var archive = ClaudeDesktopExtension.Build(new ClaudeDesktopExtensionContent(executable,
			platform,
			body.Token!,
			ClaudeDesktopExtension.VersionOf(typeof(McpController).Assembly.GetName().Version),
			description));
		Response.Headers.CacheControl = "no-store";
		return File(archive, "application/zip", ClaudeDesktopExtension.FileName);
	}

	[HttpGet("tokens")]
	public async Task<GetMcpTokensResponse> GetTokens()
	{
		var connections = _connections.Snapshot();
		var tokens = await _tokenService.GetAll();

		return new GetMcpTokensResponse(tokens
			.Select(token => ToBody(token, connections.Count(connection => connection.TokenId == token.Id)))
			.ToList());
	}

	[HttpPost("tokens")]
	public async Task<IActionResult> CreateToken(CreateMcpTokenBody body)
	{
		var name = body.Name?.Trim() ?? string.Empty;
		if (name.Length is 0 or > McpTokenService.MaxNameLength)
		{
			return Ok(new CreateMcpTokenResponse(false,
				new TransportError { Code = "invalid_name", Message = AppStrings.Errors.Mcp.TokenNameInvalid() },
				null,
				null));
		}

		var created = await _tokenService.Create(name);

		return StatusCode(StatusCodes.Status201Created,
			new CreateMcpTokenResponse(true, null, ToBody(created.Token, 0), created.PlaintextToken));
	}

	[HttpPost("tokens/{id:guid}/revoke")]
	public async Task<McpTokenActionResponse> RevokeToken(Guid id)
	{
		return await _tokenService.Revoke(id) switch
		{
			McpAccessTokenRevokeResult.NotFound => Failure("not_found", AppStrings.Errors.Mcp.TokenNotFound()),
			McpAccessTokenRevokeResult.AlreadyRevoked => Failure("already_revoked",
				AppStrings.Errors.Mcp.TokenAlreadyRevoked()),
			_ => new McpTokenActionResponse(true, null)
		};
	}

	[HttpDelete("tokens/{id:guid}")]
	public async Task<McpTokenActionResponse> DeleteToken(Guid id)
	{
		return await _tokenService.Delete(id) switch
		{
			McpAccessTokenDeleteResult.NotFound => Failure("not_found", AppStrings.Errors.Mcp.TokenNotFound()),
			McpAccessTokenDeleteResult.NotRevoked => Failure("not_revoked",
				AppStrings.Errors.Plugins.DeveloperTokenNotRevoked()),
			_ => new McpTokenActionResponse(true, null)
		};
	}

	[HttpGet("connections")]
	public async Task<GetMcpConnectionsResponse> GetConnections()
	{
		var names = (await _tokenService.GetAll()).ToDictionary(token => token.Id, token => token.Name);

		return new GetMcpConnectionsResponse(_connections.Snapshot()
			.Select(connection => new McpConnectionBody(connection.TokenId,
				names.GetValueOrDefault(connection.TokenId),
				connection.ClientName,
				connection.ClientVersion,
				connection.ConnectedAt,
				connection.LastSeenAt))
			.ToList());
	}

	private static McpTokenActionResponse Failure(string code, LocalizedString message)
	{
		return new McpTokenActionResponse(false, new TransportError { Code = code, Message = message });
	}

	private static McpTokenBody ToBody(McpAccessToken token, int connectionCount)
	{
		return new McpTokenBody(token.Id,
			token.Name,
			token.CreatedAt,
			token.LastUsedAt,
			token.RevokedAt,
			connectionCount);
	}
}
