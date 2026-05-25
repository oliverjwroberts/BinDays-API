namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for North East Lincolnshire Council.
/// </summary>
internal sealed partial class NorthEastLincolnshireCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "North East Lincolnshire Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.nelincs.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "north-east-lincolnshire";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "Household Waste",
			Colour = BinColour.Green,
			Keys = [ "Household Waste" ],
		},
		new()
		{
			Name = "Paper Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Paper" ],
		},
		new()
		{
			Name = "Cans, Plastic and Glass Recycling",
			Colour = BinColour.Black,
			Keys = [ "Cans, Plastic & Glass" ],
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "Garden" ],
		}
	];

	/// <summary>
	/// The URL of the refuse collection schedule search.
	/// </summary>
	private const string _searchUrl = "https://www.nelincs.gov.uk/";

	/// <summary>
	/// Regex for the addresses from the response data.
	/// </summary>
	[GeneratedRegex(@"<option value=""(?<uid>\d+)""[^>]*>\s*(?<address>[^<]+)\s*</option>", RegexOptions.IgnoreCase)]
	private static partial Regex AddressRegex();

	/// <summary>
	/// Regex for the bin collection sections from the response data.
	/// </summary>
	[GeneratedRegex(@"<div class=""mb-4"">\s*<div class=""px-4 py-3 text-white shadow-sm rounded""[^>]*>[\s\S]*?<div class=""h4[^""]*"">\s*(?<service>[^<]+)\s*</div>[\s\S]*?</div>\s*<div class=""mt-2"">\s*<ul class=""list-group shadow-sm\s*"">(?<dates>[\s\S]*?)</ul>", RegexOptions.IgnoreCase)]
	private static partial Regex BinSectionRegex();

	/// <summary>
	/// Regex for the collection dates from a bin section.
	/// </summary>
	[GeneratedRegex(@"<li class=""list-group-item"">\s*(?<date>[^<]+)\s*</li>", RegexOptions.IgnoreCase)]
	private static partial Regex CollectionDateRegex();

	/// <summary>
	/// Regex for ordinal suffixes in date strings.
	/// </summary>
	[GeneratedRegex(@"(?<=\d)(st|nd|rd|th)")]
	private static partial Regex OrdinalSuffixRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting addresses
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = $"{_searchUrl}?s={postcode}",
				Method = "GET",
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Process addresses from response
		else if (clientSideResponse.RequestId == 1)
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
				Url = $"{_searchUrl}?s={address.Postcode!}&uprn={address.Uid!}",
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
			var rawBinSections = BinSectionRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each bin section, and create new bin day objects
			var binDays = new List<BinDay>();
			foreach (Match rawBinSection in rawBinSections)
			{
				var service = WebUtility.HtmlDecode(rawBinSection.Groups["service"].Value).Trim();
				var matchedBins = ProcessingUtilities.GetMatchingBins(_binTypes, service);

				if (matchedBins.Count == 0)
				{
					continue;
				}

				var rawCollectionDates = CollectionDateRegex().Matches(rawBinSection.Groups["dates"].Value)!;

				// Iterate through each collection date, and create a new bin day object
				foreach (Match rawCollectionDate in rawCollectionDates)
				{
					var collectionDate = WebUtility.HtmlDecode(rawCollectionDate.Groups["date"].Value).Trim();
					var dateWithoutOrdinal = OrdinalSuffixRegex().Replace(collectionDate, string.Empty);

					var binDay = new BinDay
					{
						Date = DateUtilities.ParseDateExact(dateWithoutOrdinal, "dddd, d MMMM yyyy"),
						Address = address,
						Bins = matchedBins,
					};

					binDays.Add(binDay);
				}
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
