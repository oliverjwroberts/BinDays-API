namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Glasgow City Council.
/// </summary>
internal sealed partial class GlasgowCityCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Glasgow City Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.glasgow.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "glasgow";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Green,
			Keys = [ "greenBin.gif" ],
		},
		new()
		{
			Name = "Paper, Card & Cardboard Recycling",
			Colour = BinColour.Blue,
			Keys = [ "blueBin.gif" ],
		},
		new()
		{
			Name = "Plastics, Cans & Cartons Recycling",
			Colour = BinColour.Grey,
			Keys = [ "greyBin.gif" ],
		},
		new()
		{
			Name = "Glass Recycling",
			Colour = BinColour.Purple,
			Keys = [ "purpleBin.gif" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Brown,
			Keys = [ "foodBin.gif" ],
			Type = BinType.Caddy,
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "brownBin.gif" ],
		},
	];

	/// <summary>
	/// The address search URL.
	/// </summary>
	private const string _addressSearchUrl = "https://onlineservices.glasgow.gov.uk/forms/refuseandrecyclingcalendar/AddressSearch.aspx";

	/// <summary>
	/// Regex for extracting hidden form fields.
	/// </summary>
	[GeneratedRegex(@"<input[^>]*name=""(?<name>__VIEWSTATE|__EVENTVALIDATION)""[^>]*value=""(?<value>[^""]+)""")]
	private static partial Regex HiddenFieldRegex();

	/// <summary>
	/// Regex for extracting selectable addresses.
	/// </summary>
	[GeneratedRegex(@"href=""javascript:__doPostBack\(&#39;(?<eventTarget>ctl00\$Application\$Addresses\$Select\d+)&#39;,&#39;&#39;\)""[^>]*>Select<\/a><\/td><td>(?<address>[^<]+)<\/td>")]
	private static partial Regex AddressRegex();

	/// <summary>
	/// Regex for extracting bin collection entries from the calendar grid.
	/// </summary>
	[GeneratedRegex(@"<td title=""(?<date>[^""]+)""[^>]*>(?:(?!<\/td>).)*?<img[^>]*src=""\.\./Images/Bins/(?<image>[^""]+)""(?:(?!<\/td>).)*?<\/td>", RegexOptions.Singleline)]
	private static partial Regex BinDayRegex();

	/// <summary>
	/// Regex for removing the "today is" prefix from title text.
	/// </summary>
	[GeneratedRegex(@"^today is\s+", RegexOptions.IgnoreCase)]
	private static partial Regex TodayPrefixRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for loading the address search form
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _addressSearchUrl,
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
			var viewState = GetHiddenFieldValue(clientSideResponse.Content, "__VIEWSTATE");
			var eventValidation = GetHiddenFieldValue(clientSideResponse.Content, "__EVENTVALIDATION");

			var clientSideRequest = CreateAddressSearchRequest(2, postcode, viewState, eventValidation);

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Process addresses from response
		else if (clientSideResponse.RequestId == 2)
		{
			var rawAddresses = AddressRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (Match rawAddress in rawAddresses)
			{
				var address = new Address
				{
					Property = WebUtility.HtmlDecode(rawAddress.Groups["address"].Value).Trim(),
					Postcode = postcode,
					Uid = rawAddress.Groups["eventTarget"].Value.Trim(),
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
		// Prepare client-side request for loading the address search form
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _addressSearchUrl,
				Method = "GET",
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Prepare client-side request for searching addresses
		else if (clientSideResponse.RequestId == 1)
		{
			var viewState = GetHiddenFieldValue(clientSideResponse.Content, "__VIEWSTATE");
			var eventValidation = GetHiddenFieldValue(clientSideResponse.Content, "__EVENTVALIDATION");

			var clientSideRequest = CreateAddressSearchRequest(2, address.Postcode!, viewState, eventValidation);

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Prepare client-side request for selecting the address
		else if (clientSideResponse.RequestId == 2)
		{
			var viewState = GetHiddenFieldValue(clientSideResponse.Content, "__VIEWSTATE");
			var eventValidation = GetHiddenFieldValue(clientSideResponse.Content, "__EVENTVALIDATION");

			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "__EVENTTARGET", address.Uid! },
				{ "__VIEWSTATE", viewState },
				{ "__EVENTVALIDATION", eventValidation },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 3,
				Url = _addressSearchUrl,
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
		else if (clientSideResponse.RequestId == 3)
		{
			var rawBinDays = BinDayRegex().Matches(clientSideResponse.Content)!;

			// Iterate through each bin day, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (Match rawBinDay in rawBinDays)
			{
				var rawDate = rawBinDay.Groups["date"].Value.Trim();
				var dateString = TodayPrefixRegex().Replace(rawDate, string.Empty);
				var image = rawBinDay.Groups["image"].Value.Trim();

				var date = DateUtilities.ParseDateExact(dateString, "dddd, dd MMMM yyyy");
				var matchedBins = ProcessingUtilities.GetMatchingBins(_binTypes, image);

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

		throw new InvalidOperationException("Invalid client-side request.");
	}

	/// <summary>
	/// Creates a client-side request for postcode address search.
	/// </summary>
	private static ClientSideRequest CreateAddressSearchRequest(int requestId, string postcode, string viewState, string eventValidation)
	{
		var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
		{
			{ "__VIEWSTATE", viewState },
			{ "__EVENTVALIDATION", eventValidation },
			{ "ctl00$Application$Addresses$Search", postcode },
			{ "ctl00$Application$Addresses$ImageButton.x", "80" },
			{ "ctl00$Application$Addresses$ImageButton.y", "18" },
		});

		var clientSideRequest = new ClientSideRequest
		{
			RequestId = requestId,
			Url = _addressSearchUrl,
			Method = "POST",
			Headers = new()
			{
				{ "user-agent", Constants.UserAgent },
				{ "content-type", Constants.FormUrlEncoded },
			},
			Body = requestBody,
		};

		return clientSideRequest;
	}

	/// <summary>
	/// Gets the value of a hidden field from the HTML.
	/// </summary>
	private static string GetHiddenFieldValue(string content, string fieldName)
	{
		var hiddenFields = HiddenFieldRegex().Matches(content)!;

		// Iterate through each hidden field to find the requested field
		foreach (Match hiddenField in hiddenFields)
		{
			var name = hiddenField.Groups["name"].Value;
			if (name == fieldName)
			{
				return hiddenField.Groups["value"].Value;
			}
		}

		throw new InvalidOperationException($"Hidden field '{fieldName}' was not found.");
	}
}
