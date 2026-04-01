namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Plymouth Council.
/// </summary>
internal sealed partial class PlymouthCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Plymouth Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.plymouth.gov.uk/checkyourcollectionday");

	/// <inheritdoc/>
	public override string GovUkId => "plymouth";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes = [
		new()
		{
			Name = "Domestic",
			Colour = BinColour.Brown,
			Keys = [ "Residual" ],
		},
		new()
		{
			Name = "Recycling",
			Colour = BinColour.Green,
			Keys = [ "Recycling" ],
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Black,
			Keys = [ "Garden" ],
		},
	];

	/// <summary>
	/// Regex to extract the session ID (sid) from HTML content.
	/// </summary>
	[GeneratedRegex(@"sid=([a-f0-9]+)")]
	private static partial Regex SessionIdRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Step 1: Get Session ID
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = "https://plymouth-self.achieveservice.com/en/AchieveForms/?form_uri=sandbox-publish://AF-Process-31283f9a-3ae7-4225-af71-bf3884e0ac1b/AF-Stagedba4a7d5-e916-46b6-abdb-643d38bec875/definition.json&redirectlink=/en&cancelRedirectLink=/en&consentMessage=yes",
				Method = "GET",
			};

			var getAddressesResponse = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getAddressesResponse;
		}
		// Step 2: Get Addresses using Session ID and Postcode
		else if (clientSideResponse.RequestId == 1)
		{
			// Get set-cookies from response
			var setCookies = clientSideResponse.Headers["set-cookie"];
			var requestCookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(setCookies);

			// Extract Session ID from Step 1 response content
			var sessionId = SessionIdRegex().Match(clientSideResponse.Content).Groups[1].Value;

			// Prepare request body as a JSON string
			var requestBodyObject = new
			{
				formValues = new
				{
					Section1 = new
					{
						postcode_search = new
						{
							name = "postcode_search",
							type = "text",
							id = "AF-Field-c627b676-e7a7-428c-9196-2e59b2a36100",
							value_changed = true,
							section_id = "AF-Section-f62c31c7-a20e-4cb7-bec2-ed2260daa14c",
							label = "Postcode / Street Search (min 5 characters)",
							value = postcode,
							path = "root/addressDetails/postcode_search",
							valid = true,
						}
					}
				}
			};
			var requestBody = JsonSerializer.Serialize(requestBodyObject);

			var requestUrl = $"https://plymouth-self.achieveservice.com/apibroker/?api=RunLookup&id=560d5266e930f&sid={sessionId}";

			var requestHeaders = new Dictionary<string, string> {
				{"content-type", Constants.ApplicationJson},
				{"cookie", requestCookies},
			};

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = requestUrl,
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
		// Step 3: Process Addresses from Response
		else if (clientSideResponse.RequestId == 2)
		{
			// Parse response content as JSON object
			var responseJson = JsonDocument.Parse(clientSideResponse.Content).RootElement;
			var rawAddresses = responseJson.GetProperty("integration").GetProperty("transformed").GetProperty("rows_data");

			// Iterate through each address object
			var addresses = new List<Address>();
			foreach (var property in rawAddresses.EnumerateObject())
			{
				var addressData = property.Value;

				var flat = addressData.GetProperty("flat").ToString();
				var house = addressData.GetProperty("house").ToString();
				var street = addressData.GetProperty("street").ToString();
				var town = addressData.GetProperty("town").ToString();
				var uprn = addressData.GetProperty("uprn").ToString();

				// Combine flat and house for property, ensuring no double spaces
				var addressProperty = $"{flat} {house}".Trim().Replace("  ", " ");

				var address = new Address
				{
					Property = addressProperty,
					Street = street.Trim(),
					Town = town.Trim(),
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
		// Step 1: Get Session ID (same as GetAddresses Step 1)
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = "https://plymouth-self.achieveservice.com/en/AchieveForms/?form_uri=sandbox-publish://AF-Process-31283f9a-3ae7-4225-af71-bf3884e0ac1b/AF-Stagedba4a7d5-e916-46b6-abdb-643d38bec875/definition.json&redirectlink=/en&cancelRedirectLink=/en&consentMessage=yes",
				Method = "GET",
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Step 2: Get Collective API key using Session ID and UPRN
		else if (clientSideResponse.RequestId == 1)
		{
			var setCookies = clientSideResponse.Headers["set-cookie"];
			var requestCookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(setCookies);
			var sessionId = SessionIdRegex().Match(clientSideResponse.Content).Groups[1].Value;

			var requestBody = $$"""
				{
					"formValues": {
						"Section1": {
							"number1": {
								"value": "{{address.Uid}}"
							}
						}
					}
				}
				""";

			var requestUrl = $"https://plymouth-self.achieveservice.com/apibroker/runLookup?id=6936e38f6d376&repeat_against=&noRetry=true&getOnlyTokens=undefined&log_id=&app_name=AF-Renderer::Self&sid={sessionId}";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = requestUrl,
				Method = "POST",
				Headers = new()
				{
					{ "content-type", Constants.ApplicationJson },
					{ "cookie", requestCookies },
				},
				Body = requestBody,
				Options = new ClientSideOptions
				{
					Metadata =
					{
						{ "sessionId", sessionId },
						{ "cookie", requestCookies },
					},
				},
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Step 3: Get collection jobs using Collective API key
		else if (clientSideResponse.RequestId == 2)
		{
			var responseJson = JsonDocument.Parse(clientSideResponse.Content).RootElement;
			var collectiveKey = responseJson
				.GetProperty("integration").GetProperty("transformed").GetProperty("rows_data")
				.GetProperty("0").GetProperty("collectiveKey").GetString()!;

			var sessionId = clientSideResponse.Options.Metadata["sessionId"];
			var requestCookies = clientSideResponse.Options.Metadata["cookie"];

			var startDate = DateTime.Today.ToString("yyyy-MM-ddT00:00:00");
			var endDate = DateTime.Today.AddDays(90).ToString("yyyy-MM-ddT00:00:00");

			var requestBody = $$"""
				{
					"formValues": {
						"Section1": {
							"collectiveKey": {
								"value": "{{collectiveKey}}"
							},
							"collectiveUPRN": {
								"value": "{{address.Uid}}"
							},
							"collectiveGetJobStartDate": {
								"value": "{{startDate}}"
							},
							"collectiveGetJobEndDate": {
								"value": "{{endDate}}"
							}
						}
					}
				}
				""";

			var requestUrl = $"https://plymouth-self.achieveservice.com/apibroker/runLookup?id=698b9c49a3c13&repeat_against=&noRetry=false&getOnlyTokens=undefined&log_id=&app_name=AF-Renderer::Self&sid={sessionId}";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 3,
				Url = requestUrl,
				Method = "POST",
				Headers = new()
				{
					{ "content-type", Constants.ApplicationJson },
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
		// Step 4: Process collection jobs from response
		else if (clientSideResponse.RequestId == 3)
		{
			var responseJson = JsonDocument.Parse(clientSideResponse.Content).RootElement;
			var rawBinDays = responseJson.GetProperty("integration").GetProperty("transformed").GetProperty("rows_data");

			var binDays = new List<BinDay>();
			foreach (var property in rawBinDays.EnumerateObject())
			{
				var binDayData = property.Value;

				var dateString = binDayData.GetProperty("collectiveCollectionDate").ToString();
				var wasteType = binDayData.GetProperty("collectiveWasteType").ToString();

				var date = DateUtilities.ParseDateExact(dateString, "dd/MM/yyyy");
				var matchedBins = ProcessingUtilities.GetMatchingBins(_binTypes, wasteType);

				if (matchedBins.Count == 0)
				{
					continue;
				}

				var binDay = new BinDay
				{
					Date = date,
					Address = address,
					Bins = [.. matchedBins],
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
