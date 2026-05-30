namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Southampton City Council.
/// </summary>
internal sealed partial class SouthamptonCityCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Southampton City Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.southampton.gov.uk/bins-recycling/bins/");

	/// <inheritdoc/>
	public override string GovUkId => "southampton";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes = [
		new()
		{
			Name = "Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Recycling" ],
		},
		new()
		{
			Name = "Glass",
			Colour = BinColour.Grey,
			Keys = [ "Glass" ],
		},
		new()
		{
			Name = "General",
			Colour = BinColour.Green,
			Keys = [ "General" ],
		},
		new()
		{
			Name = "Garden",
			Colour = BinColour.Brown,
			Keys = [ "Garden" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Brown,
			Keys = [ "Food" ],
			Type = BinType.Caddy,
		},
	];

	/// <summary>
	/// The URL for the collections page, used across all steps in GetAddresses.
	/// </summary>
	private const string _collectionsUrl = "https://www.southampton.gov.uk/bins-recycling/bins/collections/";

	/// <summary>
	/// Regex for the ufprt token values from input fields.
	/// </summary>
	[GeneratedRegex(@"<input[^>]*?(?:name|id)=[""']ufprt[""'][^>]*?value=[""'](?<ufprt>[^""']*)[""'][^>]*?/?>")]
	private static partial Regex UfprtTokenRegex();

	/// <summary>
	/// Regex for the __RequestVerificationToken token values from input fields.
	/// </summary>
	[GeneratedRegex(@"<input[^>]*?(?:name|id)=[""']__RequestVerificationToken[""'][^>]*?value=[""'](?<token>[^""']*)[""'][^>]*?/?>")]
	private static partial Regex RequestVerificationTokenRegex();

	/// <summary>
	/// Regex for the addresses from the options elements.
	/// </summary>
	[GeneratedRegex(@"<option\s+value=""(?<uid>\d+),\d*""[^>]*>\s*(?<address>.*?)\s*</option>")]
	private static partial Regex AddressesRegex();

	/// <summary>
	/// Regex for the bin days from the data table elements.
	/// </summary>
	[GeneratedRegex(@"\{title:\s*'<img[^>]*?alt=""(?<binType>[^""]+)""[^>]*>',\s*start:\s*'(?<collectionDate>\d{1,2}\/\d{1,2}\/\d{4})\s+\d{1,2}:\d{2}:\d{2}\s+[AP]M'\}")]
	private static partial Regex BinDaysRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting token
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _collectionsUrl,
				Method = "GET",
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Handle Incapsula challenge, or extract tokens from the page
		else if (clientSideResponse.RequestId == 1)
		{
			if (IncapsulaSolver.IsChallenge(clientSideResponse))
			{
				if (IncapsulaSolver.GetStoredCookie(clientSideResponse) != null)
				{
					throw new InvalidOperationException("Incapsula challenge could not be solved.");
				}

				var bypassRequest = IncapsulaSolver.BuildBypassRequest(
					new ClientSideRequest
					{
						RequestId = 1,
						Url = _collectionsUrl,
						Method = "GET",
					},
					clientSideResponse
				);

				return new GetAddressesResponse { NextClientSideRequest = bypassRequest };
			}

			var ufprtMatch = UfprtTokenRegex().Match(clientSideResponse.Content);
			var requestVerificationTokenMatch = RequestVerificationTokenRegex().Match(clientSideResponse.Content);
			if (!ufprtMatch.Success)
			{
				throw new InvalidOperationException("Could not find required 'ufprt' token for address lookup.");
			}
			if (!requestVerificationTokenMatch.Success)
			{
				throw new InvalidOperationException("Could not find required '__RequestVerificationToken' for address lookup.");
			}
			var ufprt = ufprtMatch.Groups["ufprt"].Value;
			var requestVerificationToken = requestVerificationTokenMatch.Groups["token"].Value;

			// Prepare client-side request
			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "SearchString", postcode },
				{ "ufprt", ufprt },
				{ "__RequestVerificationToken", requestVerificationToken },
			});

			Dictionary<string, string> requestHeaders = new()
			{
				{ "user-agent", Constants.UserAgent },
				{ "content-type", Constants.FormUrlEncoded },
			};

			// Merge Incapsula cookies (if challenge was solved) with any cookies set by the page
			var incapsulaCookie = IncapsulaSolver.GetStoredCookie(clientSideResponse);
			var setCookieHeader = clientSideResponse.Headers.GetValueOrDefault("set-cookie");
			var pageCookie = string.IsNullOrEmpty(setCookieHeader)
				? null
				: ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(setCookieHeader);
			var combinedCookie = string.Join("; ", new[] { incapsulaCookie, pageCookie }
				.Where(c => !string.IsNullOrEmpty(c)));

			if (!string.IsNullOrEmpty(combinedCookie))
			{
				requestHeaders["cookie"] = combinedCookie;
			}

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = _collectionsUrl,
				Method = "POST",
				Headers = requestHeaders,
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
			// Get addresses from response
			var rawAddresses = AddressesRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (Match rawAddress in rawAddresses)
			{
				var property = rawAddress.Groups["address"].Value;
				var uprn = rawAddress.Groups["uid"].Value;

				var address = new Address
				{
					Property = property,
					Postcode = postcode,
					Uid = uprn,
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
		// The waste calendar endpoint is not Incapsula-protected, so no challenge handling is needed.
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = $"https://www.southampton.gov.uk/whereilive/waste-calendar?UPRN={address.Uid}",
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
			// Get bin days from response
			var rawBinDays = BinDaysRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each bin day, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (Match rawBinDay in rawBinDays)
			{
				var service = rawBinDay.Groups["binType"].Value;
				var collectionDate = rawBinDay.Groups["collectionDate"].Value;

				// Parse the collection date (6/19/2025)
				var date = DateUtilities.ParseDateExact(collectionDate, "M/d/yyyy");

				// Get matching bin types from the service using the keys
				var matchedBinTypes = ProcessingUtilities.GetMatchingBins(_binTypes, service);

				var binDay = new BinDay
				{
					Date = date,
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

		// Throw exception for invalid request
		throw new InvalidOperationException("Invalid client-side request.");
	}
}
