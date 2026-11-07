using Microsoft.Extensions.DependencyInjection;

using RealKevbase.Core.Services;

namespace RealKevbase.Api.Services;

/// <summary>
/// Registration helpers for asynchronous checkout fulfillment.
/// </summary>
public static class CheckoutFulfillmentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the fulfillment queue, background worker and PDF issuer/delivery services.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddCheckoutFulfillment(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ICheckoutFulfillmentQueue, CheckoutFulfillmentQueue>();
        services.AddHostedService<CheckoutFulfillmentWorker>();
        services.AddSingleton<IDigitalFulfillmentService, DigitalFulfillmentService>();
        services.AddSingleton<IPdfDeliveryService, LoggingPdfDeliveryService>();

        return services;
    }
}
