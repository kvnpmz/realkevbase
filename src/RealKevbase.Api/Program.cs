using RealKevbase.Api.Endpoints;
using RealKevbase.Core.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Azure Blob Storage-backed PDF download links (24-hour SAS URLs), configured from the
// "AzureStorage" section or environment variables. No secrets are stored in source.
builder.Services.AddAzureBlobPdfStorage(builder.Configuration);

// Product catalog used to validate carts and resolve authoritative prices server-side.
builder.Services.AddSingleton<IProductCatalog, InMemoryProductCatalog>();

// Stripe Checkout sessions. The secret key comes from configuration, environment
// variables or user secrets; payment fulfillment happens in the async webhook.
builder.Services.AddStripeCheckout(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapCheckoutEndpoints();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
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
