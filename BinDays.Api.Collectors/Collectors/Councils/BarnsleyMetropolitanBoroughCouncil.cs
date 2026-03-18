namespace BinDays.Api.Collectors.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Vendors;
using BinDays.Api.Collectors.Models;
using BinDays.Api.Collectors.Utilities;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Collector implementation for Barnsley Metropolitan Borough Council.
/// </summary>
internal sealed partial class BarnsleyMetropolitanBoroughCouncil : GovUkCollectorBase, ICollector
{
	/// <inheritdoc/>
	public string Name => "Barnsley Metropolitan Borough Council";

	/// <inheritdoc/>
	public Uri WebsiteUrl => new("https://www.barnsley.gov.uk/");

	/// <inheritdoc/>
	public override string GovUkId => "barnsley";

	/// <summary>
	/// The list of bin types for this collector.
	/// </summary>
	private readonly IReadOnlyCollection<Bin> _binTypes =
	[
		new()
		{
			Name = "General Waste",
			Colour = BinColour.Grey,
			Keys = [ "Grey" ],
		},
		new()
		{
			Name = "Paper Recycling",
			Colour = BinColour.Blue,
			Keys = [ "Blue" ],
		},
		new()
		{
			Name = "Glass and Plastic Recycling",
			Colour = BinColour.Brown,
			Keys = [ "Brown" ],
		},
		new()
		{
			Name = "Garden Waste",
			Colour = BinColour.Green,
			Keys = [ "Green" ],
		},
	];

	/// <summary>
	/// The base URL for the waste collection site.
	/// </summary>
	private const string BaseUrl = "https://waste.barnsley.gov.uk/ViewCollection";

	/// <summary>
	/// The URL for the initial session setup page.
	/// </summary>
	private const string BeforeYouBeginUrl = $"{BaseUrl}/BeforeYouBegin";

	/// <summary>
	/// The URL for the address selection page.
	/// </summary>
	private const string SelectAddressUrl = $"{BaseUrl}/SelectAddress";

	/// <summary>
	/// The URL for the collections results page.
	/// </summary>
	private const string CollectionsUrl = $"{BaseUrl}/Collections";

	/// <summary>
	/// Regex for the RequestVerificationToken.
	/// </summary>
	[GeneratedRegex(@"<input name=""__RequestVerificationToken"" type=""hidden"" value=""(?<token>[^""]+)"" />")]
	private static partial Regex TokenRegex();

	/// <summary>
	/// Regex for the postcode value from the hidden input.
	/// </summary>
	[GeneratedRegex(@"name=""personInfo\.person1\.Postcode""[^>]*value=""(?<postcode>[^""]*)""")]
	private static partial Regex PostcodeRegex();

	/// <summary>
	/// Regex for parsing addresses from option elements.
	/// </summary>
	[GeneratedRegex(@"<option value=""(?<uid>[^""]+)"">(?<address>[^<]+)</option>")]
	private static partial Regex AddressRegex();

	/// <summary>
	/// Regex for parsing the next bin collection section.
	/// </summary>
	[GeneratedRegex(@"Your next bin collection\s*</p>\s*<p><em class=""ui-bin-next-date"">(?<date>[^<]+)</em></p>\s*<p class=""ui-bin-next-type"">\s*(?<bins>[^<]+)</p>", RegexOptions.Singleline)]
	private static partial Regex NextCollectionRegex();

	/// <summary>
	/// Regex for parsing bin collection rows from tables.
	/// </summary>
	[GeneratedRegex(@"<tr>\s*<td>\s*(?<date>[A-Za-z]+,\s+[A-Za-z]+\s+\d{1,2},\s+\d{4})\s*</td>\s*<td>\s*(?<bins>[^<]+)</td>\s*</tr>", RegexOptions.Singleline)]
	private static partial Regex BinRowRegex();

