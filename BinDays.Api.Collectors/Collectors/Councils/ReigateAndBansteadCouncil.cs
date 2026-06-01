namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Reigate and Banstead Borough Council.
/// </summary>
internal sealed partial class ReigateAndBansteadCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Reigate and Banstead Borough Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://whereilive.reigate-banstead.gov.uk");

	/// <inheritdoc/>
	public override string GovUkId => "reigate-and-banstead";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Green,
			Keys = [ "Refuse Collection" ],
		},
		new()
		{
			Name = "Mixed Recycling",
			Colour = BinColour.Grey,
			Keys = [ "Recycling Collection" ],
		},
		new()
		{
			Name = "Paper Recycling",
			Colour = BinColour.Black,
			Keys = [ "Paper Collection" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Brown,
			Keys = [ "Food Waste Collection" ],
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "Garden Waste Collection" ],
		},
	];

	/// <summary>
	/// Regex for the CSRF token from the Laravel search form.
	/// </summary>
	[GeneratedRegex(@"<input[^>]*?name=""_token""[^>]*?value=""(?<token>[^""]+)""[^>]*?/?>")]
	private static partial Regex TokenRegex();

	/// <summary>
	/// Regex for the addresses from the UPRN select options.
	/// </summary>
	[GeneratedRegex(@"<option value=""(?<uprn>\d+)"">(?<address>[^<]+)</option>")]
	private static partial Regex AddressRegex();

	/// <summary>
	/// Regex for the bin day service name and date from the waste page.
	/// </summary>
	[GeneratedRegex(
		@"Your next\s+<span[^>]*?class=""govuk-!-font-weight-bold""[^>]*?>\s*(?<service>[^<]+?)\s*</span[^>]*?>\s+is on\s+<br[^>]*?/?>\s+<span[^>]*?class=""govuk-!-font-weight-bold""[^>]*?>\s*(?<date>[^<]+?)\s*</span[^>]*?>",
		RegexOptions.Singleline)]
	private static partial Regex BinDayRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting token
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = "https://whereilive.reigate-banstead.gov.uk/search",
				Method = "GET",
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Prepare client-side request for getting addresses
		else if (clientSideResponse.RequestId == 1)
		{
			var setCookieHeader = clientSideResponse.Headers["set-cookie"];
			var cookie = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(setCookieHeader);

			var token = TokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;

			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "_token", token },
				{ "postcode", postcode },
				{ "flow", "postcode" },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = "https://whereilive.reigate-banstead.gov.uk/search",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
					{ "cookie", cookie },
				},
				Body = requestBody,
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
					Uid = rawAddress.Groups["uprn"].Value.Trim(),
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
				Url = $"https://whereilive.reigate-banstead.gov.uk/waste/{address.Uid!}",
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
				var service = rawBinDay.Groups["service"].Value.Trim();
				var date = rawBinDay.Groups["date"].Value.Trim();

				var matchedBins = ProcessingUtilities.GetMatchingBins(_binTypes, service);

				if (matchedBins.Count == 0)
				{
					continue;
				}

				var collectionDate = DateUtilities.ParseDateInferringYear(date, "dddd dd MMMM");

				var binDay = new BinDay
				{
					Date = collectionDate,
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
