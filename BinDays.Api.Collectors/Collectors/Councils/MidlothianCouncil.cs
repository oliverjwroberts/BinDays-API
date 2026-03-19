namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>
/// Collector implementation for Midlothian Council.
/// </summary>
internal sealed partial class MidlothianCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Midlothian Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.midlothian.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "midlothian";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Grey,
			Keys = [ "Residual Collection Service" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Grey,
			Keys = [ "Food Collection Service" ],
			Type = BinType.Caddy,
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "Garden Collection Service" ],
		},
		new()
		{
			Name = "Plastic, Cans and Cartons",
			Colour = BinColour.Blue,
			Keys = [ "Recycling Collection Service" ],
		},
		new()
		{
			Name = "Paper and Card",
			Colour = BinColour.Green,
			Keys = [ "Card Collection Service" ],
		},
		new()
		{
			Name = "Glass",
			Colour = BinColour.Red,
			Keys = [ "Glass Collection Service" ],
			Type = BinType.Box,
		},
	];

	/// <summary>
	/// The base URL for Midlothian council services.
	/// </summary>
	private const string _baseUrl = "https://my.midlothian.gov.uk";

	/// <summary>
	/// Regex to extract the session identifier from HTML.
	/// </summary>
	[GeneratedRegex(@"sid=(?<sid>[a-f0-9]+)")]
	private static partial Regex SidRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for starting the session
		if (clientSideResponse == null)
		{
			var clientSideRequest = CreateInitialRequest();

			var response = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return response;
		}
		// Fetch reference and CSRF token
		else if (clientSideResponse.RequestId == 1)
		{
			var nextClientSideRequest = CreateNextRefRequest(clientSideResponse);

			var response = new GetAddressesResponse
			{
				NextClientSideRequest = nextClientSideRequest,
			};

			return response;
		}
		// Request addresses for the supplied postcode
		else if (clientSideResponse.RequestId == 2)
		{
			var metadata = BuildMetadataWithTokens(clientSideResponse);
			var requestBody = BuildLookupRequestBody(
				postcode,
				string.Empty,
				metadata["reference"]
			);

			var clientSideRequest = CreateLookupRequest(
				3,
				"68f7a2ca3325e",
				requestBody,
				metadata
			);

			var response = new GetAddressesResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return response;
		}
		// Process addresses from response
		else if (clientSideResponse.RequestId == 3)
		{
			using var jsonDoc = JsonDocument.Parse(clientSideResponse.Content);
			var xmlData = jsonDoc.RootElement.GetProperty("data").GetString()!;

			var xml = XDocument.Parse(xmlData);
			var rows = xml.Descendants("Row");

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (var row in rows)
			{
				var results = row.Elements("result").ToList();
				var uprn = results.First(result => result.Attribute("column")!.Value == "uprn").Value.Trim();
				var display = results.First(result => result.Attribute("column")!.Value == "display").Value.Trim();

				var address = new Address
				{
					Property = display,
					Postcode = postcode,
					Uid = uprn,
				};

				addresses.Add(address);
			}

			var response = new GetAddressesResponse
			{
				Addresses = [.. addresses],
			};

			return response;
		}

		throw new InvalidOperationException("Invalid client-side request.");
	}

	/// <inheritdoc/>
	public GetBinDaysResponse GetBinDays(Address address, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for starting the session
		if (clientSideResponse == null)
		{
			var clientSideRequest = CreateInitialRequest();

			var response = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return response;
		}
		// Fetch reference and CSRF token
		else if (clientSideResponse.RequestId == 1)
		{
			var nextClientSideRequest = CreateNextRefRequest(clientSideResponse);

			var response = new GetBinDaysResponse
			{
				NextClientSideRequest = nextClientSideRequest,
			};

			return response;
		}
		// Request bin collections for the selected address
		else if (clientSideResponse.RequestId == 2)
		{
			var metadata = BuildMetadataWithTokens(clientSideResponse);
			var requestBody = BuildLookupRequestBody(
				address.Postcode!,
				address.Uid!,
				metadata["reference"]
			);

			var clientSideRequest = CreateLookupRequest(
				3,
				"69a19ba76d3a2",
				requestBody,
				metadata
			);

			var response = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return response;
		}
		// Process bin days from response
		else if (clientSideResponse.RequestId == 3)
		{
			using var jsonDoc = JsonDocument.Parse(clientSideResponse.Content);
			var xmlData = jsonDoc.RootElement.GetProperty("data").GetString()!;

			var xml = XDocument.Parse(xmlData);
			var rows = xml.Descendants("Row");

			// Iterate through each bin day, and create a new bin day object
			var binDays = new List<BinDay>();
			foreach (var row in rows)
			{
				var results = row.Elements("result").ToList();
				var service = results.First(result => result.Attribute("column")!.Value == "Service").Value.Trim();
				var collectionDate = results.First(result => result.Attribute("column")!.Value == "Date").Value.Trim();

				var binDay = new BinDay
				{
					Date = DateUtilities.ParseDateExact(collectionDate, "dd/MM/yyyy HH:mm:ss"),
					Address = address,
					Bins = ProcessingUtilities.GetMatchingBins(_binTypes, service),
				};

				binDays.Add(binDay);
			}

			var response = new GetBinDaysResponse
			{
				BinDays = ProcessingUtilities.ProcessBinDays(binDays),
			};

			return response;
		}

		throw new InvalidOperationException("Invalid client-side request.");
	}

	/// <summary>
	/// Creates the initial client-side request used to start the session.
	/// </summary>
	private static ClientSideRequest CreateInitialRequest()
	{
		var clientSideRequest = new ClientSideRequest
		{
			RequestId = 1,
			Url = "https://my.midlothian.gov.uk/service/Bin_Collection_Dates",
			Method = "GET",
		};

		return clientSideRequest;
	}

	/// <summary>
	/// Builds the metadata dictionary and prepares the next reference request.
	/// </summary>
	private static ClientSideRequest CreateNextRefRequest(ClientSideResponse clientSideResponse)
	{
		var setCookieHeader = clientSideResponse.Headers["set-cookie"];
		var cookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(setCookieHeader);

		var metadata = new Dictionary<string, string>
		{
			{ "cookie", cookies },
			{ "sid", SidRegex().Match(clientSideResponse.Content).Groups["sid"].Value },
		};

		var clientSideRequest = new ClientSideRequest
		{
			RequestId = 2,
			Url = $"{_baseUrl}/api/nextref?sid={metadata["sid"]}",
			Method = "GET",
			Headers = new()
			{
				{ "user-agent", Constants.UserAgent },
				{ "cookie", metadata["cookie"] },
				{ "x-requested-with", Constants.XmlHttpRequest },
			},
			Options = new ClientSideOptions
			{
				Metadata = metadata,
			},
		};

		return clientSideRequest;
	}

	/// <summary>
	/// Adds reference and CSRF tokens to the metadata dictionary.
	/// </summary>
	private static Dictionary<string, string> BuildMetadataWithTokens(ClientSideResponse clientSideResponse)
	{
		var metadata = new Dictionary<string, string>(clientSideResponse.Options.Metadata);

		using var jsonDoc = JsonDocument.Parse(clientSideResponse.Content);
		metadata["reference"] = jsonDoc.RootElement.GetProperty("data").GetProperty("reference").GetString()!;

		return metadata;
	}

	/// <summary>
	/// Creates a client-side request for the AchieveForms lookup.
	/// </summary>
	private static ClientSideRequest CreateLookupRequest(
		int requestId,
		string lookupId,
		string requestBody,
		Dictionary<string, string> metadata)
	{
		var clientSideRequest = new ClientSideRequest
		{
			RequestId = requestId,
			Url = $"{_baseUrl}/apibroker/runLookup?id={lookupId}",
			Method = "POST",
			Headers = new()
			{
				{ "user-agent", Constants.UserAgent },
				{ "content-type", Constants.ApplicationJson },
				{ "cookie", metadata["cookie"] },
				{ "x-requested-with", Constants.XmlHttpRequest },
			},
			Body = requestBody,
			Options = new ClientSideOptions
			{
				Metadata = metadata,
			},
		};

		return clientSideRequest;
	}

	/// <summary>
	/// Builds the JSON payload for the AchieveForms lookup request.
	/// </summary>
	private static string BuildLookupRequestBody(
		string postcode,
		string uprn,
		string reference)
	{
		var fromDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

		var requestBody = $$"""
		{
			"formValues": {
				"Section 1": {
					"postcode": { "value": "{{postcode}}" },
					"uprn": { "value": "{{uprn}}" },
					"fromDate": { "value": "{{fromDate}}" }
				}
			},
			"reference": "{{reference}}"
		}
		""";

		return requestBody;
	}
}
