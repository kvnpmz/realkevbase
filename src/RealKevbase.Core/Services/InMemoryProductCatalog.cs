using RealKevbase.Core.Models;

namespace RealKevbase.Core.Services;

/// <summary>
/// In-memory product catalog used until the product data is moved into a database.
/// </summary>
/// <remarks>
/// The identifiers mirror the storefront catalog rendered by the SPA so that a cart built
/// in the browser validates end to end. Every entry is validated on construction, which
/// makes a misconfigured product (for example a PDF without a blob name) fail at startup
/// rather than during checkout.
/// </remarks>
public sealed class InMemoryProductCatalog : IProductCatalog
{
    private static readonly Product[] Seed =
    [
        new()
        {
            Id = Guid.Parse("a76057dc-170b-547f-8793-bb6b02c70f22"),
            Name = "Baby's 150 First Foods",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/150first-foods.pdf"
        },
        new()
        {
            Id = Guid.Parse("45531eb6-0be8-5b97-86dd-710b271bde70"),
            Name = "First Apartment Checklist",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/apartment-checklist.pdf"
        },
        new()
        {
            Id = Guid.Parse("d0e1fe4f-9b25-52db-89ae-e971cb68ad07"),
            Name = "New Apartment Checklist (Grid)",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/apartment-checklist-grid.pdf"
        },
        new()
        {
            Id = Guid.Parse("79568cf0-14af-54c8-b576-523c195dc799"),
            Name = "Baby Registry List",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/baby-registrylist.pdf"
        },
        new()
        {
            Id = Guid.Parse("f4c24033-d4db-571b-a703-5c5c2b9e615e"),
            Name = "Baby Registry Checklist - Light Blue",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/baby-registrylist-lightblue.pdf"
        },
        new()
        {
            Id = Guid.Parse("f4d264d5-5e0a-559c-8635-396aac33e22d"),
            Name = "Monthly Bill Tracker",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/bill-tracker.pdf"
        },
        new()
        {
            Id = Guid.Parse("7cfc9806-9705-520d-a553-f61a12fc909e"),
            Name = "Blood Sugar Log",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/bloodsugar-log.pdf"
        },
        new()
        {
            Id = Guid.Parse("c034f9ca-8086-5f11-8468-2f1f81b32b53"),
            Name = "Budget Planner",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/budget-planner.pdf"
        },
        new()
        {
            Id = Guid.Parse("2d7690e6-c133-5a77-9484-c3c7158e811d"),
            Name = "CKD Stage 3 Food List",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/ckd-stage3-food-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("63d4bfba-71e1-5093-af99-d223d7a45650"),
            Name = "Cleaning Planner",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/cleaning-planner.pdf"
        },
        new()
        {
            Id = Guid.Parse("c1f641af-65f7-56e2-bfe3-507d4f733025"),
            Name = "Cleaning Planner - Pink",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/cleaning-planner-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("a074a908-1801-5e02-8e43-899706901998"),
            Name = "Diverticulitis Food Chart",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/diverticulitis-foodchart.pdf"
        },
        new()
        {
            Id = Guid.Parse("06d8772d-5948-51fe-9833-1291c654a20d"),
            Name = "Diverticulitis Food Chart - Alternative",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/diverticulitis-foodchart-alternative.pdf"
        },
        new()
        {
            Id = Guid.Parse("37b475bd-f4c8-5abc-9da3-69f078916500"),
            Name = "Diverticulitis Flare-Up Food List",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/diverticulitis-foodlist.pdf"
        },
        new()
        {
            Id = Guid.Parse("a2e6b2c8-8501-581b-becc-2a67398563a8"),
            Name = "Diverticulitis Flare-Up Food List - Regal",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/diverticulitis-foodlist-regal.pdf"
        },
        new()
        {
            Id = Guid.Parse("2b99e9ad-5490-52e4-a094-239f88f8bcb9"),
            Name = "College Dorm Checklist",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/dorm-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("db2a2f20-31d9-5f7c-8538-e32a8d9e8074"),
            Name = "College Dorm Checklist - Pink",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/dorm-list-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("9c300eda-7ecf-5693-9796-497cb5b054af"),
            Name = "72-Hour Emergency Plan",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/emergency-72hour-plan.pdf"
        },
        new()
        {
            Id = Guid.Parse("bb08174c-8c1f-58bb-bf36-0baf1e5422f1"),
            Name = "Emergency Food List",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/emergency-foodlist.pdf"
        },
        new()
        {
            Id = Guid.Parse("0379d248-a229-5305-a47b-f4557fdafca5"),
            Name = "Emergency Food List - Teal",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/emergency-foodlist-teal.pdf"
        },
        new()
        {
            Id = Guid.Parse("18945289-61b6-5159-80aa-6e7eae433f0d"),
            Name = "Fatty Liver Food List",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/fatty-liver-food-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("4c104954-0a46-51db-8262-7f5db25c447d"),
            Name = "Fatty Liver Food List - Eat / Limit",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/fattyliver-foodlist.pdf"
        },
        new()
        {
            Id = Guid.Parse("0f3ee9fc-9d76-5185-aed0-fa67817b91e8"),
            Name = "Fatty Liver Food List - Alternative",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/fattyliver-foodlist-alternative.pdf"
        },
        new()
        {
            Id = Guid.Parse("d4caf8e3-ca74-5876-8c2e-2a2ac8b21a0c"),
            Name = "First Apartment Essentials",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/first-apartment.pdf"
        },
        new()
        {
            Id = Guid.Parse("7738b126-7165-513d-a89e-33d6d3787790"),
            Name = "First Apartment Essentials - Blue",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/first-apartment-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("d42f1395-4b11-52d1-bb94-f3f045362ce9"),
            Name = "First Apartment Essentials - Green",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/first-apartment-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("52e548ae-fada-5a9e-a9a8-7b5b3dde39b9"),
            Name = "First Apartment Essentials - Pink",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/first-apartment-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("6e98d208-5bbe-5b72-8658-82103808b932"),
            Name = "First Apartment Essentials - Plum",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/first-apartment-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("e9d66e3e-bd43-5699-a955-3eae82709691"),
            Name = "First Apartment Essentials - Purple",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/first-apartment-purple.pdf"
        },
        new()
        {
            Id = Guid.Parse("f1910970-0875-507b-a3cf-aa1fb67ec983"),
            Name = "Folic Acid Rich Food List - Blue",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/folicacid-foodlist-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("fccb3413-4fb2-53db-b846-e50a4138c627"),
            Name = "Folic Acid Rich Food List - Green",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/folicacid-foodlist-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("4dad84a3-646a-5320-8199-9eddd4126280"),
            Name = "Folic Acid Rich Food List - Pink",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/folicacid-foodlist-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("d74fc153-d537-5987-bc9a-33f46a1784f6"),
            Name = "GLP-1 Diet Food List",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/glp1-checklist.pdf"
        },
        new()
        {
            Id = Guid.Parse("e7ef1371-59f2-50d3-a3e3-6f75f26902b2"),
            Name = "GLP-1 Diet Food List - Green",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/glp1-checklist-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("b0ea529e-c7cb-5d90-aaf0-cb49d1d17c16"),
            Name = "Grocery Checklist",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/grocery-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("c324e89c-6ffd-5a82-8765-72fe1ccf5578"),
            Name = "Grocery Checklist - Blue",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/grocery-list-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("876fe1aa-b785-5013-a947-8bd3756946c9"),
            Name = "Half-Hour Weekly Schedule",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/halfhour-weeklyschedule.pdf"
        },
        new()
        {
            Id = Guid.Parse("d658cc37-2317-578c-beae-2e3516848c5e"),
            Name = "Heart Health Food List",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/heart-health-food-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("28485458-6082-58c0-abbe-f3c294a2a474"),
            Name = "High Protein Food Guide",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/high-protein-food-guide.pdf"
        },
        new()
        {
            Id = Guid.Parse("7ca2d05e-9f0a-56e0-8387-5cfd1e9c5b60"),
            Name = "Household Essentials Checklist",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/household-checklist.pdf"
        },
        new()
        {
            Id = Guid.Parse("c2633263-6c12-576e-bdca-0e9f1e4544ef"),
            Name = "Household Essentials Checklist - Blue",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/household-checklist-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("19e7d8d5-715d-59e4-9297-3443bbabaa29"),
            Name = "Household Essentials Checklist - Pink",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/household-checklist-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("012309c0-982a-593c-b130-8cf24509f2b5"),
            Name = "Monthly Investment Goals Template",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/investment-goals.pdf"
        },
        new()
        {
            Id = Guid.Parse("e600d847-813c-58ec-b7db-65354afde0d3"),
            Name = "Monthly Investment Goals Template - Pink",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/investment-goals-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("3012c022-9d0c-5a00-826f-b043f12f414e"),
            Name = "Kidney Disease (CKD) Food List",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/kidney-checklist.pdf"
        },
        new()
        {
            Id = Guid.Parse("b6408a1a-80a6-5a97-b89e-d817bd212140"),
            Name = "Kidney Disease (CKD) Food List - Bullet",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/kidney-checklist-bullet.pdf"
        },
        new()
        {
            Id = Guid.Parse("ff53b218-d86a-56f5-8b29-ed17e9b91b0d"),
            Name = "Kidney Grocery List (Tabbed)",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/kidney-checklist-tabbed.pdf"
        },
        new()
        {
            Id = Guid.Parse("f05d66f0-b8b8-5345-bf38-32464fe1e542"),
            Name = "Kidney Grocery List - Tabbed Dark",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/kidney-checklist-tabbed-dark.pdf"
        },
        new()
        {
            Id = Guid.Parse("78841385-f263-5915-bd0c-000a3bf134c0"),
            Name = "Kidney Food List",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/kidney-foodlist.pdf"
        },
        new()
        {
            Id = Guid.Parse("e5704908-8ec4-54ad-a8ba-54be874de3ee"),
            Name = "Kidney Food List - Card Layout",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/kidney-foodlist-card.pdf"
        },
        new()
        {
            Id = Guid.Parse("3046a50d-854c-52e3-8b03-63cce75da875"),
            Name = "Low Carb Food List",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/lowcarb-foodlist.pdf"
        },
        new()
        {
            Id = Guid.Parse("b9c377e0-ad3e-5d79-ba48-54d55af0fc75"),
            Name = "Low Carb Food List - Alternative",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/lowcarb-foodlist-alternative.pdf"
        },
        new()
        {
            Id = Guid.Parse("98fc08c4-75d6-53d1-95a6-f4791af80643"),
            Name = "Low Potassium Diet Food List",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/lowpotassium-foodlist.pdf"
        },
        new()
        {
            Id = Guid.Parse("44283e65-6790-5154-b6a1-da1d29651411"),
            Name = "Low Potassium Diet Food List - Decorative",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/lowpotassium-foodlist-decorative.pdf"
        },
        new()
        {
            Id = Guid.Parse("991e4d5f-d2a7-5174-aea1-c58ba5773961"),
            Name = "Weekly Meal Plan",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/meal-plan.pdf"
        },
        new()
        {
            Id = Guid.Parse("0108d445-c20a-57e3-82f7-526d7f35b092"),
            Name = "Weekly Meal Plan - Plum",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/meal-plan-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("ff577001-fc3f-5fc5-b91c-1c0ecb594e65"),
            Name = "Monthly Budget Template",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/monthly-template.pdf"
        },
        new()
        {
            Id = Guid.Parse("bd0893a7-eb09-528a-966e-a9dc1b684301"),
            Name = "Monthly Budget Template - Green",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/monthly-template-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("1f498cd8-29be-589c-a25d-12135738a1a7"),
            Name = "Monthly Budget Template - Pink",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/monthly-template-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("36fe6431-5d6e-5eac-a8d7-5c968def6661"),
            Name = "Paycheck Budget Planner",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/paycheck-budget.pdf"
        },
        new()
        {
            Id = Guid.Parse("07745ba1-0d00-5bf8-a6cb-76b3201ed725"),
            Name = "Paycheck Budget Planner - Blue",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/paycheck-budget-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("25baa9eb-c8f9-553e-8363-ebe4867a2556"),
            Name = "Monthly Planner Grid",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/planner-grid.pdf"
        },
        new()
        {
            Id = Guid.Parse("2ba0a7ad-4c18-569f-b456-f8d093ba2775"),
            Name = "Travel Packing List",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/travel-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("4a6453b4-d7e7-5fd0-8ffc-3eac5c979691"),
            Name = "Travel Packing List - Blue",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/travel-list-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("ed71b890-939f-5dcd-8036-8dd2c740bc99"),
            Name = "Weekly Plan",
            Price = 5.00m,
            Fulfillment = FulfillmentType.DigitalPdf,
            PdfBlobName = "guides/weekly-plan.pdf"
        },
        new()
        {
            Id = Guid.Parse("9c2d4e6f-8a0b-4c1d-9e3f-5a7b9c1d3e5f"),
            Name = "RealKevbase Logo Tee",
            Price = 24.00m,
            Fulfillment = FulfillmentType.PhysicalMerch
        },
        new()
        {
            Id = Guid.Parse("d4b6c3e1-7f80-4c9d-8e1f-3a4b5c6d7e8f"),
            Name = "Heavyweight Hoodie",
            Price = 59.00m,
            Fulfillment = FulfillmentType.PhysicalMerch
        },
        new()
        {
            Id = Guid.Parse("e5c7d4f2-8091-4d0e-9f2a-4b5c6d7e8f90"),
            Name = "Studio Desk Mat",
            Price = 34.00m,
            Fulfillment = FulfillmentType.PhysicalMerch
        }
    ];

    private readonly Dictionary<Guid, Product> _products;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryProductCatalog"/> class and
    /// validates every seeded product.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when a seeded product is inconsistent.</exception>
    public InMemoryProductCatalog()
    {
        foreach (var product in Seed)
        {
            product.Validate();
        }

        _products = Seed.ToDictionary(product => product.Id);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<Product> Products => _products.Values;

    /// <inheritdoc />
    public bool TryGetProduct(Guid id, out Product product)
    {
        if (_products.TryGetValue(id, out var found))
        {
            product = found;
            return true;
        }

        product = null!;
        return false;
    }
}
