namespace BinDays.Api.Extensions;

using Microsoft.Extensions.Logging;

/// <summary>
/// Extension methods for <see cref="ILogger"/> to support fluent structured data attachment.
/// </summary>
public static class LoggerExtensions
{
	/// <summary>
	/// Begins a fluent chain for attaching structured data properties to a log event.
	/// </summary>
	public static LoggerData WithData(this ILogger logger, string key, object? value)
		=> new LoggerData(logger).WithData(key, value);

	/// <summary>
	/// Begins a fluent chain for attaching a JSON-serialised structured data property to a log event.
	/// </summary>
	public static LoggerData WithJsonData(this ILogger logger, string key, object? value)
		=> new LoggerData(logger).WithJsonData(key, value);
}
