namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Perth and Kinross Council.
/// </summary>
internal sealed partial class PerthAndKinrossCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Perth and Kinross Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.pkc.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "perth-and-kinross";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "Non-Recyclable General Waste",
			Colour = BinColour.Green,
			Keys = [ "nextGeneralWasteCollectionDate", "nextGeneralWasteCollectionDate2nd" ],
		},
		new()
		{
			Name = "Paper and Card Recycling",
			Colour = BinColour.Blue,
			Keys = [ "nextBlueCollectionDate", "nextBlueWasteCollectionDate2nd" ],
		},
		new()
		{
			Name = "Plastic, Soft Plastics, Cans, Cartons and Foil Recycling",
			Colour = BinColour.Grey,
			Keys = [ "nextGreyWasteCollectionDate", "nextGreyWasteCollectionDate2nd" ],
		},
		new()
		{
			Name = "Food and Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "nextGardenandFoodWasteCollectionDate", "nextGardenandFoodWasteCollectionDate2nd" ],
		},
	];

	/// <summary>
	/// The base URL for the Perth and Kinross AchieveService API.
	/// </summary>
	private const string _baseUrl = "https://pkc-self.achieveservice.com";

	/// <summary>
	/// The session initialization URL for the AchieveService form.
	/// </summary>
	private const string _sessionUrl = $"{_baseUrl}/en/AchieveForms/?form_uri=sandbox-publish://AF-Process-de9223b1-a7c6-408f-aaa3-aee33fd7f7fa/AF-Stage-9fa33e2e-4c1b-4963-babf-4348ab8154bc/definition.json&consentMessage=yes";

	/// <summary>
	/// Regex to extract the session ID from the form page content.
	/// </summary>
	[GeneratedRegex(@"sid=(?<sid>[a-f0-9]+)")]
	private static partial Regex SessionIdRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for starting the session
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _sessionUrl,
				Method = "GET",
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Prepare client-side request for getting addresses
		else if (clientSideResponse.RequestId == 1)
		{
			var setCookieHeader = clientSideResponse.Headers["set-cookie"];
			var cookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(setCookieHeader);
			var sessionId = SessionIdRegex().Match(clientSideResponse.Content).Groups["sid"].Value;

			var requestBody = $$"""
			{
				"formValues": {
					"Bin collections": {
						"postcode_search": {
							"value": "{{postcode}}"
						}
					}
				}
			}
			""";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = $"{_baseUrl}/apibroker/runLookup?id=5876a68c6c9f0&sid={sessionId}",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.ApplicationJson },
					{ "cookie", cookies },
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
			using var jsonDocument = JsonDocument.Parse(clientSideResponse.Content);
			var rowsData = jsonDocument.RootElement
				.GetProperty("integration")
				.GetProperty("transformed")
				.GetProperty("rows_data");

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (var row in rowsData.EnumerateObject())
			{
				var rowData = row.Value;

				var address = new Address
				{
					Property = rowData.GetProperty("display").GetString()!.Trim(),
					Postcode = postcode,
					Uid = rowData.GetProperty("uprn").GetString()!.Trim(),
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
		// Prepare client-side request for starting the session
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = _sessionUrl,
				Method = "GET",
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Prepare client-side request for getting bin days
		else if (clientSideResponse.RequestId == 1)
		{
			var setCookieHeader = clientSideResponse.Headers["set-cookie"];
			var cookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(setCookieHeader);
			var sessionId = SessionIdRegex().Match(clientSideResponse.Content).Groups["sid"].Value;
			var uid = address.Uid!;

			var requestBody = $$"""
			{
				"formValues": {
					"Bin collections": {
						"propertyUPRNQuery": {
							"value": "{{uid}}"
						}
					}
				}
			}
			""";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = $"{_baseUrl}/apibroker/runLookup?id=5c9267cee5efe&sid={sessionId}",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.ApplicationJson },
					{ "cookie", cookies },
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
		else if (clientSideResponse.RequestId == 2)
		{
			using var jsonDocument = JsonDocument.Parse(clientSideResponse.Content);
			var rowsData = jsonDocument.RootElement
				.GetProperty("integration")
				.GetProperty("transformed")
				.GetProperty("rows_data");

			// Iterate through each configured bin type, and create new bin day objects
			var binDays = new List<BinDay>();
			foreach (var binType in _binTypes)
			{
				// Iterate through each response row, and create new bin day objects
				foreach (var row in rowsData.EnumerateObject())
				{
					// Iterate through each date key, and create a new bin day object
					foreach (var dateKey in binType.Keys)
					{
						var dateString = row.Value.GetProperty(dateKey).GetString()!;
						if (string.IsNullOrWhiteSpace(dateString))
						{
							continue;
						}

						var binDay = new BinDay
						{
							Date = DateUtilities.ParseDateExact(dateString, "dd/MM/yyyy"),
							Address = address,
							Bins = [binType],
						};

						binDays.Add(binDay);
					}
				}
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
