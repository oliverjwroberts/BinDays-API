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

	[GeneratedRegex(@"<li>\s*<a href=""/bin-collection/weeks/(?<uid>\d+)"">(?<street>[^<]+)</a>\s*</li>")]
	private static partial Regex AddressRegex();

	// The Ipswich site skips address selection and returns bin days directly when the street name
	// is unambiguous. This regex extracts the street name and UID from that direct response.
	[GeneratedRegex(@"<h2>(?<street>[^<]+)</h2>[\s\S]*?/bin-collection/(?:pdf|months)/(?<uid>\d+)")]
	private static partial Regex DirectBinDaysRegex();

	[GeneratedRegex(@"<dt class=""ibc-calendar-entry"">[\s\S]*?<div class=""ibc-calendar-entry__date"">(?<day>\d+)<span class=""ibc-visually-hidden"">[^<]+</span></div>\s*<div class=""ibc-calendar-entry__month"">(?<monthYear>[^<]+)</div>[\s\S]*?<dd class=""ibc-calendar-entry__details"">[\s\S]*?<ul>\s*(?<bins>[\s\S]*?)\s*</ul>")]
	private static partial Regex BinDaysRegex();

	[GeneratedRegex(@"<li class=""[^""]+"">(?<service>[^<]+)</li>")]
	private static partial Regex BinNameRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Step 1: GET postcodes.io to obtain lat/lon for the postcode
		if (clientSideResponse == null)
		{
			return new GetAddressesResponse
			{
				NextClientSideRequest = GeocodingUtilities.CreatePostcodesIoRequest(postcode, 1)
			};
		}
		// Step 2: GET Nominatim reverse geocode to resolve lat/lon to a road name
		else if (clientSideResponse.RequestId == 1)
		{
			return new GetAddressesResponse
			{
				NextClientSideRequest = GeocodingUtilities.CreateNominatimReverseGeocodeRequest(clientSideResponse.Content, 2)
			};
		}
		// Step 3: POST to Ipswich bin collection search using the road name
		else if (clientSideResponse.RequestId == 2)
		{
			var road = GeocodingUtilities.ParseRoadName(clientSideResponse.Content)
				.Replace("'", string.Empty)
				.Replace("’", string.Empty);

			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "street-name", road },
				{ "submit-button", string.Empty },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 3,
				Url = $"{_baseUrl}/bin-collection/",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
				},
				Body = requestBody,
			};

			return new GetAddressesResponse { NextClientSideRequest = clientSideRequest };
		}
		// Parse addresses from Ipswich response
		else if (clientSideResponse.RequestId == 3)
		{
			var rawAddresses = AddressRegex().Matches(clientSideResponse.Content)!;

			if (rawAddresses.Count > 0)
			{
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

				return new GetAddressesResponse { Addresses = [.. addresses] };
			}

			var directMatch = DirectBinDaysRegex().Match(clientSideResponse.Content);
			if (directMatch.Success)
			{
				var street = WebUtility.HtmlDecode(directMatch.Groups["street"].Value.Trim());
				var uid = directMatch.Groups["uid"].Value.Trim();

				return new GetAddressesResponse
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

			return new GetBinDaysResponse { NextClientSideRequest = clientSideRequest };
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
