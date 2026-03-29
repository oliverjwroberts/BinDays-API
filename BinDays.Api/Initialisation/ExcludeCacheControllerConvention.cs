namespace BinDays.Api.Initialisation;

using BinDays.Api.Controllers;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using System.Linq;

/// <summary>
/// Application model convention that removes <see cref="CacheController"/> from routing.
/// Applied at startup when Redis is not configured, so cache endpoints are not registered.
/// </summary>
internal sealed class ExcludeCacheControllerConvention : IApplicationModelConvention
{
	/// <summary>
	/// Removes <see cref="CacheController"/> from the application model.
	/// </summary>
	/// <param name="application">The application model.</param>
	public void Apply(ApplicationModel application)
	{
		application.Controllers.Remove(
			application.Controllers.First(c => c.ControllerType == typeof(CacheController))
		);
	}
}
