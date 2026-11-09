using Microsoft.AspNetCore.HttpOverrides;
using RealKevbase.Api.Cors;
using RealKevbase.Api.Endpoints;
using RealKevbase.Api.Listings;
using RealKevbase.Api.Services;
using RealKevbase.Core.Services;

var builder = WebApplication.CreateBuilder(args);

// Register CORS service
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicies.SpaPolicyName, policy =>
    {
        // Apex domain, *.pages.dev previews and http://localhost:5173 (see CorsPolicies).
        policy.SetIsOriginAllowed(CorsPolicies.IsAllowedOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddOpenApi();
builder.Services.AddAzureBlobPdfStorage(builder.Configuration);
builder.Services.AddSingleton<IProductCatalog, InMemoryProductCatalog>();
builder.Services.AddListings(builder.Configuration);
builder.Services.AddStripeCheckout(builder.Configuration);

// Queued PDF fulfillment for paid Stripe sessions; the webhook only enqueues work.
builder.Services.AddCheckoutFulfillment(builder.Configuration);

var app = builder.Build();

// Configure Forwarded Headers and clear proxy restrictions for Azure proxy
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// REMOVED app.UseHttpsRedirection() -> Azure App Service handles SSL termination

app.UseCors(CorsPolicies.SpaPolicyName);
app.MapCheckoutEndpoints();
app.MapCatalogEndpoints();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
