namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Sefton Metropolitan Borough Council.
/// </summary>
internal sealed partial class SeftonMetropolitanBoroughCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Sefton Metropolitan Borough Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.sefton.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "sefton";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "Recycling",
			Colour = BinColour.Brown,
			Keys = [ "Recycling" ],
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Green,
			Keys = [ "Green" ],
		},
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Grey,
			Keys = [ "Residual" ],
		},
	];

	/// <summary>
	/// The URL of the bin collection lookup page.
	/// </summary>
	private const string _pageUrl = "https://www.sefton.gov.uk/bins-and-recycling/bins-and-recycling/when-is-my-bin-collection-day/";

	/// <summary>
	/// Regex for extracting the ASP.NET anti-forgery token from HTML.
	/// </summary>
	[GeneratedRegex(@"<input name=""__RequestVerificationToken"" type=""hidden"" value=""(?<token>[^""]+)""")]
	private static partial Regex TokenRegex();

	/// <summary>
	/// Regex for extracting the Umbraco Forms protection token from HTML.
	/// </summary>
	[GeneratedRegex(@"<input name=""ufprt"" type=""hidden"" value=""(?<ufprt>[^""]+)""")]
	private static partial Regex UfprtRegex();

	/// <summary>
	/// Regex for extracting address options from the HTML response.
	/// </summary>
	[GeneratedRegex(@"<option value=""(?<uid>[^""]+)"">(?<address>[^<]+)</option>")]
	private static partial Regex AddressRegex();

	/// <summary>
	/// Regex for extracting bin collection rows from the HTML response.
	/// </summary>
	[GeneratedRegex(@"<td><img[^>]+>\s*(?<bin>[^<\n]+?)\s*</td>\s*<td>[^<]*</td>\s*<td>(?<date>\d{2}/\d{2}/\d{4})</td>", RegexOptions.Singleline)]
	private static partial Regex BinDaysRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for initial page load
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _pageUrl,
				Method = "GET",
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Prepare client-side request for postcode search
		else if (clientSideResponse.RequestId == 1)
		{
			var setCookieHeader = clientSideResponse.Headers["set-cookie"];
			var cookie = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(setCookieHeader);

			var token = TokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;
			var ufprt = UfprtRegex().Match(clientSideResponse.Content).Groups["ufprt"].Value;

			var body = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "Postcode", postcode },
				{ "Streetname", "" },
				{ "__RequestVerificationToken", token },
				{ "ufprt", ufprt },
			});

			// The postcode POST redirects (302); FollowRedirects=false so we can capture and forward the session cookie
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = _pageUrl,
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
					{ "cookie", cookie },
				},
				Body = body,
				Options = new ClientSideOptions
				{
					FollowRedirects = false,
					Metadata =
					{
						{ "cookie", cookie },
					},
				},
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Follow 302 redirect from postcode POST, merging session cookie from redirect response
		else if (clientSideResponse.RequestId == 2)
		{
			var antiforgery = clientSideResponse.Options.Metadata["cookie"];
			clientSideResponse.Headers.TryGetValue("set-cookie", out var redirectCookieHeader);
			var redirectCookie = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(redirectCookieHeader!);
			var mergedCookie = $"{antiforgery}; {redirectCookie}";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 3,
				Url = _pageUrl,
				Method = "GET",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "cookie", mergedCookie },
				},
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Process addresses from redirect target page
		else if (clientSideResponse.RequestId == 3)
		{
			var rawAddresses = AddressRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (Match rawAddress in rawAddresses)
			{
				var uid = rawAddress.Groups["uid"].Value;

				if (string.IsNullOrWhiteSpace(uid))
				{
					continue;
				}

				var address = new Address
				{
					Property = WebUtility.HtmlDecode(rawAddress.Groups["address"].Value).Trim(),
					Postcode = postcode,
					Uid = uid,
				};

				addresses.Add(address);
			}

			var getAddressesResponse = new GetAddressesResponse
			{
				Addresses = [.. addresses],
			};

			return getAddressesResponse;
		}

		throw new InvalidOperationException("Invalid client-side request.");
	}

	/// <inheritdoc/>
	public GetBinDaysResponse GetBinDays(Address address, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for initial page load
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _pageUrl,
				Method = "GET",
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Prepare client-side request for postcode search
		else if (clientSideResponse.RequestId == 1)
		{
			var setCookieHeader = clientSideResponse.Headers["set-cookie"];
			var cookie = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(setCookieHeader);

			var token = TokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;
			var ufprt = UfprtRegex().Match(clientSideResponse.Content).Groups["ufprt"].Value;

			var body = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "Postcode", address.Postcode! },
				{ "Streetname", "" },
				{ "__RequestVerificationToken", token },
				{ "ufprt", ufprt },
			});

			// The postcode POST redirects (302); FollowRedirects=false so we can capture and forward the session cookie
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = _pageUrl,
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
					{ "cookie", cookie },
				},
				Body = body,
				Options = new ClientSideOptions
				{
					FollowRedirects = false,
					Metadata =
					{
						{ "cookie", cookie },
					},
				},
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Follow 302 redirect from postcode POST, merging session cookie from redirect response
		else if (clientSideResponse.RequestId == 2)
		{
			var antiforgery = clientSideResponse.Options.Metadata["cookie"];
			clientSideResponse.Headers.TryGetValue("set-cookie", out var redirectCookieHeader);
			var redirectCookie = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(redirectCookieHeader!);
			var mergedCookie = $"{antiforgery}; {redirectCookie}";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 3,
				Url = _pageUrl,
				Method = "GET",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "cookie", mergedCookie },
				},
				Options = new ClientSideOptions
				{
					Metadata =
					{
						{ "cookie", mergedCookie },
					},
				},
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Prepare client-side request for address selection
		else if (clientSideResponse.RequestId == 3)
		{
			var cookie = clientSideResponse.Options.Metadata["cookie"];

			var token = TokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;
			var ufprt = UfprtRegex().Match(clientSideResponse.Content).Groups["ufprt"].Value;

			var body = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "selectedValue", address.Uid! },
				{ "Action", "Select" },
				{ "__RequestVerificationToken", token },
				{ "ufprt", ufprt },
			});

			// The address selection POST also redirects (302); FollowRedirects=false to capture the updated session cookie
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 4,
				Url = _pageUrl,
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
					{ "cookie", cookie },
				},
				Body = body,
				Options = new ClientSideOptions
				{
					FollowRedirects = false,
					Metadata =
					{
						{ "cookie", cookie },
					},
				},
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Follow 302 redirect from address selection POST, merging updated session cookie
		else if (clientSideResponse.RequestId == 4)
		{
			var existingCookie = clientSideResponse.Options.Metadata["cookie"];
			clientSideResponse.Headers.TryGetValue("set-cookie", out var redirectCookieHeader);
			var redirectCookie = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(redirectCookieHeader!);
			var mergedCookie = $"{existingCookie}; {redirectCookie}";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 5,
				Url = _pageUrl,
				Method = "GET",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "cookie", mergedCookie },
				},
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Process bin days from bin collection page
		else if (clientSideResponse.RequestId == 5)
		{
			var rawBinDays = BinDaysRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each bin day, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (Match rawBinDay in rawBinDays)
			{
				var bin = rawBinDay.Groups["bin"].Value.Trim();
				var dateString = rawBinDay.Groups["date"].Value.Trim();

				var date = DateUtilities.ParseDateExact(dateString, "dd/MM/yyyy");

				var matchedBins = ProcessingUtilities.GetMatchingBins(_binTypes, bin);

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

		throw new InvalidOperationException("Invalid client-side request.");
	}
}
