namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for the Royal Borough of Windsor and Maidenhead.
/// </summary>
internal sealed partial class RoyalBoroughOfWindsorAndMaidenhead : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Royal Borough of Windsor and Maidenhead";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.rbwm.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "windsor-and-maidenhead";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Black,
			Keys = [ "Refuse Collection Service" ],
		},
		new()
		{
			Name = "Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Recycling Collection Service" ],
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Green,
			Keys = [ "Garden Waste Collection Service" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Grey,
			Keys = [ "Recycling Collection Service" ],
			Type = BinType.Caddy,
		},
	];

	/// <summary>
	/// Regex for parsing addresses and UPRNs from the address table.
	/// </summary>
	[GeneratedRegex(@"<tr>\s*<td>(?<address>[^<]+)</td>\s*<td>\s*<a href=""\?uprn=(?<uid>[^""]+)""", RegexOptions.Singleline)]
	private static partial Regex AddressRegex();

	/// <summary>
	/// Regex for parsing bin collection service rows from the schedule table.
	/// </summary>
	[GeneratedRegex(@"<tr>\s*<td>(?<service>[^<]+)</td>\s*<td>(?<date>[^<]+)</td>\s*</tr>", RegexOptions.Singleline)]
	private static partial Regex BinDaysRegex();

	/// <summary>
	/// Regex for removing ordinal suffixes from dates.
	/// </summary>
	[GeneratedRegex(@"(?<=\d)(st|nd|rd|th)", RegexOptions.IgnoreCase)]
	private static partial Regex OrdinalSuffixRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting addresses
		if (clientSideResponse == null)
		{
			var requestUrl = $"https://forms.rbwm.gov.uk/bincollections?postcode={postcode}";
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = requestUrl,
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
				var uid = rawAddress.Groups["uid"].Value.Trim();
				var property = WebUtility.HtmlDecode(rawAddress.Groups["address"].Value).Trim();

				var address = new Address
				{
					Property = property,
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

		// Throw exception for invalid request
		throw new InvalidOperationException("Invalid client-side request.");
	}

	/// <inheritdoc/>
	public GetBinDaysResponse GetBinDays(Address address, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting bin days
		if (clientSideResponse == null)
		{
			var requestUrl = $"https://forms.rbwm.gov.uk/bincollections?uprn={address.Uid!}";
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = requestUrl,
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
				var collectionDate = rawBinDay.Groups["date"].Value.Trim();
				var cleanedDate = OrdinalSuffixRegex().Replace(collectionDate, string.Empty);

				var date = DateUtilities.ParseDateExact(cleanedDate, "d MMMM yyyy");
				var matchedBinTypes = ProcessingUtilities.GetMatchingBins(_binTypes, service);

				var binDay = new BinDay
				{
					Date = date,
					Address = address,
					Bins = matchedBinTypes,
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
