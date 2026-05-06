namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Aberdeenshire Council.
/// </summary>
internal sealed partial class AberdeenshireCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Aberdeenshire Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.aberdeenshire.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "aberdeenshire";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Black,
			Keys = [ "Refuse" ],
		},
		new()
		{
			Name = "Paper and Card Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Blue lid bin recycling" ],
		},
		new()
		{
			Name = "Plastic and Metal Recycling",
			Colour = BinColour.Orange,
			Keys = [ "Orange lid bin recycling" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Grey,
			Keys = [ "food waste" ],
			Type = BinType.Caddy,
		},
	];

	/// <summary>
	/// Regex for the request verification token.
	/// </summary>
	[GeneratedRegex(@"<input name=""__RequestVerificationToken"" type=""hidden"" value=""(?<token>[^""]+)"" />")]
	private static partial Regex TokenRegex();

	/// <summary>
	/// Regex for the address rows.
	/// </summary>
	[GeneratedRegex(@"<a href=""/apps/waste-collections/Routes/Route/(?<uid>\d+)"">(?<address>[^<]+)</a>")]
	private static partial Regex AddressRegex();

	/// <summary>
	/// Regex for the bin collection rows.
	/// </summary>
	[GeneratedRegex(
		@"<tr>\s*<td[^>]*>(?<date>\d{2}/\d{2}/\d{4})[^<]*</td>\s*<td[^>]*>(?<service>[^<]+)</td>\s*<td[^>]*>[^<]+</td>\s*</tr>",
		RegexOptions.IgnoreCase
	)]
	private static partial Regex BinDayRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting the initial page
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = "https://online.aberdeenshire.gov.uk/apps/waste-collections/",
				Method = "GET",
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Prepare client-side request for searching addresses
		else if (clientSideResponse.RequestId == 1)
		{
			var token = TokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;
			var requestCookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(clientSideResponse.Headers["set-cookie"]);

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = "https://online.aberdeenshire.gov.uk/apps/waste-collections/",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
					{ "cookie", requestCookies },
				},
				Body = ProcessingUtilities.ConvertDictionaryToFormData(new()
				{
					{ "PageModel.searchTerms", postcode },
					{ "SearchButton", "Search" },
					{ "__RequestVerificationToken", token },
				}),
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Process addresses from response
		else if (clientSideResponse.RequestId == 2)
		{
			var rawAddresses = AddressRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (Match rawAddress in rawAddresses)
			{
				var address = new Address
				{
					Property = WebUtility.HtmlDecode(rawAddress.Groups["address"].Value).Trim(),
					Postcode = postcode,
					Uid = rawAddress.Groups["uid"].Value.Trim(),
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
		// Prepare client-side request for getting bin days
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = $"https://online.aberdeenshire.gov.uk/apps/waste-collections/Routes/Route/{address.Uid!}",
				Method = "GET",
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Process bin days from response
		else if (clientSideResponse.RequestId == 1)
		{
			var rawBinDays = BinDayRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each bin day, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (Match rawBinDay in rawBinDays)
			{
				var collectionDate = rawBinDay.Groups["date"].Value.Trim();
				var service = WebUtility.HtmlDecode(rawBinDay.Groups["service"].Value).Trim();

				var date = DateUtilities.ParseDateExact(collectionDate, "dd/MM/yyyy");

				var matchedBins = ProcessingUtilities.GetMatchingBins(_binTypes, service);

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
