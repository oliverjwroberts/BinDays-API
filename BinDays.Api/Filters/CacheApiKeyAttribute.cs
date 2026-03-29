namespace BinDays.Api.Filters;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Action filter attribute that validates the <c>X-Api-Key</c> request header against the
/// <c>CacheApiKey</c> configuration value. Returns 401 Unauthorized if the key is missing or incorrect.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class CacheApiKeyAttribute : ActionFilterAttribute
{
	/// <summary>
	/// Validates the API key before the action executes.
	/// </summary>
	/// <param name="context">The action executing context.</param>
	public override void OnActionExecuting(ActionExecutingContext context)
	{
		var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
		var apiKey = config.GetValue<string>("CacheApiKey");

		if (string.IsNullOrEmpty(apiKey) || context.HttpContext.Request.Headers["X-Api-Key"] != apiKey)
		{
			context.Result = new UnauthorizedResult();
		}
	}
}
