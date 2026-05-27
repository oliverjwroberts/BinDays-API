namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>
/// Collector implementation for Plymouth Council.
/// </summary>
internal sealed partial class PlymouthCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Plymouth Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.plymouth.gov.uk/check-your-bin-day");

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
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Grey,
			Keys = [ "Food" ],
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
				Url = "https://plymouth-self.achieveservice.com/AchieveForms/?mode=fill&consentMessage=yes&form_uri=sandbox-publish://AF-Process-084d6742-3572-41ba-ac1a-430750451f9d/AF-Stage-67ba684d-0a5b-48f8-9c50-1c01cc43c396/definition.json&process=1&process_uri=sandbox-processes://AF-Process-084d6742-3572-41ba-ac1a-430750451f9d&process_id=AF-Process-084d6742-3572-41ba-ac1a-430750451f9d",
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

			var requestUrl = $"https://plymouth-self.achieveservice.com/apibroker/runLookup?id=560d5266e930f&repeat_against=&noRetry=false&getOnlyTokens=undefined&log_id=&app_name=AF-Renderer::Self&sid={sessionId}";

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
			var xmlData = JsonDocument.Parse(clientSideResponse.Content).RootElement
				.GetProperty("data").GetString()!;

			var addresses = new List<Address>();
			foreach (var row in XDocument.Parse(xmlData).Descendants("Row"))
			{
				var columns = row.Elements("result").ToDictionary(
					e => e.Attribute("column")!.Value,
					e => e.Value
				);

				var addressProperty = $"{columns["flat"]} {columns["house"]}".Trim().Replace("  ", " ");

				addresses.Add(new Address
				{
					Property = addressProperty,
					Street = columns["street"].Trim(),
					Town = columns["town"].Trim(),
					Postcode = postcode,
					Uid = columns["uprn"],
				});
			}

			return new GetAddressesResponse
			{
				Addresses = [.. addresses],
			};
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
				Url = "https://plymouth-self.achieveservice.com/AchieveForms/?mode=fill&consentMessage=yes&form_uri=sandbox-publish://AF-Process-084d6742-3572-41ba-ac1a-430750451f9d/AF-Stage-67ba684d-0a5b-48f8-9c50-1c01cc43c396/definition.json&process=1&process_uri=sandbox-processes://AF-Process-084d6742-3572-41ba-ac1a-430750451f9d&process_id=AF-Process-084d6742-3572-41ba-ac1a-430750451f9d",
				Method = "GET",
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Step 2: Get Collective API key using Session ID
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
		// Step 3: Get related UPRN using Collective API key
		else if (clientSideResponse.RequestId == 2)
		{
			var responseJson = JsonDocument.Parse(clientSideResponse.Content).RootElement;
			var collectiveKey = responseJson
				.GetProperty("integration").GetProperty("transformed").GetProperty("rows_data")
				.GetProperty("0").GetProperty("collectiveKey").GetString()!;

			var sessionId = clientSideResponse.Options.Metadata["sessionId"];
			var requestCookies = clientSideResponse.Options.Metadata["cookie"];

			var requestBody = $$"""
				{
					"formValues": {
						"Section1": {
							"collectiveUPRN": {
								"value": "{{address.Uid}}"
							},
							"collectiveKey": {
								"value": "{{collectiveKey}}"
							}
						}
					}
				}
				""";

			var requestUrl = $"https://plymouth-self.achieveservice.com/apibroker/runLookup?id=69f05bb2ad2d6&repeat_against=&noRetry=true&getOnlyTokens=undefined&log_id=&app_name=AF-Renderer::Self&sid={sessionId}";

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
				Options = new ClientSideOptions
				{
					Metadata =
					{
						{ "sessionId", sessionId },
						{ "cookie", requestCookies },
						{ "collectiveKey", collectiveKey },
					},
				},
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Step 4: Get collection jobs using related UPRN and Collective API key
		else if (clientSideResponse.RequestId == 3)
		{
			var responseJson = JsonDocument.Parse(clientSideResponse.Content).RootElement;
			var rowsData = responseJson
				.GetProperty("integration").GetProperty("transformed").GetProperty("rows_data");

			var collectiveUprn = address.Uid;
			if (rowsData.ValueKind == JsonValueKind.Object && rowsData.TryGetProperty("0", out var firstRow))
			{
				var relatedUprn = firstRow.GetProperty("collectiveRelatedUPRN").GetString();
				if (!string.IsNullOrEmpty(relatedUprn))
				{
					collectiveUprn = relatedUprn;
				}
			}

			var sessionId = clientSideResponse.Options.Metadata["sessionId"];
			var requestCookies = clientSideResponse.Options.Metadata["cookie"];
			var collectiveKey = clientSideResponse.Options.Metadata["collectiveKey"];

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
								"value": "{{collectiveUprn}}"
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
				RequestId = 4,
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
		// Step 5: Process collection jobs from response
		else if (clientSideResponse.RequestId == 4)
		{
			var responseJson = JsonDocument.Parse(clientSideResponse.Content).RootElement;
			var rawBinDays = responseJson.GetProperty("integration").GetProperty("transformed").GetProperty("rows_data");

			var binDays = new List<BinDay>();

			if (rawBinDays.ValueKind == JsonValueKind.Object)
			{
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
