namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Collector implementation for Reading Borough Council.
/// </summary>
internal sealed class ReadingBoroughCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Reading Borough Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.reading.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "reading";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Grey,
			Keys = [ "Domestic Waste Collection Service" ],
		},
		new()
		{
			Name = "Mixed Recycling",
			Colour = BinColour.Green,
			Keys = [ "Bulk Recycling" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Grey,
			Keys = [ "Food Waste Collection Service" ],
			Type = BinType.Caddy,
		},
	];

	/// <summary>
	/// The base URL for the Reading collection API.
	/// </summary>
	private const string _apiBaseUrl = "https://api.reading.gov.uk";

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting addresses
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = $"{_apiBaseUrl}/rbc/getaddresses/{postcode}",
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
			using var jsonDocument = JsonDocument.Parse(clientSideResponse.Content);
			var addressElements = jsonDocument.RootElement.GetProperty("Addresses").EnumerateArray();

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (var addressElement in addressElements)
			{
				var address = new Address
				{
					Property = addressElement.GetProperty("SiteShortAddress").GetString()!.Trim(),
					Postcode = postcode,
					Uid = addressElement.GetProperty("AccountSiteUprn").GetString()!.Trim(),
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
				Url = $"{_apiBaseUrl}/api/collections/{address.Uid!}",
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
			using var jsonDocument = JsonDocument.Parse(clientSideResponse.Content);
			var collectionElements = jsonDocument.RootElement.GetProperty("collections").EnumerateArray();

			// Iterate through each bin day, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (var collectionElement in collectionElements)
			{
				var service = collectionElement.GetProperty("service").GetString()!.Trim();
				var collectionDate = collectionElement.GetProperty("date").GetString()!.Trim();

				var date = DateUtilities.ParseDateExact(collectionDate, "dd/MM/yyyy HH:mm:ss");
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
