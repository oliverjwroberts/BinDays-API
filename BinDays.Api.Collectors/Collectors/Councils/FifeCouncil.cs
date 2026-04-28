namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Collector implementation for Fife Council.
/// </summary>
internal sealed class FifeCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Fife Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.fife.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "fife";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "Landfill Waste",
			Colour = BinColour.Blue,
			Keys = [ "Landfill / Blue Bin" ],
		},
		new()
		{
			Name = "Paper and Cardboard Recycling",
			Colour = BinColour.Grey,
			Keys = [ "Paper and Cardboard / Grey Bin" ],
		},
		new()
		{
			Name = "Cans and Plastics Recycling",
			Colour = BinColour.Green,
			Keys = [ "Cans and Plastics / Green Bin" ],
		},
		new()
		{
			Name = "Food and Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "Food and Garden Waste / Brown Bin" ],
		},
	];

	/// <summary>
	/// The URL for loading the citizen API context.
	/// </summary>
	private const string _citizenUrl = "https://fife.form.uk.empro.verintcloudservices.com/api/citizen";

	/// <summary>
	/// The form name for the bin calendar.
	/// </summary>
	private const string _formName = "bin_calendar";

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for loading citizen API context
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _citizenUrl,
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
			var authToken = clientSideResponse.Headers["authorization"];

			var requestBody = $$"""
			{
				"name": "{{_formName}}",
				"data": {
					"postcode": "{{postcode}}"
				}
			}
			""";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = "https://fife.form.uk.empro.verintcloudservices.com/api/widget?action=propertysearch&actionedby=ps_3SHSN93&loadform=true&access=citizen",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "authorization", authToken },
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
		else if (clientSideResponse.RequestId == 2)
		{
			using var document = JsonDocument.Parse(clientSideResponse.Content);
			var addressesJson = document.RootElement.GetProperty("data").EnumerateArray();

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (var addressElement in addressesJson)
			{
				var address = new Address
				{
					Property = addressElement.GetProperty("label").GetString()!.Trim(),
					Postcode = postcode,
					Uid = addressElement.GetProperty("value").GetString()!,
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
		// Prepare client-side request for loading citizen API context
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _citizenUrl,
				Method = "GET",
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Prepare client-side request for retrieving property data
		else if (clientSideResponse.RequestId == 1)
		{
			var authToken = clientSideResponse.Headers["authorization"];

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = $"https://fife.form.uk.empro.verintcloudservices.com/api/getobjectdata?objecttype=property&objectid={address.Uid!}",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "authorization", authToken },
				},
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Prepare client-side request for retrieving bin collections
		else if (clientSideResponse.RequestId == 2)
		{
			var authToken = clientSideResponse.Headers["authorization"];

			using var document = JsonDocument.Parse(clientSideResponse.Content);
			var uprn = document.RootElement.GetProperty("profileData").GetProperty("property-UPRN").GetString()!;

			var requestBody = $$"""
			{
				"name": "{{_formName}}",
				"data": {
					"uprn": "{{uprn}}"
				}
			}
			""";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 3,
				Url = "https://fife.form.uk.empro.verintcloudservices.com/api/custom?action=powersuite_bin_calendar_collections&actionedby=bin_calendar&loadform=true&access=citizen",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "authorization", authToken },
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
		else if (clientSideResponse.RequestId == 3)
		{
			using var document = JsonDocument.Parse(clientSideResponse.Content);
			var collectionsJson = document.RootElement.GetProperty("data").GetProperty("tab_collections").EnumerateArray();

			// Iterate through each collection row, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (var collectionElement in collectionsJson)
			{
				var service = collectionElement.GetProperty("type").GetString()!;
				var collectionDate = collectionElement.GetProperty("date").GetString()!;

				var matchedBins = ProcessingUtilities.GetMatchingBins(_binTypes, service);

				var binDay = new BinDay
				{
					Date = DateUtilities.ParseDateExact(collectionDate, "dddd, MMMM d, yyyy"),
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
