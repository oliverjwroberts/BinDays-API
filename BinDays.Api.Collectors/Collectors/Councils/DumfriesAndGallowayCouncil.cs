namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Dumfries and Galloway Council.
/// </summary>
internal sealed partial class DumfriesAndGallowayCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Dumfries and Galloway Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.dumfriesandgalloway.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "dumfries-and-galloway";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "Non-Recyclable Waste",
			Colour = BinColour.Grey,
			Keys = [ "Non-recyclable waste" ],
		},
		new()
		{
			Name = "Paper and Cardboard Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Paper and cardboard" ],
		},
		new()
		{
			Name = "Cans and Plastics Recycling",
			Colour = BinColour.Red,
			Keys = [ "Cans and plastics" ],
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "Garden waste" ],
		},
	];

	/// <summary>
	/// The waste collection schedule page URL.
	/// </summary>
	private const string _wasteCollectionScheduleUrl = "https://www.dumfriesandgalloway.gov.uk/bins-recycling/waste-collection-schedule";

	/// <summary>
	/// Regex for the form build ID.
	/// </summary>
	[GeneratedRegex(@"name=""form_build_id""\s+value=""(?<formBuildId>[^""]+)""")]
	private static partial Regex FormBuildIdRegex();

	/// <summary>
	/// Regex for the addresses from the response data.
	/// </summary>
	[GeneratedRegex(@"<option value=""(?<uid>\d+)"">(?<address>[^<]+)</option>")]
	private static partial Regex AddressRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting the postcode form
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _wasteCollectionScheduleUrl,
				Method = "GET",
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		else if (clientSideResponse.RequestId == 1)
		{
			// Prepare client-side request for posting the postcode
			var formBuildId = FormBuildIdRegex().Match(clientSideResponse.Content).Groups["formBuildId"].Value;

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = _wasteCollectionScheduleUrl,
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
				},
				Body = ProcessingUtilities.ConvertDictionaryToFormData(new()
				{
					{ "postcode", postcode },
					{ "op", "Find" },
					{ "form_build_id", formBuildId },
					{ "form_id", "localgov_waste_collection_postcode_form" },
				}),
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		else if (clientSideResponse.RequestId == 2)
		{
			// Process addresses from response
			var rawAddresses = AddressRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (Match rawAddress in rawAddresses)
			{
				var address = new Address
				{
					Property = rawAddress.Groups["address"].Value.Trim(),
					Postcode = postcode,
					Uid = rawAddress.Groups["uid"].Value,
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
		// Prepare client-side request for getting the collection calendar
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = $"https://www.dumfriesandgalloway.gov.uk/bins-recycling/waste-collection-schedule/download/{address.Uid!}",
				Method = "GET",
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		else if (clientSideResponse.RequestId == 1)
		{
			var unfoldedLines = new List<string>();
			var lines = clientSideResponse.Content.Split('\n');

			foreach (var line in lines)
			{
				var trimmedLine = line.TrimEnd('\r');

				if (trimmedLine.StartsWith(' ') && unfoldedLines.Count > 0)
				{
					unfoldedLines[^1] += trimmedLine[1..];
					continue;
				}

				unfoldedLines.Add(trimmedLine);
			}

			// Iterate through each event, and create a new bin day object
			var binDays = new List<BinDay>();
			var summary = string.Empty;
			var dateString = string.Empty;

			foreach (var line in unfoldedLines)
			{
				if (line == "BEGIN:VEVENT")
				{
					summary = string.Empty;
					dateString = string.Empty;
					continue;
				}

				if (line.StartsWith("SUMMARY:", StringComparison.Ordinal))
				{
					summary = line["SUMMARY:".Length..].Trim();
					continue;
				}

				if (line.StartsWith("DTSTART", StringComparison.Ordinal))
				{
					var value = line[(line.LastIndexOf(':') + 1)..];
					dateString = value[..8];
					continue;
				}

				if (line != "END:VEVENT")
				{
					continue;
				}

				var serviceStartIndex = summary.LastIndexOf(" for ", StringComparison.Ordinal) + 5;
				var service = summary[serviceStartIndex..];
				var matchedBins = ProcessingUtilities.GetMatchingBins(_binTypes, service);

				if (matchedBins.Count == 0)
				{
					continue;
				}

				var binDay = new BinDay
				{
					Date = DateUtilities.ParseDateExact(dateString, "yyyyMMdd"),
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
