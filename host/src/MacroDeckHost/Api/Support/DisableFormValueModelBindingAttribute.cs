using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MacroDeckHost.Api.Support;

[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
	public void OnResourceExecuting(ResourceExecutingContext context)
	{
		var factories = context.ValueProviderFactories;
		for (var i = factories.Count - 1; i >= 0; i--)
		{
			if (factories[i] is FormValueProviderFactory
				or FormFileValueProviderFactory
				or JQueryFormValueProviderFactory)
			{
				factories.RemoveAt(i);
			}
		}
	}

	public void OnResourceExecuted(ResourceExecutedContext context)
	{
	}
}
