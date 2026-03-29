using BinDays.Api.Initialisation;
using Scalar.AspNetCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Use Autofac as the service provider factory
builder.Host.UseServiceProviderFactory(new Autofac.Extensions.DependencyInjection.AutofacServiceProviderFactory());

// Register services directly with Autofac using the ConfigureContainer method
builder.Host.ConfigureContainer<Autofac.ContainerBuilder>(BinDays.Api.Initialisation.DependencyInjection.ConfigureContainer);

var redis = builder.Configuration.GetValue<string>("Redis");

builder.Services.AddControllers(options =>
{
	if (string.IsNullOrEmpty(redis))
	{
		options.Conventions.Add(new ExcludeCacheControllerConvention());
	}
});

// Add caching for responses, either in-memory or Redis
if (!string.IsNullOrEmpty(redis))
{
	var multiplexer = ConnectionMultiplexer.Connect(redis);
	builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);

	builder.Services.AddStackExchangeRedisCache(options =>
	{
		options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(multiplexer);
	});
}
else
{
	builder.Services.AddDistributedMemoryCache();
}

// Health check for monitoring
builder.Services.AddHealthChecks();

// Configure Seq logging (optional)
builder.Services.AddLogging(loggingBuilder =>
{
	loggingBuilder.AddSeq(builder.Configuration.GetSection("Seq"));
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
	options.WithOperationTitleSource(OperationTitleSource.Path);
});

app.UseCors(x => x
	.AllowAnyOrigin()
	.AllowAnyMethod()
	.AllowAnyHeader()
);

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/status");

app.Run();
