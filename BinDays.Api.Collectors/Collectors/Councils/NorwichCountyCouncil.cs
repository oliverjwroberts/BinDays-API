namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Norwich County Council.
/// </summary>
internal sealed partial class NorwichCountyCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Norwich County Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://norwich.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "norwich";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Black,
			Keys = [ "Domestic Waste Collection Service" ],
		},
		new()
		{
			Name = "Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Recycling Collection Service" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Green,
			Keys = [ "Food Waste Collection Service" ],
			Type = BinType.Caddy,
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "Garden Waste Collection Service" ],
		},
	];

	/// <summary>
	/// The base URL for the Norwich collection service.
	/// </summary>
	private const string _baseUrl = "https://bnr-wrp.whitespacews.com";

	/// <summary>
	/// Regex for extracting the address lookup URL.
	/// </summary>
	[GeneratedRegex(@"<form action=""(?<addressLookupUrl>https://bnr-wrp\.whitespacews\.com/mop\.php\?serviceID=A&Track=[^""]+&seq=2)""\s+method=""post""\s+oldTarget=""MoP""\s+align=""left""\s+data-form-title=""Property Lookup Form"">")]
	private static partial Regex AddressLookupUrlRegex();

	/// <summary>
	/// Regex for extracting addresses from the address list.
	/// </summary>
	[GeneratedRegex(@"href=""mop\.php\?Track=(?<track>[^&""]+)&serviceID=A&seq=3&pIndex=(?<pIndex>\d+)""[^>]*>\s*(?<address>[^<]+)\s*</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex AddressRegex();

	/// <summary>
	/// Regex for extracting bin collection dates and services.
	/// </summary>
	[GeneratedRegex(@"<p class=""colorblack fontfamilyRoboto fontsize12rem"">(?<date>\d{2}/\d{2}/\d{4})</p>\s*</li>\s*<li[^>]*>\s*<p class=""colorblack fontfamilyRoboto fontsize12rem"">(?<service>[^<]+)</p>", RegexOptions.Singleline)]
	private static partial Regex BinDaysRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting the postcode form
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = $"{_baseUrl}/?serviceID=A&seq=1",
				Method = "GET",
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Prepare client-side request for address lookup
		else if (clientSideResponse.RequestId == 1)
		{
			var addressLookupUrl = AddressLookupUrlRegex().Match(clientSideResponse.Content).Groups["addressLookupUrl"].Value;

			var requestBody = $"address_postcode={postcode}";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = addressLookupUrl,
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
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
				var track = rawAddress.Groups["track"].Value;
				var pIndex = rawAddress.Groups["pIndex"].Value;

				// Uid format: "track;pIndex"
				var address = new Address
				{
					Property = rawAddress.Groups["address"].Value.Trim(),
					Postcode = postcode,
					Uid = $"{track};{pIndex}",
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
		// Uid format: "track;pIndex"
		var uidParts = address.Uid!.Split(';', 2);
		var track = uidParts[0];
		var pIndex = uidParts[1];

		// Prepare client-side request for getting bin days
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = $"{_baseUrl}/mop.php?Track={track}&serviceID=A&seq=3&pIndex={pIndex}",
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
			var rawBinDays = BinDaysRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each bin day, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (Match rawBinDay in rawBinDays)
			{
				var service = rawBinDay.Groups["service"].Value.Trim();
				var dateText = rawBinDay.Groups["date"].Value;

				var date = DateUtilities.ParseDateExact(dateText, "dd/MM/yyyy");
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
