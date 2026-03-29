namespace BinDays.Api.Controllers;

using BinDays.Api.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Linq;

/// <summary>
/// API controller for managing the cache.
/// </summary>
[ApiController]
[CacheApiKey]
public class CacheController : ControllerBase
{
	/// <summary>
	/// Distributed cache for storing responses.
	/// </summary>
	private readonly IDistributedCache _cache;

	/// <summary>
	/// Redis connection multiplexer for key scanning.
	/// </summary>
	private readonly IConnectionMultiplexer _multiplexer;

	/// <summary>
	/// Logger for the controller.
	/// </summary>
	private readonly ILogger<CacheController> _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="CacheController"/> class.
	/// </summary>
	/// <param name="cache">Distributed cache for storing responses.</param>
	/// <param name="multiplexer">Redis connection multiplexer for key scanning.</param>
	/// <param name="logger">Logger for the controller.</param>
	public CacheController(IDistributedCache cache, IConnectionMultiplexer multiplexer, ILogger<CacheController> logger)
	{
		_cache = cache;
		_multiplexer = multiplexer;
		_logger = logger;
	}

	/// <summary>
	/// Formats a postcode string for use in a cache key by converting it to uppercase and removing spaces.
	/// </summary>
	/// <param name="postcode">The postcode string to format.</param>
	/// <returns>The formatted postcode string for cache key usage.</returns>
	private static string FormatPostcodeForCacheKey(string postcode)
	{
		return postcode.ToUpperInvariant().Replace(" ", string.Empty);
	}

	/// <summary>
	/// Removes all cache keys matching the given Redis glob pattern.
	/// </summary>
	/// <param name="pattern">The Redis glob pattern to match keys against.</param>
	private void RemoveByPattern(string pattern)
	{
		var server = _multiplexer.GetServers().First();
		foreach (var key in server.Keys(pattern: pattern))
		{
			_cache.Remove((string)key!);
		}
	}

	/// <summary>
	/// Gets the collector cache entry for the given postcode.
	/// </summary>
	/// <param name="postcode">The postcode to retrieve the collector cache entry for.</param>
	/// <returns>The cached JSON value, or 404 if not found.</returns>
	[HttpGet]
	[Route("/cache/collectors/{postcode}")]
	public IActionResult GetCollector(string postcode)
	{
		var formattedPostcode = FormatPostcodeForCacheKey(postcode);
		var cachedValue = _cache.GetString($"collector-{formattedPostcode}");

		if (cachedValue == null)
		{
			return NotFound();
		}

		return Content(cachedValue, "application/json");
	}

	/// <summary>
	/// Gets the address cache entry for the given gov.uk ID and postcode.
	/// </summary>
	/// <param name="govUkId">The gov.uk identifier for the collector.</param>
	/// <param name="postcode">The postcode to retrieve the address cache entry for.</param>
	/// <returns>The cached JSON value, or 404 if not found.</returns>
	[HttpGet]
	[Route("/cache/addresses/{govUkId}/{postcode}")]
	public IActionResult GetAddresses(string govUkId, string postcode)
	{
		var formattedPostcode = FormatPostcodeForCacheKey(postcode);
		var cachedValue = _cache.GetString($"addresses-{govUkId}-{formattedPostcode}");

		if (cachedValue == null)
		{
			return NotFound();
		}

		return Content(cachedValue, "application/json");
	}

	/// <summary>
	/// Gets the bin day cache entry for the given gov.uk ID, postcode, and address UID.
	/// </summary>
	/// <param name="govUkId">The gov.uk identifier for the collector.</param>
	/// <param name="postcode">The postcode of the address.</param>
	/// <param name="uid">The unique identifier of the address.</param>
	/// <returns>The cached JSON value, or 404 if not found.</returns>
	[HttpGet]
	[Route("/cache/bin-days/{govUkId}/{postcode}/{uid}")]
	public IActionResult GetBinDays(string govUkId, string postcode, string uid)
	{
		var formattedPostcode = FormatPostcodeForCacheKey(postcode);
		var cachedValue = _cache.GetString($"bin-days-{govUkId}-{formattedPostcode}-{uid}");

		if (cachedValue == null)
		{
			return NotFound();
		}

		return Content(cachedValue, "application/json");
	}

