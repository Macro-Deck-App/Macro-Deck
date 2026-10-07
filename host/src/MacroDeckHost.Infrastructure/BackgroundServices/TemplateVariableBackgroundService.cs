using MacroDeckHost.Application.Variables.Templates;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class TemplateVariableBackgroundService : BackgroundService
{
	private readonly TemplateVariableSynchronizer _templates;
	private readonly ILogger _logger;

	public TemplateVariableBackgroundService(TemplateVariableSynchronizer templates, ILogger logger)
	{
		_templates = templates;
		_logger = logger.ForContext<TemplateVariableBackgroundService>();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await _templates.WaitForChangesAsync(stoppingToken);
				await _templates.DrainAsync();
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to update template variables");
			}
		}
	}
}
