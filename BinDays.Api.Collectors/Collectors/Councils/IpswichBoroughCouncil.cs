namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Ipswich Borough Council.
/// </summary>
internal sealed partial class IpswichBoroughCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Ipswich Borough Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.ipswich.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "ipswich";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "Large Food Waste",
			Colour = BinColour.Grey,
			Keys = [ "Large food waste caddy" ],
			Type = BinType.Caddy,
		},
		new()
		{
			Name = "Blue Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Blue recycling bin" ],
		},
		new()
		{
			Name = "Green-Lidded Recycling",
			Colour = BinColour.Green,
			Keys = [ "Green-lidded recycling bin" ],
		},
		new()
		{
			Name = "Black Refuse",
			Colour = BinColour.Black,
			Keys = [ "Black refuse bin" ],
		},
		new()
		{
			Name = "Brown Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "Brown garden waste bin" ],
		},
	];

	/// <summary>
	/// The base URL for Ipswich's bin collection pages.
	/// </summary>
	private const string _baseUrl = "https://app.ipswich.gov.uk";

	/// <summary>
	/// Regex to extract addresses from the street search results list.
	/// </summary>
	[GeneratedRegex(@"<li>\s*<a href=""/bin-collection/weeks/(?<uid>\d+)"">(?<street>[^<]+)</a>\s*</li>")]
	private static partial Regex AddressRegex();

	/// <summary>
	/// Regex to extract the street name and UID when the site skips address selection and returns
	/// bin days directly for an unambiguous street name.
	/// </summary>
	[GeneratedRegex(@"<h2>(?<street>[^<]+)</h2>[\s\S]*?/bin-collection/(?:pdf|months)/(?<uid>\d+)")]
	private static partial Regex DirectBinDaysRegex();

	/// <summary>
	/// Regex to extract bin days from the calendar entries.
	/// </summary>
	[GeneratedRegex(@"<dt class=""ibc-calendar-entry"">[\s\S]*?<div class=""ibc-calendar-entry__date"">(?<day>\d+)<span class=""ibc-visually-hidden"">[^<]+</span></div>\s*<div class=""ibc-calendar-entry__month"">(?<monthYear>[^<]+)</div>[\s\S]*?<dd class=""ibc-calendar-entry__details"">[\s\S]*?<ul>\s*(?<bins>[\s\S]*?)\s*</ul>")]
	private static partial Regex BinDaysRegex();

	/// <summary>
	/// Regex to extract bin service names from the bin day list items.
	/// </summary>
	[GeneratedRegex(@"<li class=""[^""]+"">(?<service>[^<]+)</li>")]
	private static partial Regex BinNameRegex();

	/// <summary>
	/// Regex to extract street names from the HTML-encoded autocomplete attribute. The &amp;quot;
	/// encoding only appears inside data-autocomplete, making the pattern unambiguous.
	/// </summary>
	[GeneratedRegex(@"&quot;(?<street>[^&]+)&quot;")]
	private static partial Regex AutocompleteStreetRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Step 1: GET postcodes.io to obtain lat/lon for the postcode
		if (clientSideResponse == null)
		{
			var clientSideRequest = GeocodingUtilities.CreatePostcodesIoRequest(postcode, 1);

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Step 2: GET Nominatim reverse geocode to resolve lat/lon to a road name
		else if (clientSideResponse.RequestId == 1)
		{
			var clientSideRequest = GeocodingUtilities.CreateNominatimReverseGeocodeRequest(clientSideResponse.Content, 2);

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Step 3: GET Ipswich search page to resolve the road name against the autocomplete list
		else if (clientSideResponse.RequestId == 2)
		{
			var road = GeocodingUtilities.ParseRoadName(clientSideResponse.Content)
				.Replace("'", string.Empty)
				.Replace("’", string.Empty);

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 3,
				Url = $"{_baseUrl}/bin-collection/",
				Method = "GET",
				Options = new ClientSideOptions
				{
					Metadata = { { "road", road } },
				},
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Step 4: POST to Ipswich bin collection search using the normalized road name
		else if (clientSideResponse.RequestId == 3)
		{
			var road = clientSideResponse.Options.Metadata["road"];
			var normalizedRoad = road.Replace(" ", "").ToLowerInvariant();

			// Nominatim may use a different spelling (e.g. "Coleness Road" vs "Cole Ness Road");
			// the autocomplete list contains the canonical form, so find the normalized match.
			var streetName = road;
			foreach (Match m in AutocompleteStreetRegex().Matches(clientSideResponse.Content)!)
			{
				var street = m.Groups["street"].Value;
				if (street.Replace(" ", "").Equals(normalizedRoad, StringComparison.InvariantCultureIgnoreCase))
				{
					streetName = street;
					break;
				}
			}

			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "street-name", streetName },
				{ "submit-button", string.Empty },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 4,
				Url = $"{_baseUrl}/bin-collection/",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
				},
				Body = requestBody,
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Parse addresses from Ipswich response
		else if (clientSideResponse.RequestId == 4)
		{
			var rawAddresses = AddressRegex().Matches(clientSideResponse.Content)!;

			if (rawAddresses.Count > 0)
			{
				// Iterate through each address, and create a new address object
				var addresses = new List<Address>();
				foreach (Match rawAddress in rawAddresses)
				{
					var street = WebUtility.HtmlDecode(rawAddress.Groups["street"].Value.Trim());
					var uid = rawAddress.Groups["uid"].Value.Trim();

					addresses.Add(new Address
					{
						Property = street,
						Postcode = postcode,
						Uid = uid,
					});
				}

				var getAddressesResponse = new GetAddressesResponse
				{
					Addresses = [.. addresses],
				};

				return getAddressesResponse;
			}

			var directMatch = DirectBinDaysRegex().Match(clientSideResponse.Content);
			if (directMatch.Success)
			{
				var street = WebUtility.HtmlDecode(directMatch.Groups["street"].Value.Trim());
				var uid = directMatch.Groups["uid"].Value.Trim();

				var getAddressesResponse = new GetAddressesResponse
				{
					Addresses =
					[
						new Address
						{
							Property = street,
							Postcode = postcode,
							Uid = uid,
						},
					],
				};

				return getAddressesResponse;
			}

			return new GetAddressesResponse { Addresses = [] };
		}

		throw new InvalidOperationException("Invalid client-side request.");
	}

	/// <inheritdoc/>
	public GetBinDaysResponse GetBinDays(Address address, ClientSideResponse? clientSideResponse)
	{
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = $"{_baseUrl}/bin-collection/weeks/{address.Uid!}",
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
			var rawBinDays = BinDaysRegex().Matches(clientSideResponse.Content)!;

			var binDays = new List<BinDay>();
			foreach (Match rawBinDay in rawBinDays)
			{
				var day = rawBinDay.Groups["day"].Value.Trim();
				var monthYear = rawBinDay.Groups["monthYear"].Value.Trim();
				var date = DateUtilities.ParseDateExact(
					$"{day} {monthYear}",
					"d MMMM yyyy"
				);

				var rawBins = BinNameRegex().Matches(rawBinDay.Groups["bins"].Value)!;

				foreach (Match rawBin in rawBins)
				{
					var service = WebUtility.HtmlDecode(rawBin.Groups["service"].Value.Trim());
					var matchedBins = ProcessingUtilities.GetMatchingBins(_binTypes, service);

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
			}

			return new GetBinDaysResponse
			{
				BinDays = ProcessingUtilities.ProcessBinDays(binDays),
			};
		}

		throw new InvalidOperationException("Invalid client-side request.");
	}
}
