namespace BinDays.Api.Extensions;

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

/// <summary>
/// Fluent builder for attaching structured data to a log event as Seq-accessible properties.
/// Implements <see cref="ILogger"/> so standard logging extension methods can be used directly,
/// avoiding CA2254 warnings while keeping the call site clean.
/// </summary>
public sealed class LoggerData(ILogger logger) : ILogger
{
	/// <summary>
	/// Structured properties to attach to the log event via <see cref="ILogger.BeginScope{TState}"/>.
	/// </summary>
	private readonly Dictionary<string, object?> _data = [];

	/// <summary>
	/// Adds a structured property to include in the log event.
	/// </summary>
	public LoggerData WithData(string key, object? value)
	{
		_data[key] = value;
		return this;
	}

	/// <summary>
	/// Adds a structured property to include in the log event, serialised as JSON.
	/// </summary>
	public LoggerData WithJsonData(string key, object? value)
	{
		_data[key] = value is null ? null : JsonConvert.SerializeObject(value);
		return this;
	}

	/// <inheritdoc/>
	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		using (logger.BeginScope(_data))
		{
			logger.Log(logLevel, eventId, state, exception, formatter);
		}
	}

	/// <inheritdoc/>
	public bool IsEnabled(LogLevel logLevel) => logger.IsEnabled(logLevel);

	/// <inheritdoc/>
	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => logger.BeginScope(state);
}
