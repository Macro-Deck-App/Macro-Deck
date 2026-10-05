using MacroDeckHost.Application.Feedback;
using MacroDeckHost.Application.Ui.Transport.Messages.Feedback;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/github-star-prompt")]
public class GitHubStarPromptController : ControllerBase
{
	private readonly IGitHubStarPromptService _prompt;

	public GitHubStarPromptController(IGitHubStarPromptService prompt)
	{
		_prompt = prompt;
	}

	[HttpGet]
	public Task<GetGitHubStarPromptResponse> Get() => _prompt.GetPrompt();

	[HttpPost("shown")]
	public async Task<IActionResult> MarkShown(CancellationToken ct)
	{
		await _prompt.MarkShown(ct);
		return NoContent();
	}
}