	/// <inheritdoc/>
	public GetAddressesResponse GetAddresses(string postcode, ClientSideResponse? clientSideResponse)
	{
		// Step 1: GET BeforeYouBegin to establish session cookie
		if (clientSideResponse == null)
		{
			return new GetAddressesResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 1,
					Url = BeforeYouBeginUrl,
					Method = "GET",
					Options = new ClientSideOptions
					{
						FollowRedirects = false,
					},
				},
			};
		}
		// Step 2: GET SelectAddress with session cookie to get form + token
		else if (clientSideResponse.RequestId == 1)
		{
			var requestCookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(clientSideResponse.Headers["set-cookie"]);

			return new GetAddressesResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 2,
					Url = SelectAddressUrl,
					Method = "GET",
					Headers = new()
					{
						{ "user-agent", Constants.UserAgent },
						{ "cookie", requestCookies },
					},
					Options = new ClientSideOptions
					{
						Metadata =
						{
							{ "cookie", requestCookies },
						},
					},
				},
			};
		}
		// Step 3: POST address search form (PRG pattern, no redirect follow)
		else if (clientSideResponse.RequestId == 2)
		{
			var token = TokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;
			var requestCookies = clientSideResponse.Options.Metadata["cookie"];

			return new GetAddressesResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 3,
					Url = SelectAddressUrl,
					Method = "POST",
					Headers = new()
					{
						{ "user-agent", Constants.UserAgent },
						{ "content-type", Constants.FormUrlEncoded },
						{ "cookie", requestCookies },
					},
					Body = ProcessingUtilities.ConvertDictionaryToFormData(new()
					{
						{ "personInfo.person1.HouseNumberOrName", string.Empty },
						{ "personInfo.person1.Postcode", postcode },
						{ "person1_FindAddress", "Find address" },
						{ "__RequestVerificationToken", token },
					}),
					Options = new ClientSideOptions
					{
						FollowRedirects = false,
						Metadata =
						{
							{ "cookie", requestCookies },
						},
					},
				},
			};
		}
		// Step 4: Follow POST redirect to get addresses page
		else if (clientSideResponse.RequestId == 3)
		{
			var requestCookies = clientSideResponse.Options.Metadata["cookie"];

			return new GetAddressesResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 4,
					Url = SelectAddressUrl,
					Method = "GET",
					Headers = new()
					{
						{ "user-agent", Constants.UserAgent },
						{ "cookie", requestCookies },
					},
				},
			};
		}
		// Step 5: Parse addresses from the response
		else if (clientSideResponse.RequestId == 4)
		{
			var rawAddresses = AddressRegex().Matches(clientSideResponse.Content)!;

			var addresses = new List<Address>();
			foreach (Match rawAddress in rawAddresses)
			{
				var uid = rawAddress.Groups["uid"].Value;

				if (string.IsNullOrWhiteSpace(uid))
				{
					continue;
				}

				addresses.Add(new Address
				{
					Property = rawAddress.Groups["address"].Value.Trim(),
					Postcode = postcode,
					Uid = uid,
				});
			}

			return new GetAddressesResponse
			{
				Addresses = [.. addresses],
			};
		}

		throw new InvalidOperationException("Invalid client-side request.");
	}

	/// <inheritdoc/>
	public GetBinDaysResponse GetBinDays(Address address, ClientSideResponse? clientSideResponse)
	{
		// Step 1: GET BeforeYouBegin to establish session cookie
		if (clientSideResponse == null)
		{
			return new GetBinDaysResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 1,
					Url = BeforeYouBeginUrl,
					Method = "GET",
					Options = new ClientSideOptions
					{
						FollowRedirects = false,
					},
				},
			};
		}
		// Step 2: GET SelectAddress with session cookie to get form + token
		else if (clientSideResponse.RequestId == 1)
		{
			var requestCookies = ProcessingUtilities.ParseSetCookieHeaderForRequestCookie(clientSideResponse.Headers["set-cookie"]);

			return new GetBinDaysResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 2,
					Url = SelectAddressUrl,
					Method = "GET",
					Headers = new()
					{
						{ "user-agent", Constants.UserAgent },
						{ "cookie", requestCookies },
					},
					Options = new ClientSideOptions
					{
						Metadata =
						{
							{ "cookie", requestCookies },
						},
					},
				},
			};
		}
		// Step 3: POST address search form (PRG pattern, no redirect follow)
		else if (clientSideResponse.RequestId == 2)
		{
			var token = TokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;
			var requestCookies = clientSideResponse.Options.Metadata["cookie"];

			return new GetBinDaysResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 3,
					Url = SelectAddressUrl,
					Method = "POST",
					Headers = new()
					{
						{ "user-agent", Constants.UserAgent },
						{ "content-type", Constants.FormUrlEncoded },
						{ "cookie", requestCookies },
					},
					Body = ProcessingUtilities.ConvertDictionaryToFormData(new()
					{
						{ "personInfo.person1.HouseNumberOrName", string.Empty },
						{ "personInfo.person1.Postcode", address.Postcode! },
						{ "person1_FindAddress", "Find address" },
						{ "__RequestVerificationToken", token },
					}),
					Options = new ClientSideOptions
					{
						FollowRedirects = false,
						Metadata =
						{
							{ "cookie", requestCookies },
						},
					},
				},
			};
		}
		// Step 4: Follow POST redirect to get addresses page with token
		else if (clientSideResponse.RequestId == 3)
		{
			var requestCookies = clientSideResponse.Options.Metadata["cookie"];

			return new GetBinDaysResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 4,
					Url = SelectAddressUrl,
					Method = "GET",
					Headers = new()
					{
						{ "user-agent", Constants.UserAgent },
						{ "cookie", requestCookies },
					},
					Options = new ClientSideOptions
					{
						Metadata =
						{
							{ "cookie", requestCookies },
						},
					},
				},
			};
		}
		// Step 5: POST to select the address (PRG pattern, no redirect follow)
		else if (clientSideResponse.RequestId == 4)
		{
			var token = TokenRegex().Match(clientSideResponse.Content).Groups["token"].Value;
			var requestCookies = clientSideResponse.Options.Metadata["cookie"];
			var postcodeValue = PostcodeRegex().Match(clientSideResponse.Content).Groups["postcode"].Value;

			return new GetBinDaysResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 5,
					Url = SelectAddressUrl,
					Method = "POST",
					Headers = new()
					{
						{ "user-agent", Constants.UserAgent },
						{ "content-type", Constants.FormUrlEncoded },
						{ "cookie", requestCookies },
					},
					Body = ProcessingUtilities.ConvertDictionaryToFormData(new()
					{
						{ "personInfo.person1.HouseNumberOrName", string.Empty },
						{ "personInfo.person1.Postcode", postcodeValue },
						{ "personInfo.person1.UPRN", address.Uid! },
						{ "person1_SelectAddress", "Select address" },
						{ "__RequestVerificationToken", token },
					}),
					Options = new ClientSideOptions
					{
						FollowRedirects = false,
						Metadata =
						{
							{ "cookie", requestCookies },
						},
					},
				},
			};
		}
		// Step 6: Follow POST redirect to the collections page
		else if (clientSideResponse.RequestId == 5)
		{
			var requestCookies = clientSideResponse.Options.Metadata["cookie"];

			return new GetBinDaysResponse
			{
				NextClientSideRequest = new ClientSideRequest
				{
					RequestId = 6,
					Url = CollectionsUrl,
					Method = "GET",
					Headers = new()
					{
						{ "user-agent", Constants.UserAgent },
						{ "cookie", requestCookies },
					},
				},
			};
		}
		// Step 7: Parse bin days from the collections page
		else if (clientSideResponse.RequestId == 6)
		{
			var binDays = new List<BinDay>();

			var nextCollectionMatch = NextCollectionRegex().Match(clientSideResponse.Content);
			if (nextCollectionMatch.Success)
			{
				var date = ParseCollectionDate(nextCollectionMatch.Groups["date"].Value);
				var bins = ProcessingUtilities.GetMatchingBins(_binTypes, nextCollectionMatch.Groups["bins"].Value.Trim());

				binDays.Add(new BinDay
				{
					Date = date,
					Address = address,
					Bins = bins,
				});
			}

			var binRows = BinRowRegex().Matches(clientSideResponse.Content)!;

			foreach (Match binRow in binRows)
			{
				var date = ParseCollectionDate(binRow.Groups["date"].Value);
				var bins = ProcessingUtilities.GetMatchingBins(_binTypes, binRow.Groups["bins"].Value.Trim());

				binDays.Add(new BinDay
				{
					Date = date,
					Address = address,
					Bins = bins,
				});
			}

			return new GetBinDaysResponse
			{
				BinDays = ProcessingUtilities.ProcessBinDays(binDays),
			};
		}

		throw new InvalidOperationException("Invalid client-side request.");
	}

	/// <summary>
	/// Parses collection date strings, handling relative terms when present.
	/// </summary>
	private static DateOnly ParseCollectionDate(string value)
	{
		var dateString = value.Trim();

		if (string.Equals(dateString, "Today", StringComparison.OrdinalIgnoreCase))
		{
			return DateOnly.FromDateTime(DateTime.UtcNow);
		}

		if (string.Equals(dateString, "Tomorrow", StringComparison.OrdinalIgnoreCase))
		{
			return DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
		}

		return DateUtilities.ParseDateExact(dateString, "dddd, MMMM d, yyyy");
	}
}
