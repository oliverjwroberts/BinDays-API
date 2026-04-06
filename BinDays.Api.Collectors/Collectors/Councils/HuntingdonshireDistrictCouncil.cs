namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Collector implementation for Huntingdonshire District Council.
/// </summary>
internal sealed class HuntingdonshireDistrictCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Huntingdonshire District Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://huntingdonshire.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "huntingdonshire";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Black,
			Keys = [ "Normal Waste" ],
		},
		new()
		{
			Name = "Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Recycling" ],
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Green,
			Keys = [ "Garden Waste" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Grey,
			Keys = [ "Food Waste" ],
			Type = BinType.Caddy,
		},
	];

	/// <summary>
	/// The authority key used by the council API.
	/// </summary>
	private const string _authority = "HDC";

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting addresses
		if (clientSideResponse == null)
		{
			var requestUrl = $"https://huntingdonshire.gov.uk/address/search?postcode={postcode}&authority={_authority}";
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
			using var addressesJson = JsonDocument.Parse(clientSideResponse.Content);
			var rawAddresses = addressesJson.RootElement.GetProperty("addresses").EnumerateArray();

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (var rawAddress in rawAddresses)
			{
				var houseNumber = rawAddress.GetProperty("houseNumber").GetString()!;
				var street = rawAddress.GetProperty("street").GetString()!;
				var town = rawAddress.GetProperty("town").GetString()!;
				var addressPostcode = rawAddress.GetProperty("postCode").GetString()!;
				var property = string.Join(", ", new[] { houseNumber, street, town, addressPostcode }.Where(part => !string.IsNullOrWhiteSpace(part)));

				var address = new Address
				{
					Property = property.Trim(),
					Postcode = postcode,
					Uid = rawAddress.GetProperty("id").GetString()!,
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
			var requestUrl = $"https://servicelayer3c.azure-api.net/wastecalendar/calendar/ical/{address.Uid!}?authority={_authority}";
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
			var lines = clientSideResponse.Content.Split('\n');
			var summary = string.Empty;
			var dateString = string.Empty;

			// Iterate through each line, and create bin day objects from each event
			var binDays = new List<BinDay>();
			foreach (var line in lines)
			{
				var trimmedLine = line.TrimEnd('\r');

				if (trimmedLine == "BEGIN:VEVENT")
				{
					summary = string.Empty;
					dateString = string.Empty;
					continue;
				}

				if (trimmedLine.StartsWith("SUMMARY:", StringComparison.Ordinal))
				{
					summary = trimmedLine["SUMMARY:".Length..].Trim();
					continue;
				}

				if (trimmedLine.StartsWith("DTSTART", StringComparison.Ordinal))
				{
					var dateValue = trimmedLine[(trimmedLine.LastIndexOf(':') + 1)..];
					dateString = dateValue[..8];
					continue;
				}

				if (trimmedLine != "END:VEVENT")
				{
					continue;
				}

				if (string.IsNullOrWhiteSpace(summary))
				{
					throw new InvalidOperationException("Missing summary in iCal event.");
				}

				if (string.IsNullOrWhiteSpace(dateString))
				{
					throw new InvalidOperationException("Missing date in iCal event.");
				}

				var matchedBins = ProcessingUtilities.GetMatchingBins(_binTypes, summary);
				if (matchedBins.Count == 0)
				{
					throw new InvalidOperationException($"No matching bin type found for iCal summary: {summary}.");
				}

				var date = DateUtilities.ParseDateExact(dateString, "yyyyMMdd");
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
