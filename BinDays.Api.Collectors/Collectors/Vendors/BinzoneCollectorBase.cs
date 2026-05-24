namespace BinDays.Api.Collectors.Collectors.Vendors;

using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Base collector implementation for councils using the South and Vale BinDays API.
/// </summary>
internal abstract partial class BinzoneCollectorBase : GovUkCollectorBase
{
	/// <summary>
	/// The council code used by the BinDays API ("S" for South Oxfordshire or "V" for Vale).
	/// </summary>
	protected abstract string CouncilCode { get; }

	/// <summary>
	/// The URL of the council's waste collections calendar page, used to scrape bank holiday revision tables.
	/// </summary>
	protected abstract string CalendarUrl { get; }

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "Rubbish",
			Colour = BinColour.Black,
			Keys = [ "Non-recyclable refuse waste" ],
		},
		new()
		{
			Name = "Recycling",
			Colour = BinColour.Green,
			Keys = [ "Recycling" ],
		},
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Green,
			Keys = [ "Food waste" ],
			Type = BinType.Caddy,
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "Garden Waste subscribers" ],
		},
		new()
		{
			Name = "Small Electrical Items",
			Colour = BinColour.Grey,
			Keys = [ "Small electricals" ],
			Type = BinType.Bag,
		},
		new()
		{
			Name = "Textiles",
			Colour = BinColour.Grey,
			Keys = [ "Textiles/Clothes" ],
			Type = BinType.Bag,
		},
	];

	/// <summary>
	/// The base URL for the BinDays property API.
	/// </summary>
	private const string _propertyApiBaseUrl = "https://forms.southandvale.gov.uk/api/property";

	/// <summary>
	/// Regex for parsing bank holiday revision table rows on the council website.
	/// Matches a pair of table cells: the normal collection date and the revised collection date.
	/// </summary>
	[GeneratedRegex(@"<td[^>]*>\s*\w+ (?<originalDay>\d{1,2}) (?<originalMonth>\w+).*?</td>.*?<td[^>]*>\s*\w+ (?<revisedDay>\d{1,2}) (?<revisedMonth>\w+)", RegexOptions.Singleline)]
	private static partial Regex RevisionTableRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Prepare client-side request for getting addresses
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = $"{_propertyApiBaseUrl}/postcode/{postcode}",
				Method = "GET",
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
			using var jsonDocument = JsonDocument.Parse(clientSideResponse.Content);
			var setData = jsonDocument.RootElement.GetProperty("setData");

			// Iterate through each address, and create a new address object
			var addresses = new List<Address>();
			foreach (var addressItem in setData.EnumerateArray())
			{
				var council = addressItem.GetProperty("council").GetString()!;

				if (council != CouncilCode)
				{
					continue;
				}

				var address = new Address
				{
					Property = addressItem.GetProperty("address").GetString()!,
					Postcode = postcode,
					Uid = addressItem.GetProperty("uprn").GetString()!,
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
		// TODO: Remove once legacy UIDs are no longer in circulation. Addresses are cached both
		// server-side (30-day TTL) and client-side (indefinitely), so this may need to stay long-term.
		// Clients that cached addresses before the migration to the South and Vale JSON API still
		// hold UIDs in the legacy ebase Binzone form format (CTRL:63:_:D:N). These cannot be used
		// directly as UPRNs, so they are resolved via a postcode lookup before fetching bin days.
		if (address.Uid!.Contains(':'))
		{
			return GetLegacyBinDays(address, clientSideResponse);
		}

		// Step 1: Fetch the calendar page to check for bank holiday date revisions
		if (clientSideResponse == null)
		{
			return new GetBinDaysResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 1,
					Url = CalendarUrl,
					Method = "GET",
				},
			};
		}

		// Step 1 response: parse revision table, then issue the bin days request
		if (clientSideResponse.RequestId == 1)
		{
			var revisions = ParseRevisions(clientSideResponse.Content);
			var revisionsJson = JsonSerializer.Serialize(revisions);

			return new GetBinDaysResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 2,
					Url = $"{_propertyApiBaseUrl}/bins/{address.Uid}",
					Method = "GET",
					Options = new ClientSideOptions
					{
						Metadata = { { "revisions", revisionsJson } },
					},
				},
			};
		}

		// Step 2 response: parse bin days and apply bank holiday revisions
		if (clientSideResponse.RequestId == 2)
		{
			var revisions = JsonSerializer.Deserialize<Dictionary<string, string>>(
				clientSideResponse.Options.Metadata["revisions"])!;

			return BuildBinDaysResponse(address, clientSideResponse.Content, revisions);
		}

		throw new InvalidOperationException("Invalid client-side request.");
	}

	/// <summary>
	/// Handles bin days requests for legacy UIDs (CTRL:63:_:D:N format).
	/// Resolves the UPRN by fetching addresses for the postcode and selecting by the index encoded in the legacy UID.
	/// </summary>
	private GetBinDaysResponse GetLegacyBinDays(Address address, ClientSideResponse? clientSideResponse)
	{
		// Step 1: Fetch the calendar page to check for bank holiday date revisions
		if (clientSideResponse == null)
		{
			return new GetBinDaysResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 1,
					Url = CalendarUrl,
					Method = "GET",
				},
			};
		}

		// Step 1 response: parse revision table, then issue the postcode lookup
		if (clientSideResponse.RequestId == 1)
		{
			var revisions = ParseRevisions(clientSideResponse.Content);
			var revisionsJson = JsonSerializer.Serialize(revisions);

			return new GetBinDaysResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 2,
					Url = $"{_propertyApiBaseUrl}/postcode/{address.Postcode}",
					Method = "GET",
					Options = new ClientSideOptions
					{
						Metadata = { { "revisions", revisionsJson } },
					},
				},
			};
		}

		// Step 2 response: resolve UPRN from postcode lookup, then issue the bin days request
		if (clientSideResponse.RequestId == 2)
		{
			var uid = address.Uid!;
			var index = int.Parse(uid.AsSpan(uid.LastIndexOf(':') + 1));

			using var jsonDocument = JsonDocument.Parse(clientSideResponse.Content);
			var setData = jsonDocument.RootElement.GetProperty("setData");

			var uprn = setData.EnumerateArray()
				.Where(item => item.GetProperty("council").GetString() == CouncilCode)
				.ElementAt(index)
				.GetProperty("uprn").GetString()!;

			return new GetBinDaysResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 3,
					Url = $"{_propertyApiBaseUrl}/bins/{uprn}",
					Method = "GET",
					Options = new ClientSideOptions
					{
						Metadata = { { "revisions", clientSideResponse.Options.Metadata["revisions"] } },
					},
				},
			};
		}

		// Step 3 response: parse bin days and apply bank holiday revisions
		if (clientSideResponse.RequestId == 3)
		{
			var revisions = JsonSerializer.Deserialize<Dictionary<string, string>>(
				clientSideResponse.Options.Metadata["revisions"])!;

			return BuildBinDaysResponse(address, clientSideResponse.Content, revisions);
		}

		throw new InvalidOperationException("Invalid client-side request.");
	}

	/// <summary>
	/// Parses bin days from the Binzone API JSON response and applies bank holiday date revisions.
	/// </summary>
	private GetBinDaysResponse BuildBinDaysResponse(Address address, string content, Dictionary<string, string> revisions)
	{
		using var jsonDocument = JsonDocument.Parse(content);
		var setData = jsonDocument.RootElement.GetProperty("setData");
		var binDays = new List<BinDay>();

		if (setData.GetProperty("site").GetString()! != CouncilCode)
		{
			throw new InvalidOperationException("Address does not belong to this council.");
		}

		foreach (var week in setData.GetProperty("week").EnumerateArray())
		{
			foreach (var day in week.GetProperty("day").EnumerateArray())
			{
				var collectionDate = day.GetProperty("collection_date").GetString()!;
				var date = DateUtilities.ParseDateExact(collectionDate, "dd/MM/yyyy");

				// The Binzone API does not account for bank holidays — it always returns the
				// original scheduled collection date. Revisions are sourced from the council's
				// calendar page and applied here.
				if (revisions.TryGetValue(date.ToString("yyyy-MM-dd"), out var revisedDateStr))
				{
					date = DateUtilities.ParseDateExact(revisedDateStr, "yyyy-MM-dd");
				}

				foreach (var bin in day.GetProperty("bins").EnumerateArray())
				{
					var service = bin.GetProperty("bin_type").GetString()!;
					var matchedBins = ProcessingUtilities.GetMatchingBins(_binTypes, service);

					if (matchedBins.Count == 0)
					{
						continue;
					}

					binDays.Add(new BinDay
					{
						Date = date,
						Address = address,
						Bins = matchedBins,
					});
				}
			}
		}

		return new GetBinDaysResponse
		{
			BinDays = ProcessingUtilities.ProcessBinDays(binDays),
		};
	}

	/// <summary>
	/// Parses the bank holiday revision table from the council website HTML.
	/// Returns a dictionary mapping original dates (yyyy-MM-dd) to revised dates (yyyy-MM-dd).
	/// </summary>
	private static Dictionary<string, string> ParseRevisions(string html)
	{
		var revisions = new Dictionary<string, string>();

		foreach (Match match in RevisionTableRegex().Matches(html))
		{
			var originalDate = DateUtilities.ParseDateInferringYear(
				$"{match.Groups["originalDay"].Value} {match.Groups["originalMonth"].Value}",
				"d MMMM"
			);
			var revisedDate = DateUtilities.ParseDateInferringYear(
				$"{match.Groups["revisedDay"].Value} {match.Groups["revisedMonth"].Value}",
				"d MMMM"
			);

			revisions[$"{originalDate:yyyy-MM-dd}"] = $"{revisedDate:yyyy-MM-dd}";
		}

		return revisions;
	}
}
