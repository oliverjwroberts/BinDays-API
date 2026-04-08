namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Buckinghamshire Council.
/// </summary>
internal sealed partial class BuckinghamshireCouncil : ITouchVisionCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Buckinghamshire Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.buckinghamshire.gov.uk/waste-and-recycling/find-out-when-its-your-bin-collection/");

	/// <inheritdoc/>
	public override string GovUkId => "buckinghamshire";

	/// <inheritdoc/>
	protected override int ClientId => 152;

	/// <inheritdoc/>
	protected override int CouncilId => 34505;

	/// <inheritdoc/>
	protected override string ApiBaseUrl => "https://itouchvision.app/portal/itouchvision/";

	/// <inheritdoc/>
	protected override IReadOnlyCollection<Bin> BinTypes => _northBinTypes;

	/// <summary>
	/// North Buckinghamshire Council (Aylesbury Vale) bin types.
	/// </summary>
	private static readonly IReadOnlyCollection<Bin> _northBinTypes = [
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Green,
			Keys = [ "Food waste" ],
			Type = BinType.Caddy,
		},
		new()
		{
			Name = "Mixed Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Mixed recycling" ],
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Brown,
			Keys = [ "Garden waste" ],
		},
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Green,
			Keys = [ "General waste" ],
		},
	];

	/// <summary>
	/// South Buckinghamshire Council (Chiltern, South Bucks, Wycombe) bin types.
	/// </summary>
	private static readonly IReadOnlyCollection<Bin> _southBinTypes = [
		new()
		{
			Name = "Food Waste",
			Colour = BinColour.Brown,
			Keys = [ "Food waste" ],
			Type = BinType.Caddy,
		},
		new()
		{
			Name = "Mixed Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Mixed recycling" ],
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Green,
			Keys = [ "Garden waste" ],
		},
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Black,
			Keys = [ "General waste" ],
		},
		new()
		{
			Name = "Paper and Cardboard",
			Colour = BinColour.Black,
			Keys = [ "Paper and cardboard" ],
			Type = BinType.Box,
		},
		new()
		{
			Name = "Textiles, Batteries and Electricals",
			Colour = BinColour.White,
			Keys = [ "Textiles/Batteries/Electricals" ],
			Type = BinType.Bag,
		},
	];

	/// <inheritdoc/>
	protected override IReadOnlyCollection<Bin> GetBinTypes(Address address)
	{
		// Aylesbury Vale (North) consistently uses 9-digit UPRNs.
		// South areas (Chiltern, South Bucks, Wycombe) consistently use 11 or 12 digit UPRNs.
		return address.Uid!.Length > 9 ? _southBinTypes : _northBinTypes;
	}

	/// <summary>
	/// Regex for parsing bank holiday revision table rows on the council website.
	/// Matches a pair of table cells: the normal collection date and the revised collection date.
	/// </summary>
	[GeneratedRegex(@"<td>\s*\w+ (?<originalDay>\d{1,2}) (?<originalMonth>\w+).*?</td>.*?<td>\s*\w+ (?<revisedDay>\d{1,2}) (?<revisedMonth>\w+)", RegexOptions.Singleline)]
	private static partial Regex RevisionTableRegex();

	/// <inheritdoc/>
	GetBinDaysResponse ICollector.GetBinDays(Address address, ClientSideResponse? clientSideResponse)
	{
		// Step 1: Fetch the council website to check for bank holiday date revisions
		if (clientSideResponse == null)
		{
			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 1,
				Url = "https://www.buckinghamshire.gov.uk/waste-and-recycling/bin-collections/find-out-when-its-your-bin-collection/",
				Method = "GET",
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest
			};

			return getBinDaysResponse;
		}
		// Step 1 response: parse revision table, then issue the iTouch Vision request
		else if (clientSideResponse.RequestId == 1)
		{
			var revisions = ParseRevisions(clientSideResponse.Content);
			var revisionsJson = JsonSerializer.Serialize(revisions);

			// Reuse the base class to build the iTouch Vision request, then re-issue it as request ID 2
			var itouchRequest = base.GetBinDays(address, null).NextClientSideRequest!;

			var clientSideRequest = new ClientSideRequest
			{
				RequestId = 2,
				Url = itouchRequest.Url,
				Method = itouchRequest.Method,
				Headers = itouchRequest.Headers,
				Options = new ClientSideOptions
				{
					Metadata = { { "revisions", revisionsJson } },
				},
			};

			var getBinDaysResponse = new GetBinDaysResponse
			{
				NextClientSideRequest = clientSideRequest
			};

			return getBinDaysResponse;
		}
		// Step 2 response: process iTouch Vision bin days and apply bank holiday revisions
		else if (clientSideResponse.RequestId == 2)
		{
			var revisions = JsonSerializer.Deserialize<Dictionary<string, string>>(
				clientSideResponse.Options.Metadata["revisions"])!;

			// Present the response to the base class as request ID 1 so it can decrypt and parse it
			var virtualResponse = new ClientSideResponse
			{
				RequestId = 1,
				StatusCode = clientSideResponse.StatusCode,
				Headers = clientSideResponse.Headers,
				Content = clientSideResponse.Content,
				ReasonPhrase = clientSideResponse.ReasonPhrase,
			};

			var baseBinDaysResponse = base.GetBinDays(address, virtualResponse);

			if (revisions.Count == 0)
			{
				return baseBinDaysResponse;
			}

			var binDays = new List<BinDay>();

			// Iterate through each bin day, and apply any bank holiday date revisions
			foreach (var binDay in baseBinDaysResponse.BinDays!)
			{
				if (!revisions.TryGetValue(binDay.Date.ToString("yyyy-MM-dd"), out var revisedDateStr))
				{
					binDays.Add(binDay);
					continue;
				}

				binDays.Add(new BinDay
				{
					Date = DateOnly.ParseExact(revisedDateStr, "yyyy-MM-dd"),
					Address = binDay.Address,
					Bins = binDay.Bins,
				});
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
	/// Parses the bank holiday revision table from the council website HTML.
	/// Returns a dictionary mapping original dates (yyyy-MM-dd) to revised dates (yyyy-MM-dd).
	/// </summary>
	private static Dictionary<string, string> ParseRevisions(string html)
	{
		var revisions = new Dictionary<string, string>();

		// Iterate through each revision table row, and build the date revision mapping
		foreach (Match match in RevisionTableRegex().Matches(html)!)
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
