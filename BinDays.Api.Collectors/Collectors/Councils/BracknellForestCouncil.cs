namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Bracknell Forest Council.
/// </summary>
internal sealed partial class BracknellForestCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Bracknell Forest Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.bracknell-forest.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "bracknell-forest";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Black,
			Keys = [ "Food" ],
			Type = BinType.Caddy,
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "Garden" ],
		},
		new()
		{
			Name = "Mixed Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Recycling" ],
		},
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Green,
			Keys = [ "General waste" ],
		},
	];

	/// <summary>
	/// The URL for the waste collection endpoint.
	/// </summary>
	private const string _collectionsUrl = "https://selfservice.mybfc.bracknell-forest.gov.uk/w/webpage/waste-collection-days?webpage_subpage_id=PAG0000570FEFFB1&widget_action=handle_event";

	/// <summary>
	/// The action cell identifier used by the waste collection endpoint.
	/// </summary>
	private const string _actionCellId = "PCL0003988FEFFB1";

	/// <summary>
	/// The action page identifier used by the waste collection endpoint.
	/// </summary>
	private const string _actionPageId = "PAG0000570FEFFB1";

	/// <summary>
	/// Regex for extracting the collection date from upcoming collection text.
	/// </summary>
	[GeneratedRegex(@"(?<date>[A-Za-z]+\s\d{1,2}\s[A-Za-z]+\s\d{4})$")]
	private static partial Regex CollectionDateRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting addresses
		if (clientSideResponse == null)
		{
			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "code_action", "find_addresses" },
				{ "code_params", $$"""{"search":"{{postcode}}"}""" },
				{ "action_cell_id", _actionCellId },
				{ "action_page_id", _actionPageId },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _collectionsUrl,
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
					{ "x-requested-with", Constants.XmlHttpRequest },
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
			var rawAddresses = jsonDoc.RootElement
				.GetProperty("response")
				.GetProperty("addresses")
				.GetProperty("items");

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (var rawAddress in rawAddresses.EnumerateArray())
			{
				var property = rawAddress.GetProperty("Description").GetString()!.Trim();

				if (property.Contains("GARAGE", StringComparison.OrdinalIgnoreCase)
					|| property.Contains("RECREATION GROUND", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var address = new Address
				{
					Property = property,
					Postcode = postcode,
					Uid = rawAddress.GetProperty("Id").GetString()!,
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
			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "code_action", "find_rounds" },
				{ "code_params", $$"""{"addressId":"{{address.Uid!}}"}""" },
				{ "action_cell_id", _actionCellId },
				{ "action_page_id", _actionPageId },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _collectionsUrl,
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
					{ "x-requested-with", Constants.XmlHttpRequest },
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
			var rawCollections = jsonDoc.RootElement
				.GetProperty("response")
				.GetProperty("collections");

			// Iterate through each collection, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (var rawCollection in rawCollections.EnumerateArray())
			{
				var service = rawCollection.GetProperty("round").GetString()!;
				var matchedBinTypes = ProcessingUtilities.GetMatchingBins(_binTypes, service);

				foreach (var rawUpcomingCollection in rawCollection.GetProperty("upcomingCollections").EnumerateArray())
				{
					var collectionDate = CollectionDateRegex().Match(rawUpcomingCollection.GetString()!).Groups["date"].Value;
					var date = DateUtilities.ParseDateExact(collectionDate, "dddd d MMMM yyyy");

					var binDay = new BinDay
					{
						Date = date,
						Address = address,
						Bins = matchedBinTypes,
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
