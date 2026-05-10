namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Exceptions;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Collector implementation for East Ayrshire Council.
/// </summary>
internal sealed class EastAyrshireCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "East Ayrshire Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.east-ayrshire.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "east-ayrshire";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Grey,
			Keys = ["REFUSE", "Refuse"],
		},
		new()
		{
			Name = "Paper and Cardboard Recycling",
			Colour = BinColour.Blue,
			Keys = ["RECYCLING", "Paper"],
		},
		new()
		{
			Name = "Plastic and Cans Recycling",
			Colour = BinColour.Green,
			Keys = ["Plastic"],
		},
		new()
		{
			Name = "Glass Recycling",
			Colour = BinColour.Purple,
			Keys = ["Glass"],
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Brown,
			Keys = ["Garden"],
		},
		new()
		{
			Name = "Trolley Collection",
			Colour = BinColour.Blue,
			Keys = ["transition_week"],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Grey,
			Keys =
			[
				"REFUSE",
				"RECYCLING",
				"Refuse",
				"Paper",
				"Plastic",
				"Glass",
				"transition_week",
			],
			Type = BinType.Caddy,
		},
	];

	/// <summary>
	/// The base API URL for the ReCollect service.
	/// </summary>
	private const string _apiBaseUrl = "https://api.eu.recollect.net";

	/// <summary>
	/// The ReCollect area name for East Ayrshire.
	/// </summary>
	private const string _areaName = "EastAyrshireUK";

	/// <summary>
	/// The ReCollect service identifier for waste collections.
	/// </summary>
	private const string _serviceId = "50014";

	/// <summary>
	/// The locale used by the ReCollect API.
	/// </summary>
	private const string _locale = "en-GB";

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting postcode matches
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = $"{_apiBaseUrl}/api/areas/{_areaName}/services/{_serviceId}/address-suggest?q={postcode}&locale={_locale}",
				Method = "GET",
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Prepare client-side request for getting addresses in the postcode qualifier
		else if (clientSideResponse.RequestId == 1)
		{
			using var jsonDocument = JsonDocument.Parse(clientSideResponse.Content);
			if (jsonDocument.RootElement.GetArrayLength() == 0)
			{
				throw new AddressesNotFoundException(GovUkId, postcode);
			}

			var qualifierId = jsonDocument.RootElement[0].GetProperty("qualifier_id").GetString()!;

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = $"{_apiBaseUrl}/api/areas/{_areaName}/services/{_serviceId}/pages/{_locale}/place_calendar.json",
				Method = "GET",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "x-recollect-place", $"qualifier.{qualifierId}:{_serviceId}" },
				},
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
			using var jsonDocument = JsonDocument.Parse(clientSideResponse.Content);
			var addressRows = jsonDocument.RootElement.GetProperty("sections")[0].GetProperty("rows");

			// Iterate through each address row, and create a new address object
			var addresses = new List<Address>();
			foreach (var addressRow in addressRows.EnumerateArray())
			{
				var placeId = addressRow.GetProperty("place_id").GetString()!;
				var placeIdParts = placeId.Split(':', 2);

				var address = new Address
				{
					Property = addressRow.GetProperty("label").GetString()!.Trim(),
					Postcode = postcode,
					Uid = placeIdParts[0],
				};

				addresses.Add(address);
			}

			if (addresses.Count == 0)
			{
				throw new AddressesNotFoundException(GovUkId, postcode);
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
		// Prepare client-side request for getting bin day events
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = $"{_apiBaseUrl}/api/places/{address.Uid!}/services/{_serviceId}/events?locale={_locale}",
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
			var rawEvents = jsonDocument.RootElement.GetProperty("events");

			// Iterate through each event, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (var rawEvent in rawEvents.EnumerateArray())
			{
				var matchingBins = new List<Bin>();

				// Iterate through each event flag, and map it to bins
				foreach (var rawFlag in rawEvent.GetProperty("flags").EnumerateArray())
				{
					if (rawFlag.GetProperty("event_type").GetString()! != "pickup")
					{
						continue;
					}

					var flagName = rawFlag.GetProperty("name").GetString()!;
					matchingBins.AddRange(ProcessingUtilities.GetMatchingBins(_binTypes, flagName));
				}

				if (matchingBins.Count == 0)
				{
					continue;
				}

				var binDay = new BinDay
				{
					Date = DateUtilities.ParseDateExact(rawEvent.GetProperty("day").GetString()!, "yyyy-MM-dd"),
					Address = address,
					Bins = matchingBins,
				};

				binDays.Add(binDay);
			}

			if (binDays.Count == 0)
			{
				throw new BinDaysNotFoundException(GovUkId, address.Postcode!, address.Uid!);
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
