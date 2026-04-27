namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Collector implementation for South Tyneside Council.
/// </summary>
internal sealed class SouthTynesideCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "South Tyneside Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.southtyneside.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "south-tyneside";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Grey,
			Keys = [ "Household" ],
		},
		new()
		{
			Name = "Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Recycling (blue)" ],
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Green,
			Keys = [ "Recycling (green)" ],
		},
	];

	/// <summary>
	/// The South Tyneside AJAX library endpoint URL.
	/// </summary>
	private const string _ajaxLibraryUrl = "https://www.southtyneside.gov.uk/apiserver/ajaxlibrary";

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting addresses
		if (clientSideResponse == null)
		{
			var requestBody = $$"""
			{
				"id": "1",
				"method": "stc.common.snippets.getAddressList",
				"params": {
					"postcode": "{{postcode}}"
				}
			}
			""";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _ajaxLibraryUrl,
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.ApplicationJson },
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
		else if (clientSideResponse.RequestId == 1)
		{
			using var jsonDoc = JsonDocument.Parse(clientSideResponse.Content);
			var rawAddresses = jsonDoc.RootElement.GetProperty("result").GetProperty("ReturnedList");

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (var rawAddress in rawAddresses.EnumerateArray())
			{
				var uprn = rawAddress.GetProperty("UPRN").GetString()!.Trim();
				var fullAddress = rawAddress.GetProperty("Address").GetString()!.Trim();
				var shortAddress = rawAddress.GetProperty("ShortAddress").GetString()!.Trim();

				// Uid format: "uprn;fullAddress"
				var address = new Address
				{
					Property = shortAddress,
					Postcode = postcode,
					Uid = $"{uprn};{fullAddress}",
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
			// Uid format: "uprn;fullAddress"
			var uidParts = address.Uid!.Split(';', 2);
			var addressCode = $"{uidParts[0]}|{uidParts[1]}";

			var requestBody = $$"""
			{
				"id": "1",
				"method": "stc.waste.collections.getDates",
				"params": {
					"addresscode": "{{addressCode}}"
				}
			}
			""";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _ajaxLibraryUrl,
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.ApplicationJson },
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
		else if (clientSideResponse.RequestId == 1)
		{
			using var jsonDoc = JsonDocument.Parse(clientSideResponse.Content);
			var rawMonthlyCollections = jsonDoc.RootElement.GetProperty("result").GetProperty("SortedCollections");

			var binDays = new List<BinDay>();
			foreach (var rawMonthlyCollection in rawMonthlyCollections.EnumerateArray())
			{
				var rawCollections = rawMonthlyCollection.GetProperty("Collections");

				// Iterate through each bin day, and create a new bin day object
				foreach (var rawCollection in rawCollections.EnumerateArray())
				{
					var service = rawCollection.GetProperty("Type").GetString()!.Trim();
					var collectionDate = rawCollection.GetProperty("DateString").GetString()!.Trim();

					var matchedBins = ProcessingUtilities.GetMatchingBins(_binTypes, service);

					var binDay = new BinDay
					{
						Date = DateUtilities.ParseDateExact(collectionDate, "dd MMMM yyyy"),
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
