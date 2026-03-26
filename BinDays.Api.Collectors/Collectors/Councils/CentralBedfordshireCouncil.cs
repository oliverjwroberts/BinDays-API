namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Central Bedfordshire Council.
/// </summary>
internal sealed partial class CentralBedfordshireCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Central Bedfordshire Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.centralbedfordshire.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "central-bedfordshire";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Black,
			Keys = [ "Refuse (black bin)" ],
		},
		new()
		{
			Name = "Recycling",
			Colour = BinColour.Green,
			Keys = [ "Recycling" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Brown,
			Keys = [ "Food waste" ],
			Type = BinType.Caddy,
		},
	];

	/// <summary>
	/// The bin collection page used for all requests.
	/// </summary>
	private const string _collectionUrl = "https://www.centralbedfordshire.gov.uk/info/163/bins_and_waste_collections_-_check_bin_collection_days";

	/// <summary>
	/// Regex for extracting addresses from the HTML response.
	/// </summary>
	[GeneratedRegex(@"<option value='(?<uid>[^']+)'>(?<address>[^<]+)<\/option>")]
	private static partial Regex AddressRegex();

	/// <summary>
	/// Regex for extracting bin collection dates and services.
	/// </summary>
	[GeneratedRegex(@"<h3>(?<date>[^<]+)<\/h3>(?<bins>.+?)(?=<h3>|<p>)", RegexOptions.Singleline)]
	private static partial Regex BinDaysRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting the collection page and cookies
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _collectionUrl,
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
			var requestCookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(clientSideResponse.Headers["set-cookie"]);
			var requestBody = $"postcode={postcode}";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = _collectionUrl,
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
					{ "cookie", requestCookies },
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
				var addressText = rawAddress.Groups["address"].Value.Trim();
				var uprn = rawAddress.Groups["uid"].Value.Trim();

				var address = new Address
				{
					Property = addressText,
					Postcode = postcode,
					// UID format: "<uprn>;<address>"
					Uid = $"{uprn};{addressText}",
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
		// Prepare client-side request for getting the collection page and cookies
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _collectionUrl,
				Method = "GET",
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Prepare client-side request for getting bin days
		else if (clientSideResponse.RequestId == 1)
		{
			var requestCookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(clientSideResponse.Headers["set-cookie"]);
			var uidParts = address.Uid!.Split(';', 2);
			var uprn = uidParts[0];
			var addressText = uidParts[1];

			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "address_text", addressText },
				{ "postcode", address.Postcode! },
				{ "address", uprn },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = _collectionUrl,
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
					{ "cookie", requestCookies },
				},
				Body = requestBody,
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Process bin days from response
		else if (clientSideResponse.RequestId == 2)
		{
			var rawBinDays = BinDaysRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each bin day, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (Match rawBinDay in rawBinDays)
			{
				var dateText = rawBinDay.Groups["date"].Value.Trim();
				var binsText = rawBinDay.Groups["bins"].Value;

				var binServices = binsText.Split("<br>", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				var matchedBins = new List<Bin>();
				foreach (var binService in binServices)
				{
					matchedBins.AddRange(ProcessingUtilities.GetMatchingBins(_binTypes, binService));
				}

				var binDay = new BinDay
				{
					Date = DateUtilities.ParseDateExact(dateText, "dddd, d MMMM yyyy"),
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
