namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Braintree District Council.
/// </summary>
internal sealed partial class BraintreeDistrictCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Braintree District Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.braintree.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "braintree";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Grey,
			Keys =
			[
				"Grey Bin",
				"Black Bin",
			],
		},
		new()
		{
			Name = "Mixed Recycling",
			Colour = BinColour.White,
			Keys = [ "Clear Sack" ],
			Type = BinType.Sack,
		},
		new()
		{
			Name = "Card and Paper Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Blue Bin" ],
		},
		new()
		{
			Name = "Glass and Metal Recycling",
			Colour = BinColour.Red,
			Keys = [ "Red Bin" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Green,
			Keys = [ "Food Bin" ],
			Type = BinType.Caddy,
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Green,
			Keys = [ "Garden Bin" ],
		},
		new()
		{
			Name = "Absorbent Hygiene Products",
			Colour = BinColour.White,
			Keys = [ "AHP" ],
			Type = BinType.Sack,
		},
	];

	/// <summary>
	/// The Braintree bin collection form URL.
	/// </summary>
	private const string _formUrl = "https://www.braintree.gov.uk/xfp/form/554";

	/// <summary>
	/// The fixed form page value required by the council endpoint.
	/// </summary>
	private const string _page = "5730";

	/// <summary>
	/// The postcode form field name for the property lookup form.
	/// </summary>
	private const string _postcodeFieldName = "qe15dda0155d237d1ea161004d1839e3369ed4831_0_0";

	/// <summary>
	/// Regex for extracting addresses from option elements.
	/// </summary>
	[GeneratedRegex(@"<option\s+value=""(?<uid>[^""]*)""[^>]*>\s*(?<address>[^<]+)\s*</option>")]
	private static partial Regex AddressRegex();

	/// <summary>
	/// Regex for extracting bin services and dates from the date display blocks.
	/// </summary>
	[GeneratedRegex(@"<div class=""date_display""[^>]*>\s*<h3[^>]*>\s*(?<service>[^<]+)\s*</h3>\s*<p>\s*(?<date>\d{2}/\d{2}/\d{4})\s*</p>", RegexOptions.Singleline)]
	private static partial Regex BinDaysRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting addresses
		if (clientSideResponse == null)
		{
			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "page", _page },
				{ _postcodeFieldName, postcode },
				{ "callback", "{\"action\":\"ic\",\"element\":\"qe15dda0155d237d1ea161004d1839e3369ed4831\",\"data\":0,\"tableRow\":-1}" },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _formUrl,
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
		// Process addresses from response
		else if (clientSideResponse.RequestId == 1)
		{
			var rawAddresses = AddressRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (Match rawAddress in rawAddresses)
			{
				var uid = rawAddress.Groups["uid"].Value;

				if (string.IsNullOrWhiteSpace(uid) || uid == "0")
				{
					continue;
				}

				var address = new Address
				{
					Property = rawAddress.Groups["address"].Value.Trim(),
					Postcode = postcode,
					Uid = uid,
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
		// Prepare client-side request for getting bin days
		if (clientSideResponse == null)
		{
			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "page", _page },
				{ _postcodeFieldName, address.Postcode! },
				{ "qe15dda0155d237d1ea161004d1839e3369ed4831_1_0", address.Uid! },
				{ "next", "Next" },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _formUrl,
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
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
			var rawBinDays = BinDaysRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each bin day, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (Match rawBinDay in rawBinDays)
			{
				var service = rawBinDay.Groups["service"].Value.Trim();
				var collectionDate = rawBinDay.Groups["date"].Value.Trim();

				var matchedBinTypes = ProcessingUtilities.GetMatchingBins(_binTypes, service);

				if (matchedBinTypes.Count == 0)
				{
					throw new InvalidOperationException($"No matching bin type found for service '{service}'.");
				}

				var binDay = new BinDay
				{
					Date = DateUtilities.ParseDateExact(collectionDate, "dd/MM/yyyy"),
					Address = address,
					Bins = matchedBinTypes,
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
