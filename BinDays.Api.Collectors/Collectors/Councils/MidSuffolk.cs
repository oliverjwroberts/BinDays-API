namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Mid Suffolk District Council.
/// </summary>
internal sealed partial class MidSuffolk : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Mid Suffolk";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.midsuffolk.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "mid-suffolk";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes = [
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Black,
			Keys = [ "Refuse Collection (General Rubbish)" ],
		},
		new()
		{
			Name = "Recycling",
			Colour = BinColour.Green,
			Keys = [ "Recycling Collection" ],
		},
		new()
		{
			Name = "Paper and Card Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Paper and Card Collection (Black with Blue Lid)" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Grey,
			Keys = [ "Food Waste Collection (Grey Caddy)" ],
			Type = BinType.Caddy,
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "Garden Waste Collection (Brown Bin)" ],
		},
	];

	/// <summary>
	/// The field reference for the postcode and address value.
	/// </summary>
	private const string _addressFieldReference = "Address94394335";

	/// <summary>
	/// The field reference for the UPRN value.
	/// </summary>
	private const string _uprnFieldReference = "Text06802196";

	/// <summary>
	/// The field reference for returned collection data.
	/// </summary>
	private const string _collectionFieldReference = "CheckboxMultiple75509060";

	/// <summary>
	/// Regex for extracting the CSRF token from the page.
	/// </summary>
	[GeneratedRegex(@"p_auth=(?<token>[A-Za-z0-9]+)")]
	private static partial Regex AuthTokenRegex();

	/// <summary>
	/// Regex for extracting the page layout id.
	/// </summary>
	[GeneratedRegex(@"getPlid:\s*function\(\)\s*\{\s*return\s*'(?<plid>\d+)';")]
	private static partial Regex PLidRegex();

	/// <summary>
	/// Regex for extracting the scope group id.
	/// </summary>
	[GeneratedRegex(@"getScopeGroupId:\s*function\(\)\s*\{\s*return\s*'(?<scopeGroupId>\d+)';")]
	private static partial Regex ScopeGroupIdRegex();

	/// <summary>
	/// Regex for extracting the form portlet namespace.
	/// </summary>
	[GeneratedRegex(@"data-fm-namespace=""(?<portletNamespace>[^""]+)""")]
	private static partial Regex PortletNamespaceRegex();

	/// <summary>
	/// Regex for extracting the serialized form context object from the page script.
	/// </summary>
	[GeneratedRegex(@"render\(componentModule,\s*(?<formContext>\{\""templateNamespace\"":\""ddm\.paginated_form\""[\s\S]*?\})\s*,\s*'[^']+'\);", RegexOptions.Singleline)]
	private static partial Regex FormContextRegex();

	/// <summary>
	/// Regex for extracting the collection service and date from the returned option value.
	/// </summary>
	[GeneratedRegex(@"\bname=(?<service>[^,]+),.*?\bcollectionDate=(?<date>[A-Za-z]{3}\s+\d{1,2}\s+[A-Za-z]{3}\s+\d{4})")]
	private static partial Regex CollectionDetailsRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting addresses
		if (clientSideResponse == null)
		{
			var requestBody = $$"""
			{
			  "/placecube_digitalplace.addresscontext/search-address-by-postcode": {
			    "companyId": "1486681",
			    "postcode": "{{postcode}}",
			    "fallbackToNationalLookup": false
			  }
			}
			""";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = "https://www.midsuffolk.gov.uk/api/jsonws/invoke",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "content-type", Constants.ApplicationJson },
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
			using var jsonDoc = JsonDocument.Parse(clientSideResponse.Content);

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (var addressElement in jsonDoc.RootElement.EnumerateArray())
			{
				var address = new Address
				{
					Property = addressElement.GetProperty("fullAddress").GetString()!.Trim(),
					Postcode = postcode,
					Uid = addressElement.GetProperty("UPRN").GetString()!.Trim(),
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
		// Prepare client-side request for loading the bin collection page
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = "https://www.midsuffolk.gov.uk/check-your-collection-day",
				Method = "GET",
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest,
			};

			return getBinDaysResponse;
		}
		// Prepare client-side request for evaluating bin collection data
		else if (clientSideResponse.RequestId == 1)
		{
			var requestCookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(clientSideResponse.Headers["set-cookie"]);
			var token = AuthTokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;
			var pLid = PLidRegex().Match(clientSideResponse.Content).Groups["plid"].Value;
			var scopeGroupId = ScopeGroupIdRegex().Match(clientSideResponse.Content).Groups["scopeGroupId"].Value;
			var portletNamespace = PortletNamespaceRegex().Match(clientSideResponse.Content).Groups["portletNamespace"].Value;
			var formContext = FormContextRegex().Match(clientSideResponse.Content).Groups["formContext"].Value;

			var addressValue = JsonSerializer.Serialize(new { postcode = address.Postcode!, fullAddress = address.Property!, uprn = address.Uid! });
			var formContextJson = JsonNode.Parse(formContext)!.AsObject();
			var rows = formContextJson["pages"]![0]!["rows"]!.AsArray();

			// Iterate through each form row and update the selected address values
			foreach (var row in rows)
			{
				var columns = row!["columns"]!.AsArray();

				// Iterate through each form column and update field values needed by the evaluation trigger
				foreach (var column in columns)
				{
					var fields = column!["fields"]!.AsArray();

					// Iterate through each field and update the address and UPRN fields
					foreach (var field in fields)
					{
						var fieldObject = field!.AsObject();
						var fieldReference = fieldObject["fieldReference"]!.GetValue<string>();

						if (fieldReference == _addressFieldReference)
						{
							fieldObject["value"] = addressValue;
							fieldObject["localizedValue"]!["en_GB"] = addressValue;
							fieldObject["localizedValueEdited"] = JsonNode.Parse("{\"en_GB\":true}");
						}

						if (fieldReference == _uprnFieldReference)
						{
							fieldObject["value"] = address.Uid!;
							fieldObject["localizedValue"]!["en_GB"] = address.Uid!;
							fieldObject["localizedValueEdited"] = JsonNode.Parse("{\"en_GB\":true}");
						}
					}
				}
			}

			var serializedFormContext = formContextJson.ToJsonString();
			var boundary = "----BinDaysBoundary";
			var requestBody = $$"""
			--{{boundary}}
			Content-Disposition: form-data; name="languageId"

			en_GB
			--{{boundary}}
			Content-Disposition: form-data; name="p_auth"

			{{token}}
			--{{boundary}}
			Content-Disposition: form-data; name="p_l_id"

			{{pLid}}
			--{{boundary}}
			Content-Disposition: form-data; name="p_v_l_s_g_id"

			{{scopeGroupId}}
			--{{boundary}}
			Content-Disposition: form-data; name="portletNamespace"

			{{portletNamespace}}
			--{{boundary}}
			Content-Disposition: form-data; name="serializedFormContext"

			{{serializedFormContext}}
			--{{boundary}}
			Content-Disposition: form-data; name="trigger"

			{{_uprnFieldReference}}
			--{{boundary}}--
			""";

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = "https://www.midsuffolk.gov.uk/o/dynamic-data-mapping-form-context-provider/",
				Method = "POST",
				Headers = new()
				{
					{ "user-agent", Constants.UserAgent },
					{ "accept", Constants.ApplicationJson },
					{ "content-type", $"multipart/form-data; boundary={boundary}" },
					{ "x-csrf-token", token },
					{ "cookie", requestCookies },
				},
				Body = requestBody.ReplaceLineEndings("\r\n"),
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
			using var jsonDoc = JsonDocument.Parse(clientSideResponse.Content);
			var rows = jsonDoc.RootElement[0]
				.GetProperty("rows");
			JsonElement options = default;

			// Iterate through each response row to find the collection options field
			foreach (var row in rows.EnumerateArray())
			{
				var columns = row.GetProperty("columns");

				// Iterate through each response column and inspect fields
				foreach (var column in columns.EnumerateArray())
				{
					var fields = column.GetProperty("fields");

					// Iterate through each field and locate the collection options
					foreach (var field in fields.EnumerateArray())
					{
						var fieldReference = field.GetProperty("fieldReference").GetString()!;

						if (fieldReference == _collectionFieldReference)
						{
							options = field.GetProperty("options");
						}
					}
				}
			}

			// Iterate through each collection option, and create a new bin day object
			var binDays = new List<BinDay>();
			var seenCollections = new HashSet<string>();
			foreach (var option in options.EnumerateArray())
			{
				var value = option.GetProperty("value").GetString()!;
				var match = CollectionDetailsRegex().Match(value);
				var service = match.Groups["service"].Value.Trim();
				var collectionDate = match.Groups["date"].Value.Trim();
				var collectionKey = $"{service}|{collectionDate}";

				if (!seenCollections.Add(collectionKey))
				{
					continue;
				}

				var matchedBinTypes = ProcessingUtilities.GetMatchingBins(_binTypes, service);
				var date = DateUtilities.ParseDateExact(collectionDate, "ddd d MMM yyyy");

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
