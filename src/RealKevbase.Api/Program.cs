using Microsoft.AspNetCore.HttpOverrides;
using RealKevbase.Api.Cors;
using RealKevbase.Api.Endpoints;
using RealKevbase.Api.Listings;
using RealKevbase.Api.Services;
using RealKevbase.Core.Services;

var builder = WebApplication.CreateBuilder(args);

// Do not advertise the web server in responses.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Register CORS service
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicies.SpaPolicyName, policy =>
    {
        // Apex domain, this project's pages.dev previews and http://localhost:5173 (see CorsPolicies).
        policy.SetIsOriginAllowed(CorsPolicies.IsAllowedOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddOpenApi();

// The catalog is ~55 KB of JSON (~15 KB gzipped). WebP images are already compressed and
// are not in the default MIME list, so this only shrinks text responses.
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);

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

// Baseline hardening for a JSON API: no MIME sniffing, no framing, no referrer leakage,
// and HSTS so browsers never fall back to plain HTTP for this host.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// REMOVED app.UseHttpsRedirection() -> Azure App Service handles SSL termination

app.UseResponseCompression();

app.UseCors(CorsPolicies.SpaPolicyName);
app.MapCheckoutEndpoints();
app.MapCatalogEndpoints();

app.Run();
