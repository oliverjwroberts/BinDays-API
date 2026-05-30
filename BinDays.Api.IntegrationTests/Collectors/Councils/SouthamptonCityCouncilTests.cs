namespace BinDays.Api.IntegrationTests.Collectors.Councils;

using BinDays.Api.Collectors.Collectors.Councils;
using BinDays.Api.Collectors.Models;
using BinDays.Api.IntegrationTests.Helpers;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

public class SouthamptonCityCouncilTests
{
	private readonly IntegrationTestClient _client;
	private readonly ITestOutputHelper _outputHelper;
	private static readonly string _govUkId = new SouthamptonCityCouncil().GovUkId;

	public SouthamptonCityCouncilTests(ITestOutputHelper outputHelper)
	{
		_outputHelper = outputHelper;
		_client = new IntegrationTestClient(outputHelper);
	}

	/// <summary>
	/// Tests GetBinDays directly using a known UPRN. The waste calendar endpoint is not
	/// Incapsula-protected, so this test is reliable in all environments. GetAddresses
	/// requires the Incapsula-protected collections page and is therefore only reliable
	/// on real mobile devices where the TLS fingerprint is not flagged.
	/// </summary>
	[Theory]
	[InlineData("SO15 5NR", "100060691045")]
	[InlineData("SO19 7GX", "100060723360")]
	public async Task GetBinDaysTest(string postcode, string uprn)
	{
		var response = await _client.ExecuteRequestCycleAsync<GetBinDaysResponse>(
			$"/{_govUkId}/bin-days?postcode={postcode}&uid={uprn}",
			resp => resp.NextClientSideRequest
		);

		TestValidation.ValidateBinDaysResult(
			response.BinDays,
			ensureBinsPresent: true,
			ensureFutureDates: true,
			ensureSortedByDate: true
		);
	}
}
