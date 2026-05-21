namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for London Borough of Croydon.
/// </summary>
internal sealed partial class LondonBoroughOfCroydon : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "London Borough of Croydon";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.croydon.gov.uk/rubbish-and-recycling/bins/check-your-bin-collection-days");

	/// <inheritdoc/>
	public override string GovUkId => "croydon";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Black,
			Keys = [ "General rubbish" ],
		},
		new()
		{
			Name = "Glass, Plastics, Cans, & Cartons Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Glass, plastics, cans and cartons recycling" ],
		},
		new()
		{
			Name = "Paper and Card Recycling",
			Colour = BinColour.Green,
			Keys = [ "Paper and card recycling" ],
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "Garden waste" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Brown,
			Keys = [ "Food waste" ],
			Type = BinType.Caddy,
		},
	];

	/// <summary>
	/// The bin-day lookup page URL.
	/// </summary>
	private const string _lookupPageUrl = "https://service.croydon.gov.uk/wasteservices/w/webpage/bin-day-enter-address";

	/// <summary>
	/// The lookup page ID.
	/// </summary>
	private const string _lookupPageId = "PAG0000898EECEC1";

	/// <summary>
	/// The lookup widget group ID.
	/// </summary>
	private const string _lookupWidgetGroupId = "PWG0002644EECEC1";

	/// <summary>
	/// The lookup cell ID.
	/// </summary>
	private const string _lookupCellId = "PCL0005629EECEC1";

	/// <summary>
	/// The address selector fragment ID.
	/// </summary>
	private const string _addressFragmentId = "PCF0020408EECEC1";

	/// <summary>
	/// The Next button fragment ID.
	/// </summary>
	private const string _nextButtonFragmentId = "PCF0020072EECEC1";

	/// <summary>
	/// Regex for the CSRF token.
	/// </summary>
	[GeneratedRegex(@"var CSRF = '(?<token>[^']+)'")]
	private static partial Regex CsrfTokenRegex();

	/// <summary>
	/// Regex for the webpage token.
	/// </summary>
	[GeneratedRegex(@"webpage_token=(?<token>[a-f0-9]+)")]
	private static partial Regex WebpageTokenRegex();

	/// <summary>
	/// Regex for submission tokens.
	/// </summary>
	[GeneratedRegex(@"name=""submission_token"" value=""(?<token>[^""]+)""")]
	private static partial Regex SubmissionTokenRegex();

	/// <summary>
	/// Regex for the dynamic address row unique key.
	/// </summary>
	[GeneratedRegex(@"\[formtable\]\[(?<key>C_[^\]]+)\]\[PCF0020408EECEC1\]")]
	private static partial Regex AddressRowUniqueKeyRegex();

	/// <summary>
	/// Regex for bin day service and date cards.
	/// </summary>
	[GeneratedRegex(@"<h2[^>]*>(?<service>[^<]+)</h2>[\s\S]*?Next collection[\s\S]*?<span class=""value-as-text"">(?<date>[A-Za-z]+ \d{1,2} [A-Za-z]+ \d{4})</span>", RegexOptions.IgnoreCase)]
	private static partial Regex BinDayRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting addresses
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _lookupPageUrl,
				Method = "GET",
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Prepare client-side request for address lookup
		else if (clientSideResponse.RequestId == 1)
		{
			var csrfToken = CsrfTokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;
			var pageToken = WebpageTokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;
			var setCookieHeader = clientSideResponse.Headers["set-cookie"];
			var requestCookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(setCookieHeader);

			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "code_action", "search" },
				{ "code_params", $$"""{"search_item":"{{postcode}}","is_ss":true}""" },
				{ "fragment_action", "handle_event" },
				{ "fragment_id", _addressFragmentId },
				{ "fragment_collection_class", "formtable" },
				{ "action_cell_id", _lookupCellId },
				{ "action_page_id", _lookupPageId },
				{ "form_check_ajax", csrfToken },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = $"{_lookupPageUrl}?webpage_subpage_id={_lookupPageId}&webpage_token={pageToken}&widget_action=fragment_action",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
					{ "x-requested-with", Constants.XmlHttpRequest },
					{ "cookie", requestCookies },
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
		else if (clientSideResponse.RequestId == 2)
		{
			using var jsonDoc = JsonDocument.Parse(clientSideResponse.Content);
			var items = jsonDoc.RootElement.GetProperty("response").GetProperty("items");

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (var item in items.EnumerateArray())
			{
				var address = new Address
				{
					Property = item.GetProperty("address_single_line").GetString()!.Trim(),
					Postcode = postcode,
					Uid = item.GetProperty("id").GetString()!,
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
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _lookupPageUrl,
				Method = "GET",
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Prepare client-side request for retrieving dynamic form values
		else if (clientSideResponse.RequestId == 1)
		{
			var csrfToken = CsrfTokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;
			var pageToken = WebpageTokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;
			var setCookieHeader = clientSideResponse.Headers["set-cookie"];
			var requestCookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(setCookieHeader);

			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "_dummy", "1" },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = _lookupPageUrl,
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
					{ "x-requested-with", Constants.XmlHttpRequest },
					{ "cookie", requestCookies },
				},
				Body = requestBody,
				Options = new ClientSideOptions
				{
					Metadata =
					{
						{ "cookie", requestCookies },
						{ "csrfToken", csrfToken },
						{ "pageToken", pageToken },
					},
				},
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Prepare client-side request for selecting the address and loading redirect URL
		else if (clientSideResponse.RequestId == 2)
		{
			using var jsonDoc = JsonDocument.Parse(clientSideResponse.Content);
			var formHtml = jsonDoc.RootElement.GetProperty("data").GetString()!;

			var submissionToken = SubmissionTokenRegex().Matches(formHtml)![^1].Groups["token"].Value;
			var uniqueKey = AddressRowUniqueKeyRegex().Match(formHtml).Groups["key"].Value;
			var requestCookies = clientSideResponse.Options.Metadata["cookie"];
			var csrfToken = clientSideResponse.Options.Metadata["csrfToken"];
			var pageToken = clientSideResponse.Options.Metadata["pageToken"];

			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "form_check", csrfToken },
				{ "submitted_page_id", _lookupPageId },
				{ "submitted_widget_group_id", _lookupWidgetGroupId },
				{ "submitted_widget_group_type", "modify" },
				{ "submission_token", submissionToken },
				{ $"payload[{_lookupPageId}][{_lookupWidgetGroupId}][{_lookupCellId}][formtable][{uniqueKey}][{_addressFragmentId}]", address.Uid! },
				{ $"payload[{_lookupPageId}][{_lookupWidgetGroupId}][{_lookupCellId}][formtable][{uniqueKey}][PCF0021449EECEC1]", "1" },
				{ $"payload[{_lookupPageId}][{_lookupWidgetGroupId}][{_lookupCellId}][formtable][{uniqueKey}][{_nextButtonFragmentId}]", "Next" },
				{ "submit_fragment_id", _nextButtonFragmentId },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 3,
				Url = $"{_lookupPageUrl}?webpage_subpage_id={_lookupPageId}&webpage_token={pageToken}",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
					{ "x-requested-with", Constants.XmlHttpRequest },
					{ "cookie", requestCookies },
				},
				Body = requestBody,
				Options = clientSideResponse.Options,
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Prepare client-side request for getting bin day data
		else if (clientSideResponse.RequestId == 3)
		{
			using var jsonDoc = JsonDocument.Parse(clientSideResponse.Content);
			var redirectUrl = jsonDoc.RootElement.GetProperty("redirect_url").GetString()!;
			var requestCookies = clientSideResponse.Options.Metadata["cookie"];
			var requestBody = ProcessingUtilities.ConvertDictionaryToFormData(new()
			{
				{ "_dummy", "1" },
			});

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 4,
				Url = $"https://service.croydon.gov.uk{redirectUrl}",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.FormUrlEncoded },
					{ "x-requested-with", Constants.XmlHttpRequest },
					{ "cookie", requestCookies },
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
		else if (clientSideResponse.RequestId == 4)
		{
			using var jsonDoc = JsonDocument.Parse(clientSideResponse.Content);
			var html = jsonDoc.RootElement.GetProperty("data").GetString()!;
			var rawBinDays = BinDayRegex().Matches(html)!;

			// Iterate through each bin day, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (Match rawBinDay in rawBinDays)
			{
				var service = rawBinDay.Groups["service"].Value.Trim();
				var collectionDate = rawBinDay.Groups["date"].Value.Trim();
				var date = DateUtilities.ParseDateExact(collectionDate, "dddd d MMMM yyyy");
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

		throw new InvalidOperationException("Invalid client-side request.");
	}
}
