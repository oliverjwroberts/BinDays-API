namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Tonbridge and Malling Borough Council.
/// </summary>
internal sealed partial class TonbridgeAndMallingBoroughCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Tonbridge and Malling Borough Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.tmbc.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "tonbridge-and-malling";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Black,
			Keys = [ "Black domestic waste" ],
		},
		new()
		{
			Name = "Mixed Recycling",
			Colour = BinColour.Green,
			Keys = [ "Green recycling" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Yellow,
			Keys = [ "Food waste" ],
			Type = BinType.Caddy,
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "Brown garden waste" ],
		},
	];

	/// <summary>
	/// Regex for the CSRF token from the form.
	/// </summary>
	[GeneratedRegex(@"name=""__token"" value=""(?<token>[^""]+)""")]
	private static partial Regex TokenRegex();

	/// <summary>
	/// Regex for the addresses from the dropdown options.
	/// </summary>
	[GeneratedRegex(@"<option\s+value=""(?<uid>\d+)""[^>]*>\s*(?<address>.*?)\s*</option>")]
	private static partial Regex AddressRegex();

	/// <summary>
	/// Regex for bin day table rows, capturing the collection date and services HTML.
	/// </summary>
	[GeneratedRegex(@"(?<date>[A-Z][a-z]{2} \d{1,2} [A-Z][a-z]+)</td>.*?<div class=""collections"">(?<collections>.*?)</div>", RegexOptions.Singleline)]
	private static partial Regex BinDaysRowRegex();

	/// <summary>
	/// Regex for service names within a collections div.
	/// </summary>
	[GeneratedRegex(@"<p>(?<service>[^<]+)</p>")]
	private static partial Regex ServiceRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting the form and CSRF token
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = "https://www.tmbc.gov.uk/xfp/form/167",
				Method = "GET",
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Prepare client-side request for looking up addresses by postcode
		else if (clientSideResponse.RequestId == 1)
		{
			var token = TokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;

			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "__token", token },
				{ "page", "128" },
				{ "locale", "en_GB" },
				{ "injectedParams", "{'formID':'167'}" },
				{ "q752eec300b2ffef2757e4536b77b07061842041a_0_0", postcode },
				{ "callback", "{'action':'ic','element':'q752eec300b2ffef2757e4536b77b07061842041a','data':0,'tableRow':-1}" },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = "https://www.tmbc.gov.uk/xfp/form/167",
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
		// Process addresses from the postcode lookup response
		else if (clientSideResponse.RequestId == 2)
		{
			var rawAddresses = AddressRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (Match rawAddress in rawAddresses)
			{
				var uid = rawAddress.Groups["uid"].Value;

				var address = new Address
				{
					Property = rawAddress.Groups["address"].Value,
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

		// Throw exception for invalid request
		throw new InvalidOperationException("Invalid client-side request.");
	}

	/// <inheritdoc/>
	public GetBinDaysResponse GetBinDays(Address address, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting the form and CSRF token
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = "https://www.tmbc.gov.uk/xfp/form/167",
				Method = "GET",
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Prepare client-side request for submitting the address to get bin days
		else if (clientSideResponse.RequestId == 1)
		{
			var token = TokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;

			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "__token", token },
				{ "page", "128" },
				{ "locale", "en_GB" },
				{ "injectedParams", "{'formID':'167'}" },
				{ "q752eec300b2ffef2757e4536b77b07061842041a_0_0", address.Postcode! },
				{ "q752eec300b2ffef2757e4536b77b07061842041a_1_0", address.Uid! },
				{ "next", "Next" },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = "https://www.tmbc.gov.uk/xfp/form/167",
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
		// Process bin days from the collection dates response
		else if (clientSideResponse.RequestId == 2)
		{
			var rawRows = BinDaysRowRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each collection date row, and create new bin day objects
			var binDays = new List<BinDay>();
			foreach (Match rawRow in rawRows)
			{
				var dateString = rawRow.Groups["date"].Value.Trim();
				var date = DateUtilities.ParseDateInferringYear(dateString, "ddd d MMMM");

				var collectionsHtml = rawRow.Groups["collections"].Value;

				// Iterate through each service listed for this collection date
				foreach (Match serviceMatch in ServiceRegex().Matches(collectionsHtml)!)
				{
					var service = serviceMatch.Groups["service"].Value.Trim();
					var matchedBins = ProcessingUtilities.GetMatchingBins(_binTypes, service);

					var binDay = new BinDay
					{
						Date = date,
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
