using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Widgets;

internal sealed class WidgetCaptionTemplate : IDisposable
{
	private readonly string _template;
	private readonly string? _scopeRefId;
	private readonly IReadOnlySet<string> _names;
	private readonly IVariableChangeNotifier _notifier;
	private readonly VariableTemplateRenderer _renderer;
	private readonly UiState<string> _text;
	private readonly Lock _sync = new();
	private bool _disposed;

	private WidgetCaptionTemplate(
		string template,
		VariableRegistry variables,
		IVariableChangeNotifier notifier,
		string? scopeRefId)
	{
		_template = template;
		_scopeRefId = scopeRefId;
		_names = WidgetVariableReferenceParser.ReferencedNames(template);
		_notifier = notifier;
		_renderer = new VariableTemplateRenderer(variables);
		_text = new UiState<string>(string.Empty);

		// Subscribing before the first render keeps a change published in between from being lost.
		_notifier.Changed += OnVariableChanged;
		Refresh();
	}

	public UiText Text => UiText.From(() => _text.Value);

	public static WidgetCaptionTemplate? Create(
		string? template,
		VariableRegistry variables,
		IVariableChangeNotifier notifier,
		string? scopeRefId)
	{
		ArgumentNullException.ThrowIfNull(variables);
		ArgumentNullException.ThrowIfNull(notifier);

		return VariableTemplateRenderer.ContainsLiquid(template)
			? new WidgetCaptionTemplate(template!, variables, notifier, scopeRefId)
			: null;
	}

	public void Dispose()
	{
		_notifier.Changed -= OnVariableChanged;

		lock (_sync)
		{
			_disposed = true;
		}
	}

	private void OnVariableChanged(object? sender, VariableChangedEventArgs args)
	{
		if (_names.Contains(args.Name))
		{
			Refresh();
		}
	}

	private void Refresh()
	{
		// Changes are published on whichever thread made them: rendering and writing under one lock keeps
		// an older render from overwriting a newer one.
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			_text.Value = Render();
		}
	}

	private string Render()
	{
		try
		{
			return _renderer.Render(_template,
				_scopeRefId is null ? VariableScope.Global : VariableScope.Widget,
				_scopeRefId);
		}
		catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
		{
			// User written text rendered on a variable change: a broken template shows itself instead of
			// faulting the session.
			return _template;
		}
	}
}
