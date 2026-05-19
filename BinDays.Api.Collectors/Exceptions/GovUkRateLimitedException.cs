namespace BinDays.Api.Collectors.Exceptions;

using System;

/// <summary>
/// Exception thrown when gov.uk rate limits a postcode lookup request.
/// </summary>
public sealed class GovUkRateLimitedException : Exception
{
	/// <summary>
	/// The postcode that was rate-limited.
	/// </summary>
	public string Postcode { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="GovUkRateLimitedException"/> class.
	/// </summary>
	/// <param name="postcode">The postcode that was rate-limited.</param>
	public GovUkRateLimitedException(string postcode)
		: base($"Rate limited by gov.uk for postcode: {postcode}")
	{
		Postcode = postcode;
	}
}