	/// <summary>
	/// Clears all cache entries created by this application.
	/// </summary>
	/// <returns>No content.</returns>
	[HttpDelete]
	[Route("/cache")]
	public IActionResult ClearAll()
	{
		RemoveByPattern("collector-*");
		RemoveByPattern("addresses-*");
		RemoveByPattern("bin-days-*");
		_logger.LogInformation("Cleared all cache entries.");
		return NoContent();
	}

	/// <summary>
	/// Clears collector cache entries. Clears all collector entries when no postcode is provided,
	/// otherwise clears the entry for the given postcode.
	/// </summary>
	/// <param name="postcode">Optional postcode to limit clearing to a specific entry.</param>
	/// <returns>No content.</returns>
	[HttpDelete]
	[Route("/cache/collectors")]
	public IActionResult ClearCollectors(string? postcode = null)
	{
		if (postcode != null)
		{
			var formattedPostcode = FormatPostcodeForCacheKey(postcode);
			_cache.Remove($"collector-{formattedPostcode}");
		}
		else
		{
			RemoveByPattern("collector-*");
		}

		_logger.LogInformation(
			"Cleared collector cache entries for postcode: {Postcode}.",
			postcode ?? "*"
		);

		return NoContent();
	}

	/// <summary>
	/// Clears address cache entries. Any combination of parameters may be provided to narrow the scope;
	/// omitting all parameters clears all address entries.
	/// </summary>
	/// <param name="govUkId">Optional gov.uk identifier to filter by.</param>
	/// <param name="postcode">Optional postcode to filter by.</param>
	/// <returns>No content.</returns>
	[HttpDelete]
	[Route("/cache/addresses")]
	public IActionResult ClearAddresses(string? govUkId = null, string? postcode = null)
	{
		var formattedPostcode = postcode != null ? FormatPostcodeForCacheKey(postcode) : null;

		if (govUkId != null && formattedPostcode != null)
		{
			_cache.Remove($"addresses-{govUkId}-{formattedPostcode}");
		}
		else
		{
			RemoveByPattern($"addresses-{govUkId ?? "*"}-{formattedPostcode ?? "*"}");
		}

		_logger.LogInformation(
			"Cleared address cache entries for gov.uk ID: {GovUkId}, postcode: {Postcode}.",
			govUkId ?? "*",
			postcode ?? "*"
		);

		return NoContent();
	}

	/// <summary>
	/// Clears bin day cache entries. Any combination of parameters may be provided to narrow the scope;
	/// omitting all parameters clears all bin day entries.
	/// </summary>
	/// <param name="govUkId">Optional gov.uk identifier to filter by.</param>
	/// <param name="postcode">Optional postcode to filter by.</param>
	/// <param name="uid">Optional address UID to filter by.</param>
	/// <returns>No content.</returns>
	[HttpDelete]
	[Route("/cache/bin-days")]
	public IActionResult ClearBinDays(string? govUkId = null, string? postcode = null, string? uid = null)
	{
		var formattedPostcode = postcode != null ? FormatPostcodeForCacheKey(postcode) : null;

		if (govUkId != null && formattedPostcode != null && uid != null)
		{
			_cache.Remove($"bin-days-{govUkId}-{formattedPostcode}-{uid}");
		}
		else
		{
			RemoveByPattern($"bin-days-{govUkId ?? "*"}-{formattedPostcode ?? "*"}-{uid ?? "*"}");
		}

		_logger.LogInformation(
			"Cleared bin day cache entries for gov.uk ID: {GovUkId}, postcode: {Postcode}, UID: {Uid}.",
			govUkId ?? "*",
			postcode ?? "*",
			uid ?? "*"
		);

		return NoContent();
	}
}
