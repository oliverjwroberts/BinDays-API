namespace BinDays.Api.Collectors.Collectors.Vendors;

using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Base collector implementation for councils using the Placecube digital place platform.
/// </summary>
internal abstract partial class PlacecubeCollectorBase : GovUkCollectorBase
{
	/// <summary>
	/// The base URL of the council's website (no trailing slash).
	/// </summary>
	protected abstract string BaseUrl { get; }

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	protected abstract IReadOnlyCollection<Bin> BinTypes { get; }

	/// <summary>
	/// Regex to extract the Liferay CSRF auth token.
	/// </summary>
	[GeneratedRegex(@"Liferay\.authToken\s*=\s*'(?<token>[^']+)'")]
	private static partial Regex TokenRegex();

	/// <summary>
	/// Regex to extract the Liferay form date required for portlet POST validations.
	/// </summary>
	[GeneratedRegex(@"id=""[^""]*formDate""[^>]*value=""(?<formDate>\d+)""")]
	private static partial Regex FormDateRegex();

	/// <summary>
	/// Regex to extract the Liferay company ID.
	/// </summary>
	[GeneratedRegex(@"getCompanyId:\s*function\(\)\s*\{\s*return\s*'(?<companyId>\d+)';\s*\}")]
	private static partial Regex CompanyIdRegex();

	/// <summary>
	/// Regex to extract bin days from the resulting HTML table.
	/// </summary>
	[GeneratedRegex(@"<td>(?<service>[^<]+)<\/td>\s*<td>\s*(?<date>[a-zA-Z]+\s+\d+\s+[a-zA-Z]+\s+\d+)\s*<\/td>")]
	private static partial Regex BinDaysRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare initial client-side request to load Liferay tokens and session cookies
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = $"{BaseUrl}/check-your-collection-day",
				Method = "GET",
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Process initial page, extract security tokens, and request addresses
		else if (clientSideResponse.RequestId == 1)
		{
			var token = TokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;
			var companyId = CompanyIdRegex().Match(clientSideResponse.Content).Groups["companyId"].Value;

			var setCookieHeader = clientSideResponse.Headers["set-cookie"];
			var cookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(setCookieHeader);

			var requestBody = $$"""
			{
				"/placecube_digitalplace.addresscontext/search-address-by-postcode": {
					"companyId": "{{companyId}}",
					"postcode": "{{postcode}}",
					"fallbackToNationalLookup": false
				}
			}
			""";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = $"{BaseUrl}/api/jsonws/invoke",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "accept", "*/*" },
					{ "accept-language", "en-GB,en;q=0.9" },
					{ "origin", BaseUrl },
					{ "referer", $"{BaseUrl}/check-your-collection-day" },
					{ "x-csrf-token", token },
					{ "content-type", Constants.ApplicationJson },
					{ "cookie", cookies },
					{ "sec-fetch-dest", "empty" },
					{ "sec-fetch-mode", "cors" },
					{ "sec-fetch-site", "same-origin" },
				},
				Body = requestBody,
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Parse JSON response into addresses
		else if (clientSideResponse.RequestId == 2)
		{
			using var doc = JsonDocument.Parse(clientSideResponse.Content);
			var root = doc.RootElement;

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (var element in root.EnumerateArray())
			{
				var uprn = element.GetProperty("UPRN").GetString()!;
				var fullAddress = element.GetProperty("fullAddress").GetString()!;

				var address = new Address
				{
					Property = fullAddress,
					Postcode = postcode,
					Uid = uprn,
				};

				addresses.Add(address);
			}

			var getAddressesResponse = new GetAddressesResponse
			{
				Addresses = [.. addresses],
			};

			return getAddressesResponse;
		}

		// Throw exception for invalid request
		throw new InvalidOperationException("Invalid client-side request.");
	}

	/// <inheritdoc/>
	public GetBinDaysResponse GetBinDays(Address address, ClientSideResponse? clientSideResponse)
	{
		// Prepare initial client-side request to load Liferay tokens and session cookies
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = $"{BaseUrl}/check-your-collection-day",
				Method = "GET",
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Process initial page, format multipart body, and post to Liferay portlet
		else if (clientSideResponse.RequestId == 1)
		{
			var token = TokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;
			var formDate = FormDateRegex().Match(clientSideResponse.Content).Groups["formDate"].Value;

			var setCookieHeader = clientSideResponse.Headers["set-cookie"];
			var cookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(setCookieHeader);

			var uprn = address.Uid!;
			var fullAddress = address.Property!;

			var boundary = "----WebKitFormBoundary7MA4YWxkTrZu0gW";
			var portletPrefix = "_com_placecube_digitalplace_local_waste_portlet_CollectionDayFinderPortlet_";

			var requestBody = ProcessingUtilities.BuildMultipartFormData(boundary, new()
			{
				{ $"{portletPrefix}formDate", formDate },
				{ $"{portletPrefix}postcode", address.Postcode! },
				{ $"{portletPrefix}uprn", uprn },
				{ $"{portletPrefix}fullAddress", fullAddress },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = $"{BaseUrl}/check-your-collection-day?p_p_id=com_placecube_digitalplace_local_waste_portlet_CollectionDayFinderPortlet&p_p_lifecycle=0&p_p_state=normal&p_p_mode=view&_com_placecube_digitalplace_local_waste_portlet_CollectionDayFinderPortlet_mvcRenderCommandName=%2Fcollection_day_finder%2Fget_days",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "accept", "*/*" },
					{ "accept-language", "en-GB,en;q=0.9" },
					{ "origin", BaseUrl },
					{ "referer", $"{BaseUrl}/check-your-collection-day" },
					{ "x-csrf-token", token },
					{ "x-pjax", "true" },
					{ "x-requested-with", Constants.XmlHttpRequest },
					{ "content-type", $"multipart/form-data; boundary={boundary}" },
					{ "cookie", cookies },
					{ "sec-fetch-dest", "empty" },
					{ "sec-fetch-mode", "cors" },
					{ "sec-fetch-site", "same-origin" },
				},
				Body = requestBody,
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Parse resulting HTML portlet output to extract bin days
		else if (clientSideResponse.RequestId == 2)
		{
			var rawBinDays = BinDaysRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each bin day, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (Match rawBinDay in rawBinDays)
			{
				var service = rawBinDay.Groups["service"].Value.Trim();
				var dateString = rawBinDay.Groups["date"].Value.Trim();

				var date = DateUtilities.ParseDateExact(dateString, "dddd dd MMM yyyy");

				var matchedBins = ProcessingUtilities.GetMatchingBins(BinTypes, service);

				var binDay = new BinDay
				{
					Date = date,
					Address = address,
					Bins = matchedBins,
				};

				binDays.Add(binDay);
			}

			var getBinDaysResponse = new GetBinDaysResponse
			{
				BinDays = ProcessingUtilities.ProcessBinDays(binDays),
			};

			return getBinDaysResponse;
		}

		// Throw exception for invalid request
		throw new InvalidOperationException("Invalid client-side request.");
	}
}
