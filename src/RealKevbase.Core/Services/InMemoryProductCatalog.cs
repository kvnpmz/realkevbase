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
            Description = "Baby's first 150 foods grouped by type, with a common-allergen checklist for starting solids.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/150first-foods.pdf"
        },
        new()
        {
            Id = Guid.Parse("45531eb6-0be8-5b97-86dd-710b271bde70"),
            Name = "First Apartment Checklist",
            Description = "A room-by-room checklist for a first apartment, from entrance and kitchen to laundry and bedroom.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/apartment-checklist.pdf"
        },
        new()
        {
            Id = Guid.Parse("d0e1fe4f-9b25-52db-89ae-e971cb68ad07"),
            Name = "New Apartment Checklist",
            Description = "A compact grid version of the new-apartment checklist, laid out for quick ticking off in the store.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/apartment-checklist-grid.pdf"
        },
        new()
        {
            Id = Guid.Parse("79568cf0-14af-54c8-b576-523c195dc799"),
            Name = "Baby Registry List",
            Description = "Nursery, feeding, travel and health picks for a baby registry, organised by room and priority.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/baby-registrylist.pdf"
        },
        new()
        {
            Id = Guid.Parse("f4c24033-d4db-571b-a703-5c5c2b9e615e"),
            Name = "Baby Registry List — Light Blue",
            Description = "The baby registry checklist in a light blue edition, organised by room and priority.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/baby-registrylist-lightblue.pdf"
        },
        new()
        {
            Id = Guid.Parse("f4d264d5-5e0a-559c-8635-396aac33e22d"),
            Name = "Bill Tracker",
            Description = "Track every recurring bill, due date and paid status on one page so nothing slips a month.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/bill-tracker.pdf"
        },
        new()
        {
            Id = Guid.Parse("7cfc9806-9705-520d-a553-f61a12fc909e"),
            Name = "Blood Sugar Log",
            Description = "A date-and-time log for recording blood sugar readings, meals and notes across the week.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/bloodsugar-log.pdf"
        },
        new()
        {
            Id = Guid.Parse("c034f9ca-8086-5f11-8468-2f1f81b32b53"),
            Name = "Budget Planner",
            Description = "A monthly income-and-expenses planner with amount columns for every line of the budget.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/budget-planner.pdf"
        },
        new()
        {
            Id = Guid.Parse("2d7690e6-c133-5a77-9484-c3c7158e811d"),
            Name = "CKD Stage 3 Food List",
            Description = "Foods to eat and limit on a stage 3 chronic kidney disease diet, with sodium guidance.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/ckd-stage3-food-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("63d4bfba-71e1-5093-af99-d223d7a45650"),
            Name = "Cleaning Planner",
            Description = "An everyday and weekly cleaning schedule you can tick off room by room.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/cleaning-planner.pdf"
        },
        new()
        {
            Id = Guid.Parse("c1f641af-65f7-56e2-bfe3-507d4f733025"),
            Name = "Cleaning Planner — Pink",
            Description = "The everyday and weekly cleaning schedule in a pink edition, ticked off room by room.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/cleaning-planner-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("a074a908-1801-5e02-8e43-899706901998"),
            Name = "Diverticulitis Food Chart",
            Description = "A one-page diverticulitis chart splitting foods to eat from foods to avoid during a flare.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/diverticulitis-foodchart.pdf"
        },
        new()
        {
            Id = Guid.Parse("06d8772d-5948-51fe-9833-1291c654a20d"),
            Name = "Diverticulitis Food Chart — Alternative",
            Description = "The diverticulitis eat-and-avoid chart in an alternative layout for the fridge door.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/diverticulitis-foodchart-alternative.pdf"
        },
        new()
        {
            Id = Guid.Parse("37b475bd-f4c8-5abc-9da3-69f078916500"),
            Name = "Diverticulitis Flare-Up Food List",
            Description = "What to eat and what to skip during a diverticulitis flare-up, grouped by food type.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/diverticulitis-foodlist.pdf"
        },
        new()
        {
            Id = Guid.Parse("a2e6b2c8-8501-581b-becc-2a67398563a8"),
            Name = "Diverticulitis Flare-Up Food List — Regal",
            Description = "The diverticulitis flare-up food list in a regal colour edition, grouped by food type.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/diverticulitis-foodlist-regal.pdf"
        },
        new()
        {
            Id = Guid.Parse("2b99e9ad-5490-52e4-a094-239f88f8bcb9"),
            Name = "College Dorm Checklist",
            Description = "Bedroom, bathroom, study and kitchen essentials for moving into a college dorm.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/dorm-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("db2a2f20-31d9-5f7c-8538-e32a8d9e8074"),
            Name = "College Dorm Checklist — Pink",
            Description = "The college dorm checklist in a pink edition, covering every room you have to furnish.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/dorm-list-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("9c300eda-7ecf-5693-9796-497cb5b054af"),
            Name = "72-Hour Emergency Plan",
            Description = "A disaster-preparedness plan covering water, food, documents and a 72-hour kit.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#ECEFF3", "#D4DAE2", "#334155"),
            PdfBlobName = "guides/emergency-72hour-plan.pdf"
        },
        new()
        {
            Id = Guid.Parse("bb08174c-8c1f-58bb-bf36-0baf1e5422f1"),
            Name = "Emergency Food List",
            Description = "A shelf-stable emergency food list covering grains, canned goods and pantry staples.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#ECEFF3", "#D4DAE2", "#334155"),
            PdfBlobName = "guides/emergency-foodlist.pdf"
        },
        new()
        {
            Id = Guid.Parse("0379d248-a229-5305-a47b-f4557fdafca5"),
            Name = "Emergency Food List — Teal",
            Description = "The shelf-stable emergency food list in a teal edition, covering pantry staples that keep.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/emergency-foodlist-teal.pdf"
        },
        new()
        {
            Id = Guid.Parse("18945289-61b6-5159-80aa-6e7eae433f0d"),
            Name = "Fatty Liver Food List",
            Description = "Foods to eat freely on a fatty liver diet, with vegetables, proteins and what to limit.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/fatty-liver-food-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("4c104954-0a46-51db-8262-7f5db25c447d"),
            Name = "Fatty Liver Food List — Eat / Limit",
            Description = "A fatty liver food list split into eat and limit columns for quick shopping decisions.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/fattyliver-foodlist.pdf"
        },
        new()
        {
            Id = Guid.Parse("0f3ee9fc-9d76-5185-aed0-fa67817b91e8"),
            Name = "Fatty Liver Food List — Alternative",
            Description = "The eat-and-limit fatty liver food list in an alternative layout with larger type.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/fattyliver-foodlist-alternative.pdf"
        },
        new()
        {
            Id = Guid.Parse("d4caf8e3-ca74-5876-8c2e-2a2ac8b21a0c"),
            Name = "First Apartment Essentials",
            Description = "Bedroom, kitchen, bathroom and cleaning essentials for a first place, room by room.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/first-apartment.pdf"
        },
        new()
        {
            Id = Guid.Parse("7738b126-7165-513d-a89e-33d6d3787790"),
            Name = "First Apartment Essentials — Blue",
            Description = "The first-apartment essentials checklist in a blue edition, organised room by room.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/first-apartment-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("d42f1395-4b11-52d1-bb94-f3f045362ce9"),
            Name = "First Apartment Essentials — Green",
            Description = "The first-apartment essentials checklist in a green edition, organised room by room.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/first-apartment-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("52e548ae-fada-5a9e-a9a8-7b5b3dde39b9"),
            Name = "First Apartment Essentials — Pink",
            Description = "The first-apartment essentials checklist in a pink edition, organised room by room.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/first-apartment-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("6e98d208-5bbe-5b72-8658-82103808b932"),
            Name = "First Apartment Essentials — Plum",
            Description = "The first-apartment essentials checklist in a plum edition, organised room by room.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/first-apartment-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("e9d66e3e-bd43-5699-a955-3eae82709691"),
            Name = "First Apartment Essentials — Purple",
            Description = "The first-apartment essentials checklist in a purple edition, organised room by room.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/first-apartment-purple.pdf"
        },
        new()
        {
            Id = Guid.Parse("f1910970-0875-507b-a3cf-aa1fb67ec983"),
            Name = "Folic Acid Rich Food List — Blue",
            Description = "Folic acid rich vegetables, fruits, grains and proteins in a blue edition.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/folicacid-foodlist-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("fccb3413-4fb2-53db-b846-e50a4138c627"),
            Name = "Folic Acid Rich Food List — Green",
            Description = "Folic acid rich vegetables, fruits, grains and proteins in a green edition.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/folicacid-foodlist-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("4dad84a3-646a-5320-8199-9eddd4126280"),
            Name = "Folic Acid Rich Food List — Pink",
            Description = "Folic acid rich vegetables, fruits, grains and proteins in a pink edition.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/folicacid-foodlist-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("d74fc153-d537-5987-bc9a-33f46a1784f6"),
            Name = "GLP-1 Diet Food List",
            Description = "Foods to eat, limit and avoid on a GLP-1 diet, from lean protein to sugary drinks.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/glp1-checklist.pdf"
        },
        new()
        {
            Id = Guid.Parse("e7ef1371-59f2-50d3-a3e3-6f75f26902b2"),
            Name = "GLP-1 Diet Food List — Green",
            Description = "The GLP-1 eat, limit and avoid food list in a green edition.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/glp1-checklist-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("b0ea529e-c7cb-5d90-aaf0-cb49d1d17c16"),
            Name = "Grocery Checklist",
            Description = "A grocery checklist sorted by supermarket aisle, from fruit and vegetables to pantry.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/grocery-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("c324e89c-6ffd-5a82-8765-72fe1ccf5578"),
            Name = "Grocery Checklist — Blue",
            Description = "The aisle-by-aisle grocery checklist in a blue edition.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/grocery-list-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("876fe1aa-b785-5013-a947-8bd3756946c9"),
            Name = "Half Hour Weekly Schedule",
            Description = "A weekly schedule with half-hour slots from 6am, so every day is planned to the block.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/halfhour-weeklyschedule.pdf"
        },
        new()
        {
            Id = Guid.Parse("d658cc37-2317-578c-beae-2e3516848c5e"),
            Name = "Heart Health Food List",
            Description = "Heart-healthy foods built around omega-3s, fibre and healthy fats, with what to limit.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/heart-health-food-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("28485458-6082-58c0-abbe-f3c294a2a474"),
            Name = "High Protein Food Guide",
            Description = "Protein per serving for meat, fish, dairy and plant sources, cooked and raw.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/high-protein-food-guide.pdf"
        },
        new()
        {
            Id = Guid.Parse("7ca2d05e-9f0a-56e0-8387-5cfd1e9c5b60"),
            Name = "Household Essentials Checklist",
            Description = "Household essentials by chore, from dishwashing and laundry to cleaning supplies.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/household-checklist.pdf"
        },
        new()
        {
            Id = Guid.Parse("c2633263-6c12-576e-bdca-0e9f1e4544ef"),
            Name = "Household Essentials Checklist — Blue",
            Description = "The household essentials checklist in a blue edition, grouped by chore.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/household-checklist-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("19e7d8d5-715d-59e4-9297-3443bbabaa29"),
            Name = "Household Essentials Checklist — Pink",
            Description = "The household essentials checklist in a pink edition, grouped by chore.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/household-checklist-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("012309c0-982a-593c-b130-8cf24509f2b5"),
            Name = "Monthly Investment Goals Template",
            Description = "A monthly template for setting investment goals, contributions and progress.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/investment-goals.pdf"
        },
        new()
        {
            Id = Guid.Parse("e600d847-813c-58ec-b7db-65354afde0d3"),
            Name = "Monthly Investment Goals Template — Pink",
            Description = "The monthly investment goals template in a pink edition.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/investment-goals-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("3012c022-9d0c-5a00-826f-b043f12f414e"),
            Name = "Kidney Disease (CKD) Food List",
            Description = "Foods to eat and limit with chronic kidney disease, starting with low-potassium vegetables.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/kidney-checklist.pdf"
        },
        new()
        {
            Id = Guid.Parse("b6408a1a-80a6-5a97-b89e-d817bd212140"),
            Name = "Kidney Disease (CKD) Food List — Bullet",
            Description = "The CKD eat-and-limit food list in a bulleted layout for quick scanning.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/kidney-checklist-bullet.pdf"
        },
        new()
        {
            Id = Guid.Parse("ff53b218-d86a-56f5-8b29-ed17e9b91b0d"),
            Name = "Kidney Grocery List — Tabbed",
            Description = "A tabbed kidney-friendly grocery list by category, from vegetables to pantry staples.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/kidney-checklist-tabbed.pdf"
        },
        new()
        {
            Id = Guid.Parse("f05d66f0-b8b8-5345-bf38-32464fe1e542"),
            Name = "Kidney Grocery List — Tabbed Dark",
            Description = "The tabbed kidney-friendly grocery list in a dark edition.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#ECEFF3", "#D4DAE2", "#334155"),
            PdfBlobName = "guides/kidney-checklist-tabbed-dark.pdf"
        },
        new()
        {
            Id = Guid.Parse("78841385-f263-5915-bd0c-000a3bf134c0"),
            Name = "Kidney Food List",
            Description = "Foods to eat freely and foods to limit on a kidney diet, split by potassium and sodium.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/kidney-foodlist.pdf"
        },
        new()
        {
            Id = Guid.Parse("e5704908-8ec4-54ad-a8ba-54be874de3ee"),
            Name = "Kidney Food List — Card",
            Description = "The kidney food list in a card layout, splitting eat-freely from limit-and-avoid.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/kidney-foodlist-card.pdf"
        },
        new()
        {
            Id = Guid.Parse("3046a50d-854c-52e3-8b03-63cce75da875"),
            Name = "Low Carb Food List",
            Description = "A traffic-light low carb list: green enjoy freely, amber eat some, red eat little.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/lowcarb-foodlist.pdf"
        },
        new()
        {
            Id = Guid.Parse("b9c377e0-ad3e-5d79-ba48-54d55af0fc75"),
            Name = "Low Carb Food List — Alternative",
            Description = "The traffic-light low carb list in an alternative layout with larger type.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/lowcarb-foodlist-alternative.pdf"
        },
        new()
        {
            Id = Guid.Parse("98fc08c4-75d6-53d1-95a6-f4791af80643"),
            Name = "Low Potassium Diet Food List",
            Description = "Foods to eat and avoid on a low potassium diet, listed by category.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/lowpotassium-foodlist.pdf"
        },
        new()
        {
            Id = Guid.Parse("44283e65-6790-5154-b6a1-da1d29651411"),
            Name = "Low Potassium Diet Food List — Decorative",
            Description = "The low potassium eat-and-avoid list in a decorative edition.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/lowpotassium-foodlist-decorative.pdf"
        },
        new()
        {
            Id = Guid.Parse("991e4d5f-d2a7-5174-aea1-c58ba5773961"),
            Name = "Weekly Meal Plan",
            Description = "A seven-day meal plan with breakfast, lunch, dinner and a shopping list column.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/meal-plan.pdf"
        },
        new()
        {
            Id = Guid.Parse("0108d445-c20a-57e3-82f7-526d7f35b092"),
            Name = "Weekly Meal Plan — Plum",
            Description = "The seven-day meal plan in a plum edition, with a shopping list column.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/meal-plan-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("ff577001-fc3f-5fc5-b91c-1c0ecb594e65"),
            Name = "Monthly Budget Template",
            Description = "A monthly budget template with total income, fixed costs and variable spending.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/monthly-template.pdf"
        },
        new()
        {
            Id = Guid.Parse("bd0893a7-eb09-528a-966e-a9dc1b684301"),
            Name = "Monthly Budget Template — Green",
            Description = "The monthly budget template in a green edition, covering income and spending.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/monthly-template-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("1f498cd8-29be-589c-a25d-12135738a1a7"),
            Name = "Monthly Budget Template — Pink",
            Description = "The monthly budget template in a pink edition, covering income and spending.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/monthly-template-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("36fe6431-5d6e-5eac-a8d7-5c968def6661"),
            Name = "Paycheck Budget",
            Description = "Budget by paycheck across twelve months, so each pay period gets its own plan.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/paycheck-budget.pdf"
        },
        new()
        {
            Id = Guid.Parse("07745ba1-0d00-5bf8-a6cb-76b3201ed725"),
            Name = "Paycheck Budget — Blue",
            Description = "The paycheck-by-paycheck budget planner in a blue edition.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/paycheck-budget-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("25baa9eb-c8f9-553e-8363-ebe4867a2556"),
            Name = "Monthly Planner Grid",
            Description = "A blank monthly grid for planning by week, with room for notes and priorities.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/planner-grid.pdf"
        },
        new()
        {
            Id = Guid.Parse("2ba0a7ad-4c18-569f-b456-f8d093ba2775"),
            Name = "Travel Packing List",
            Description = "A packing list by quantity, covering documents, financials, clothing and toiletries.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/travel-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("4a6453b4-d7e7-5fd0-8ffc-3eac5c979691"),
            Name = "Travel Packing List — Blue",
            Description = "The quantity-based packing list in a blue edition, covering every bag.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/travel-list-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("ed71b890-939f-5dcd-8036-8dd2c740bc99"),
            Name = "Weekly Plan",
            Description = "A weekly plan with a day-by-day layout for priorities, tasks and notes.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/weekly-plan.pdf"
        },
        new()
        {
            Id = Guid.Parse("9c2d4e6f-8a0b-4c1d-9e3f-5a7b9c1d3e5f"),
            Name = "RealKevbase Logo Tee",
            Description = "Heavyweight organic cotton with a minimal chest print that survives the wash cycle.",
            Price = 24.0m,
            Fulfillment = FulfillmentType.PhysicalMerch,
            Accent = new ProductAccent("#F6EFE3", "#E8D8BF", "#92400E"),
        },
        new()
        {
            Id = Guid.Parse("d4b6c3e1-7f80-4c9d-8e1f-3a4b5c6d7e8f"),
            Name = "Heavyweight Hoodie",
            Description = "Brushed fleece, boxy fit and an embroidered mark. Built for long build sessions.",
            Price = 59.0m,
            Fulfillment = FulfillmentType.PhysicalMerch,
            Accent = new ProductAccent("#ECEFF3", "#D4DAE2", "#334155"),
        },
        new()
        {
            Id = Guid.Parse("e5c7d4f2-8091-4d0e-9f2a-4b5c6d7e8f90"),
            Name = "Studio Desk Mat",
            Description = "Stitched-edge felt mat sized for a keyboard, notebook and a mug that never moves.",
            Price = 34.0m,
            Fulfillment = FulfillmentType.PhysicalMerch,
            Accent = new ProductAccent("#EEF0E4", "#D8DEC4", "#4D7C0F"),
        },
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
    /// <remarks>
    /// Returns the seed array rather than the lookup dictionary so the catalog keeps a stable,
    /// intentional display order.
    /// </remarks>
    public IReadOnlyCollection<Product> Products => Seed;

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
