namespace BinDays.Api.Collectors.Utilities;

using BinDays.Api.Collectors.Models;
using System;
using System.Text.Json;

/// <summary>
/// Provides utility methods for resolving postcodes to road names via external geocoding APIs.
/// </summary>
public static class GeocodingUtilities
{
	/// <summary>
	/// Creates a client-side request to look up a postcode via postcodes.io.
	/// Parse the response with <see cref="CreateNominatimReverseGeocodeRequest"/>.
	/// </summary>
	public static ClientSideRequest CreatePostcodesIoRequest(string postcode, int requestId) =>
		new()
		{
			RequestId = requestId,
			Url = $"https://api.postcodes.io/postcodes/{Uri.EscapeDataString(postcode.Replace(" ", ""))}",
			Method = "GET",
			Headers = new()
			{
				{ "user-agent", Constants.UserAgent },
			},
		};

	/// <summary>
	/// Parses a postcodes.io response and creates a Nominatim reverse-geocode request.
	/// Parse the response with <see cref="ParseRoadName"/>.
	/// </summary>
	public static ClientSideRequest CreateNominatimReverseGeocodeRequest(string postcodesIoContent, int requestId)
	{
		using var json = JsonDocument.Parse(postcodesIoContent);
		var result = json.RootElement.GetProperty("result");
		var lat = result.GetProperty("latitude").GetDouble();
		var lon = result.GetProperty("longitude").GetDouble();

		return new()
		{
			RequestId = requestId,
			Url = $"https://nominatim.openstreetmap.org/reverse?lat={lat}&lon={lon}&format=json",
			Method = "GET",
			Headers = new()
			{
				{ "user-agent", Constants.UserAgent },
			},
		};
	}

	/// <summary>
	/// Parses the road name from a Nominatim reverse-geocode response.
	/// </summary>
	public static string ParseRoadName(string nominatimContent)
	{
		using var json = JsonDocument.Parse(nominatimContent);
		return json.RootElement.GetProperty("address").GetProperty("road").GetString()!;
	}
}
