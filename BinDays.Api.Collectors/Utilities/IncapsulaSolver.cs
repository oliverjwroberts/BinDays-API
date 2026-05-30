namespace BinDays.Api.Collectors.Utilities;

using BinDays.Api.Collectors.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Utility for solving Imperva/Incapsula bot protection cookie challenges without browser emulation.
///
/// Incapsula challenges work by returning a page containing a hex-encoded JavaScript snippet.
/// That snippet reads the server-issued visid_incap_* cookie, computes a ___utmvc cookie value
/// (typically via btoa()), sets both cookies, then reloads the page.
///
/// This solver:
///   1. Detects challenge pages by inspecting the response body and Set-Cookie headers.
///   2. Extracts the server-issued visid_incap_* and incap_ses_* cookies.
///   3. Decodes the hex-encoded challenge script and identifies the ___utmvc computation pattern.
///   4. Computes ___utmvc using known values (user-agent string, extracted cookie values).
///   5. Returns a retry ClientSideRequest with the full Incapsula cookie set, storing the cookies
///      in Options.Metadata so they can be forwarded to subsequent requests in the same flow.
/// </summary>
internal static partial class IncapsulaSolver
{
	/// <summary>
	/// The metadata key used to carry Incapsula cookies across request/response steps.
	/// </summary>
	internal const string MetadataKey = "incapsula_cookie";

	/// <summary>
	/// Regex to detect the Incapsula resource reference that appears in challenge pages.
	/// </summary>
	[GeneratedRegex(@"/_Incapsula_Resource")]
	private static partial Regex IncapsulaResourceRegex();

	/// <summary>
	/// Regex to extract the hex-encoded challenge script from the page HTML.
	/// </summary>
	[GeneratedRegex(@"var\s+Z\s*=\s*""(?<hex>[0-9a-fA-F]+)""")]
	private static partial Regex ChallengeHexRegex();

	/// <summary>
	/// Regex to extract visid_incap_* name/value pairs from a combined Set-Cookie header.
	/// </summary>
	[GeneratedRegex(@"(?<name>visid_incap_\d+)=(?<value>[^\s;,]+)")]
	private static partial Regex VisidIncapRegex();

	/// <summary>
	/// Regex to extract incap_ses_* name/value pairs from a combined Set-Cookie header.
	/// </summary>
	[GeneratedRegex(@"(?<name>incap_ses_[^\s=;,]+)=(?<value>[^\s;,]+)")]
	private static partial Regex IncapSesRegex();

	/// <summary>
	/// Regex to detect the btoa(visidCookie + navigator.userAgent) computation pattern.
	/// </summary>
	[GeneratedRegex(@"btoa\(\s*c\[1\]\s*\+\s*navigator\.userAgent\s*\)")]
	private static partial Regex BtoaVisidUserAgentRegex();

	/// <summary>
	/// Regex to detect the btoa(navigator.userAgent) computation pattern.
	/// </summary>
	[GeneratedRegex(@"btoa\(\s*navigator\.userAgent\s*\)")]
	private static partial Regex BtoaUserAgentRegex();

	/// <summary>
	/// Regex to detect the btoa(document.cookie) computation pattern.
	/// </summary>
	[GeneratedRegex(@"btoa\(\s*document\.cookie\s*\)")]
	private static partial Regex BtoaDocumentCookieRegex();

