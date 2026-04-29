namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Collector implementation for City of York Council.
/// </summary>
internal sealed class CityOfYorkCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "City of York Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.york.gov.uk/bins-and-recycling");

	/// <inheritdoc/>
	public override string GovUkId => "york";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Grey,
			Keys = [ "REFUSE" ],
		},
		new()
		{
			Name = "Recycling",
			Colour = BinColour.Black,
			Keys = [ "RECYCLING" ],
			Type = BinType.Box,
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Green,
			Keys = [ "GARDEN" ],
		},
	];

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting addresses
		if (clientSideResponse == null)
		{
			return new GetAddressesResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 1,
					Url = $"https://addresses.york.gov.uk/api/address/lookupbypostcode/{postcode}",
					Method = "GET",
				},
			};
		}
		// Process addresses from response
		else if (clientSideResponse.RequestId == 1)
		{
			using var jsonDoc = JsonDocument.Parse(clientSideResponse.Content);

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (var addressElement in jsonDoc.RootElement.EnumerateArray())
			{
				var property = addressElement.GetProperty("shortAddress").GetString()!.Trim();
				var uprn = addressElement.GetProperty("uprn").GetString()!;

				addresses.Add(new Address
				{
					Property = property,
					Postcode = postcode,
					Uid = uprn,
				});
			}

			return new GetAddressesResponse
			{
				Addresses = [.. addresses],
			};
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
			return new GetBinDaysResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 1,
					Url = $"https://waste-api.york.gov.uk/api/Collections/GetBinCollectionDataForUprn/{address.Uid}",
					Method = "GET",
				},
			};
		}
		// Process bin days from response
		else if (clientSideResponse.RequestId == 1)
		{
			using var jsonDoc = JsonDocument.Parse(clientSideResponse.Content);

			// Iterate through each bin day, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (var serviceElement in jsonDoc.RootElement.GetProperty("services").EnumerateArray())
			{
				var nextCollection = serviceElement.GetProperty("nextCollection").GetString()!;

				if (string.IsNullOrWhiteSpace(nextCollection))
				{
					continue;
				}

				var date = DateUtilities.ParseDateExact(nextCollection, "yyyy-MM-ddTHH:mm:ss");
				var serviceKey = serviceElement.GetProperty("service").GetString()!;
				var matchedBins = ProcessingUtilities.GetMatchingBins(_binTypes, serviceKey);

				if (matchedBins.Count == 0)
				{
					continue;
				}

				binDays.Add(new BinDay
				{
					Date = date,
					Address = address,
					Bins = matchedBins,
				});
			}

			return new GetBinDaysResponse
			{
				BinDays = ProcessingUtilities.ProcessBinDays(binDays),
			};
		}

		// Throw exception for invalid request
		throw new InvalidOperationException("Invalid client-side request.");
	}
}