	/// <summary>
	/// Returns true if the response is an Imperva/Incapsula bot protection challenge page.
	/// </summary>
	public static bool IsChallenge(ClientSideResponse response)
	{
		if (IncapsulaResourceRegex().IsMatch(response.Content))
		{
			return true;
		}

		if (response.Headers.TryGetValue("set-cookie", out var setCookieHeader) &&
			setCookieHeader.Contains("visid_incap_"))
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// Builds a bypass request for the given original request using cookies extracted from the
	/// Incapsula challenge response. The bypass request reuses the same RequestId, URL, method,
	/// and body as the original so that when the response comes back it is handled by the same
	/// collector step. The solved Incapsula cookies are also stored in Options.Metadata under
	/// <see cref="MetadataKey"/> so they can be forwarded to subsequent requests.
	/// </summary>
	public static ClientSideRequest BuildBypassRequest(
		ClientSideRequest originalRequest,
		ClientSideResponse challengeResponse)
	{
		var challengeCookies = ExtractChallengeCookies(challengeResponse);
		var utmvcValue = ComputeUtmvcValue(challengeResponse.Content, challengeCookies);

		if (!string.IsNullOrEmpty(utmvcValue))
		{
			challengeCookies["___utmvc"] = utmvcValue;
		}

		var incapsulaCookieHeader = string.Join("; ", challengeCookies.Select(kvp => $"{kvp.Key}={kvp.Value}"));

		var headers = new Dictionary<string, string>(originalRequest.Headers);

		if (headers.TryGetValue("cookie", out var existingCookie) && !string.IsNullOrEmpty(existingCookie))
		{
			headers["cookie"] = $"{incapsulaCookieHeader}; {existingCookie}";
		}
		else
		{
			headers["cookie"] = incapsulaCookieHeader;
		}

		return new ClientSideRequest
		{
			RequestId = originalRequest.RequestId,
			Url = originalRequest.Url,
			Method = originalRequest.Method,
			Headers = headers,
			Body = originalRequest.Body,
			Options = new ClientSideOptions
			{
				FollowRedirects = originalRequest.Options.FollowRedirects,
				Metadata = { [MetadataKey] = incapsulaCookieHeader },
			},
		};
	}

	/// <summary>
	/// Returns the Incapsula cookie string stored in the response metadata, or null if no bypass
	/// was performed for this response's request.
	/// </summary>
	public static string? GetStoredCookie(ClientSideResponse response)
	{
		return response.Options.Metadata.GetValueOrDefault(MetadataKey);
	}

	/// <summary>
	/// Extracts visid_incap_* and incap_ses_* cookies from the challenge response's Set-Cookie header.
	/// </summary>
	private static Dictionary<string, string> ExtractChallengeCookies(ClientSideResponse response)
	{
		var cookies = new Dictionary<string, string>();

		if (!response.Headers.TryGetValue("set-cookie", out var setCookieHeader))
		{
			return cookies;
		}

		foreach (Match match in VisidIncapRegex().Matches(setCookieHeader)!)
		{
			cookies[match.Groups["name"].Value] = match.Groups["value"].Value;
		}

		foreach (Match match in IncapSesRegex().Matches(setCookieHeader)!)
		{
			cookies[match.Groups["name"].Value] = match.Groups["value"].Value;
		}

		return cookies;
	}

	/// <summary>
	/// Attempts to compute the ___utmvc cookie value by decoding and analysing the Incapsula
	/// challenge script. Returns an empty string if the computation pattern is not recognised.
	///
	/// Known patterns matched (in priority order):
	///   btoa(visid_value + navigator.userAgent)  — most common
	///   btoa(navigator.userAgent)                — simpler variant
	///   btoa(document.cookie)                    — encodes all cookies
	/// </summary>
	private static string ComputeUtmvcValue(
		string challengeHtml,
		Dictionary<string, string> challengeCookies)
	{
		var hexMatch = ChallengeHexRegex().Match(challengeHtml);
		if (!hexMatch.Success)
		{
			return string.Empty;
		}

		var decoded = DecodeHexToString(hexMatch.Groups["hex"].Value);

		var visidValue = challengeCookies
			.FirstOrDefault(kvp => kvp.Key.StartsWith("visid_incap_")).Value ?? string.Empty;

		if (BtoaVisidUserAgentRegex().IsMatch(decoded))
		{
			return Base64Encode(visidValue + Constants.UserAgent);
		}

		if (BtoaUserAgentRegex().IsMatch(decoded))
		{
			return Base64Encode(Constants.UserAgent);
		}

		if (BtoaDocumentCookieRegex().IsMatch(decoded))
		{
			var cookieString = string.Join("; ", challengeCookies.Select(kvp => $"{kvp.Key}={kvp.Value}"));
			return Base64Encode(cookieString);
		}

		return string.Empty;
	}

	/// <summary>
	/// Decodes a hex string (two hex digits per byte) to a UTF-8 string.
	/// Returns an empty string if the hex string has an odd length.
	/// </summary>
	private static string DecodeHexToString(string hex)
	{
		if (hex.Length % 2 != 0)
		{
			return string.Empty;
		}

		var bytes = new byte[hex.Length / 2];
		for (var i = 0; i < bytes.Length; i++)
		{
			bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
		}

		return Encoding.UTF8.GetString(bytes);
	}

	/// <summary>
	/// Base64-encodes a string using UTF-8 byte representation, matching JavaScript's btoa()
	/// behaviour for strings that contain only ASCII or Latin-1 characters.
	/// </summary>
	private static string Base64Encode(string value)
	{
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
	}
}
