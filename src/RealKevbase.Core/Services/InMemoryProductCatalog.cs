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
            Id = Guid.Parse("9d86df5c-23d5-5334-b5f1-a0422c889cb7"),
            Name = "Baby First Foods Checklist",
            Description = "Track every new food your baby tries, with 150 first foods grouped by type and a dot beside each common allergen, so you can check off each taste and note reactions as you go.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/baby-first-foods.pdf"
        },
        new()
        {
            Id = Guid.Parse("45531eb6-0be8-5b97-86dd-710b271bde70"),
            Name = "First Apartment Checklist",
            Description = "A room-by-room checklist of 173 things a first apartment needs, laid out in five columns that run from entrance and dining area through the kitchen knife drawer and utensil pot to the office shelf, bathroom cabinet, laundry corner and toolbox, so nothing is bought twice or discovered missing in week two. Includes the small things leases never mention, from power sockets and door mats to a toilet plunger and spare bulbs.",
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
            Description = "The same first-apartment essentials condensed into a dense grid rather than long columns, so 172 items fit on one sheet without turning into a booklet: living room, office, bathroom, kitchen prep, dining, cleaning, laundry, tools and bedroom sit side by side and can be ticked in a single pass while you walk the rooms.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/apartment-checklist-grid.pdf"
        },
        new()
        {
            Id = Guid.Parse("79568cf0-14af-54c8-b576-523c195dc799"),
            Name = "Baby Registry Checklist",
            Description = "A registry checklist of 76 items grouped by where they will actually live, from nursery and decor through bath and potty, clothing, gear and travel, nursing and feeding, health and safety, toys and learning, and a short section for the mother herself. Built to be walked through with a shop assistant or a pushchair in one hand.",
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
            Description = "A bill tracker for the recurring payments that quietly run a household: one line per bill with what it is, when it is due and whether it has been paid, so the month is checked off in one pass instead of five separate app notifications.",
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
            Description = "A blood sugar log with one line per reading and columns for date, time, level and context, kept deliberately plain so it can be filled in at the kitchen table and handed to a doctor who has five minutes to read it.",
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
            Description = "A six-column budget planner that reads like a statement of the month rather than a wish list: income rows, sixteen bill rows, savings and expenses columns, a debt column and a summary that adds up to what is actually left, with space per line instead of a single total at the bottom.",
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
            Description = "A three-column kidney diet sheet for stage 3 CKD: 40 foods to eat freely, 40 to limit and 40 to avoid, 120 in total, arranged so the same categories line up across all three columns and the swap from one column to the next is obvious at a glance rather than something you have to reason about.",
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
            Description = "A cleaning schedule that separates what happens every day from what happens on each weekday, what waits for the month and what waits for the year, 77 jobs in total across the twelve groups, so the weekly reset is a short list you can finish rather than a vague ambition.",
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
            Description = "A diverticulitis chart organised by food group instead of by verdict: whole grains, fruits and vegetables, legumes, nuts and seeds, skinless poultry and fish each get their own row, with the diverticulitis-friendly choices and the ones to hold back marked side by side, so you can judge a meal by what is actually on the plate.",
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
            Name = "Diverticulitis Food List",
            Description = "A 78-item diverticulitis list split into eat, limit and avoid: 24 foods that help, 28 that are fine in moderation and 26 that tend to trigger a flare, grouped by type so a shopping list can be built straight off the eat column without a second reference.",
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
            Name = "Dorm Room Checklist",
            Description = "A 104-item dorm list across bedroom, kitchen, study, bathroom, cleaning supplies, electronics and decor, sized for a room that has to do everything at once: somewhere to sleep, somewhere to cook, somewhere to work and somewhere to put a wet towel.",
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
            Name = "72 Hour Emergency Plan",
            Description = "A 72-hour emergency plan built around what a household actually consumes in three days: water and drinks, protein, carbs and fruit, and the tools and extras that make the rest usable, 29 line items in total with quantities rather than vague encouragement.",
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
            Description = "An 86-item emergency food list organised by aisle logic: grains and cereals, ready-to-eat and instant foods, dry and dehydrated goods, canned and jarred goods, dairy alternatives, condiments and cooking essentials, snacks and sweets, and drinks, so a stockpile is built from food a family will actually eat rather than tins that expire untouched.",
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
            Description = "A fatty liver food list organised by what to eat freely and what to hold back, running through vegetables, whole grains, legumes, fish and oils with the everyday portion in mind, and naming the specific foods rather than describing them in categories.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/fatty-liver-food-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("4c104954-0a46-51db-8262-7f5db25c447d"),
            Name = "Fatty Liver Diet List",
            Description = "A fatty liver list in three verdicts — eat, limit, avoid — covering grains and pulses, vegetables, fruit, proteins and fats, with the items written out so it can be taken shopping rather than interpreted at the fridge.",
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
            Description = "A 112-item first-apartment list with the kitchen carrying the most weight at 35 items, because that is where a first flat is most expensive to stock from nothing, alongside bedroom, living room, bathroom, laundry, extra supplies, home tools and a short column of optionals that can wait.",
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
            Name = "Folic Acid Rich Food List",
            Description = "A folic acid food list grouped by source: leafy greens and cruciferous vegetables first, then legumes, citrus, fortified grains and the rest, with the everyday portion noted so the list answers what to eat this week rather than what contains folate in principle.",
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
            Name = "GLP 1 Food Checklist",
            Description = "A 117-item food list for people on a GLP-1 medication, built around the order that matters on the drug: protein and high-fibre vegetables first, then low-sugar fruit, whole grains and healthy fats, with the refined carbohydrates, sugary drinks and fried foods that cause trouble listed separately in three descending tiers.",
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
            Name = "Grocery List",
            Description = "A 317-item grocery list sorted the way a supermarket is: 21 aisles from fruit and vegetables through breakfast, meat, seafood and frozen, then baking, snacks, bakery, pasta and rice, cans and jars, chilled goods, seasoning, sauces, drinks, paper goods, cleaning, personal care and a short misc column, so the list follows the trolley instead of sending you back.",
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
            Description = "A weekly schedule cut into half-hour blocks, from waking to sleeping, for the weeks when the problem is not knowing what to do but fitting it into the hours that exist.",
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
            Description = "A heart health food list built around what the food does rather than which aisle it comes from: omega-3 sources such as salmon, herring, mackerel and tuna each carry a note on what they contribute, alongside the fibre, nut and oil choices that support the same goal.",
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
            Description = "A protein guide that gives the number rather than the adjective: chicken breast at 165 calories and 31 grams per serving, turkey breast at 160 and 29, lean beef at 250 and 26, and so on down the list, so a daily target can be hit by arithmetic instead of estimation.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/high-protein-food-guide.pdf"
        },
        new()
        {
            Id = Guid.Parse("7ca2d05e-9f0a-56e0-8387-5cfd1e9c5b60"),
            Name = "Household Checklist",
            Description = "A 62-item household checklist covering the work that keeps a home running rather than the work that makes it look tidy: dishwashing, cleaning, organisation, laundry, floors, windows, trash, bathroom, hygiene, kitchen and supplies, eleven groups in one page.",
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
            Description = "A monthly investment goals sheet: what is being put in, what it is for and what the target is, one page per month so the contribution is decided before the month spends it rather than audited afterwards.",
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
            Name = "Kidney Disease Food List",
            Description = "A kidney disease food list that keeps eat and avoid on one page: red pepper, cauliflower, green beans, cucumber and arugula in the eat column, the potassium and phosphorus heavy foods in the other, written for a fridge door rather than a folder.",
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
            Name = "Kidney Disease Food List — Tabbed",
            Description = "The kidney food list rebuilt with tab dividers, so the 117 items across vegetables, fruits, lean proteins, grains and starch, healthy fats, dairy alternatives and the rest can be flipped to without hunting: a print-and-file edition of the same renal guidance.",
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
            Description = "A 158-item kidney list that runs in two halves: the low-potassium, low-phosphorus foods worth leaning on, then the high-potassium, high-phosphorus and salty foods worth holding back, with dairy substitutes, herbs and drinks included because those are where hidden potassium usually hides.",
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
            Description = "A 207-item low-carb list divided into what to eat, what to limit and what to avoid, running through meat, seafood, poultry and eggs, vegetables, fruit, dairy and dairy alternatives, pantry staples, fats and oils, nuts, seeds, fermented foods, sweeteners and drinks, so the boundary is visible instead of guessed.",
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
            Name = "Low Potassium Food List",
            Description = "A low potassium list arranged in three descending tiers rather than two columns: the foods to lean on across vegetables, fruit, proteins, grains, dairy alternatives, fats and snacks, the middle ground, then the high-potassium foods to leave alone, so a daily allowance can be spent rather than avoided.",
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
            Description = "A weekly meal plan with a slot for every meal of the seven days, plus room for the shopping that follows from it, so the plan and the list are written on the same page instead of being reconstructed in the supermarket.",
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
            Description = "A monthly budget template with all 33 household categories already named, from rent or mortgage, property tax and home insurance through utilities, groceries, transport, insurance, medical, childcare, student loans, savings, investments, subscriptions and the miscellaneous line everyone forgets, so nothing is invented from scratch each month.",
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
            Description = "A paycheck budget that allocates one pay period at a time: bills with due date, amount and whether they are paid, then spending, savings goal and extra debt, so the money is divided when it lands rather than when it runs out.",
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
            Name = "Budget Planner Grid",
            Description = "A budget planner as a grid: income, bills, savings, expenses, debt and a summary sitting side by side on one sheet, for people who want the whole month visible at once rather than spread across six pages.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/planner-grid.pdf"
        },
        new()
        {
            Id = Guid.Parse("2ba0a7ad-4c18-569f-b456-f8d093ba2775"),
            Name = "Travel Packing Checklist",
            Description = "A 57-item travel packing list grouped by what tends to be forgotten: documents first, then toiletries at 22 items, financials, gadgets, health and medical, and a misc column for the adapters, padlocks and spare bags that only matter once you are away.",
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
            Description = "A weekly plan with the seven days laid out plainly and room to write, no hourly grid and no colour system: a page for the week as it is, with the few things that must happen kept where they can be seen.",
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
        new()
        {
            Id = Guid.Parse("8a40c675-c20f-515a-8c25-68f1f9b01b48"),
            Name = "Budget vs Actual Worksheet",
            Description = "A one-page month-end budget review worksheet that lines up budgeted and actual spending across needs, wants, and savings, with difference and percent-used columns, rollover checkboxes, and three takeaway lines that set next month's plan.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/budget-vs-actual.pdf"
        },
        new()
        {
            Id = Guid.Parse("3c03c110-87ef-5e1a-913c-f3eae6058352"),
            Name = "Budget vs Actual Worksheet — Green",
            Description = "A one-page month-end review sheet in a green serif style that pairs budgeted and actual spending across needs, wants, and savings, with difference and percent-used columns, rollover boxes, and three takeaway lines for next month's plan.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/budget-vs-actual-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("c280a457-9a17-5e5e-a1d5-71c0541902d7"),
            Name = "Budget vs Actual Worksheet — Serif",
            Description = "A one-page month-end review sheet with a serif title that pairs budgeted and actual spending across needs, wants, and savings, with difference and percent-used columns, rollover boxes, and three takeaway lines for next month's plan.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/budget-vs-actual-serif.pdf"
        },
        new()
        {
            Id = Guid.Parse("bfbd75fa-2fe4-5c36-8f77-d3da9e571552"),
            Name = "Carry On Packing List",
            Description = "Pack light with a single-page carry-on worksheet that sets tops, bottoms, underwear and shoe counts by trip length, then splits the load into personal-item, clothing, wear-on-plane and before-you-zip checklists.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/carryon-packing.pdf"
        },
        new()
        {
            Id = Guid.Parse("71d3b629-1419-5171-8ae4-57a361ab5011"),
            Name = "Dialysis Food List",
            Description = "Keep dialysis meals and fluids on track with a single-page large-print guide that sorts foods into enjoy, limit, and avoid columns, shows potassium, phosphorus, sodium, and fluid targets, and includes a seven-day cup-by-cup fluid log.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/dialysis-food-fluid.pdf"
        },
        new()
        {
            Id = Guid.Parse("8d3c5bd7-d851-5ddf-9c3b-3fa4ed426c77"),
            Name = "Dorm Room Layout Planner",
            Description = "Map out a dorm room before move-in day with a to-scale floor plan grid, measured furniture footprints, clearance rules, fit checks and a roommate split sheet on one printable page.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/dorm-room-layout.pdf"
        },
        new()
        {
            Id = Guid.Parse("b8cabfac-25f0-59cd-8e3e-402b9684ea2e"),
            Name = "Emergency Go Bag Checklist",
            Description = "One gear list for three packs, home, car and work, with quantities, targets and a twice-yearly check so every bag is ready before you need it.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#ECEFF3", "#D4DAE2", "#334155"),
            PdfBlobName = "guides/emergency-gobag.pdf"
        },
        new()
        {
            Id = Guid.Parse("edb04b04-eb0d-5766-b88d-9af60467945a"),
            Name = "Emergency Go Bag Checklist — Green",
            Description = "One gear list for three packs, home, car and work, with quantities, targets and a twice-yearly check so every bag is ready before you need it.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/emergency-gobag-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("f9ae37c2-6850-5cff-a35b-7616534eef14"),
            Name = "Emergency Go Bag Checklist — Pink",
            Description = "One gear list for three packs, home, car and work, with quantities, targets and a twice-yearly check so every bag is ready before you need it.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/emergency-gobag-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("a5e0d761-5ced-5628-aabc-1d2f89ce1a4d"),
            Name = "Emergency Go Bag Checklist — Plum",
            Description = "One gear list for three packs, home, car and work, with quantities, targets and a twice-yearly check so every bag is ready before you need it.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/emergency-gobag-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("5f899170-eda9-5d3c-bd3b-175467535afd"),
            Name = "Emergency Go Bag Checklist — Serif",
            Description = "One gear list for three packs, home, car and work, with quantities, targets and a twice-yearly check so every bag is ready before you need it.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#ECEFF3", "#D4DAE2", "#334155"),
            PdfBlobName = "guides/emergency-gobag-serif.pdf"
        },
        new()
        {
            Id = Guid.Parse("8d97707e-597e-5810-b01c-70932c2f3ee6"),
            Name = "Emergency Food Stock Log",
            Description = "Set a 30-day emergency food target, record what is already on hand, and keep every best-by date rotating with a single-page stock log built for pantry shelves.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#ECEFF3", "#D4DAE2", "#334155"),
            PdfBlobName = "guides/emergency-stock-log.pdf"
        },
        new()
        {
            Id = Guid.Parse("7263d856-4af9-58df-985b-3a7d2c259b6e"),
            Name = "Gout Food List",
            Description = "Keep a purine budget on one page with an eat-freely, limit, and avoid gout food chart that shows milligrams per 100 g, smart swaps, and a daily tally against a 500 mg target.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/gout-food-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("7a25d2d6-e37a-58d4-936f-7749b3ff351c"),
            Name = "Weekly Meal Plan and Grocery List",
            Description = "Plan seven days of meals across a breakfast lunch and dinner grid, then carry the same page through the store with checkbox produce, meat, dairy, bakery, pantry and freezer lists plus quantity, budget and notes lines.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/grocery-mealplan.pdf"
        },
        new()
        {
            Id = Guid.Parse("14b98d17-7d49-594c-aaa3-5381fe7736ef"),
            Name = "Weekly Meal Plan and Grocery List — Blue",
            Description = "Plan seven days of meals across a breakfast lunch and dinner grid, then carry the same page through the store with checkbox produce, meat, dairy, bakery, pantry and freezer lists plus quantity, budget and notes lines.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/grocery-mealplan-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("ead52317-fc37-52ca-ad0b-b52a517a5d34"),
            Name = "Weekly Meal Plan and Grocery List — Mustard",
            Description = "Plan seven days of meals across a breakfast lunch and dinner grid, then carry the same page through the store with checkbox produce, meat, dairy, bakery, pantry and freezer lists plus quantity, budget and notes lines.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/grocery-mealplan-mustard.pdf"
        },
        new()
        {
            Id = Guid.Parse("2e69d05e-d671-56ed-930e-be4a9c7073d1"),
            Name = "Weekly Meal Plan and Grocery List — Pink",
            Description = "Plan seven days of meals across a breakfast lunch and dinner grid, then carry the same page through the store with checkbox produce, meat, dairy, bakery, pantry and freezer lists plus quantity, budget and notes lines.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/grocery-mealplan-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("3ca27091-03c9-5cb7-9666-a12f0c0d3857"),
            Name = "Weekly Meal Plan and Grocery List — Plum",
            Description = "Plan seven days of meals across a breakfast lunch and dinner grid, then carry the same page through the store with checkbox produce, meat, dairy, bakery, pantry and freezer lists plus quantity, budget and notes lines.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/grocery-mealplan-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("532c2723-5815-5043-8e57-eb41d7ef0200"),
            Name = "Grocery Shopping List",
            Description = "A single-page grocery shopping list that sorts 348 supermarket items into six aisle columns, stars the pantry staples to check at home first, and adds store, date, budget and total-spent fields plus write-in rows for anything extra.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/grocery-shopping-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("269306e8-1022-5f77-95b9-a5d5ea0ba03a"),
            Name = "Kidney Friendly Grocery List",
            Description = "Shop nine supermarket aisles with a single-page, color-coded renal grocery list that separates low-potassium, low-phosphorus, and low-sodium foods into enjoy, small-portion, and skip picks for easier CKD meal planning.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/kidney-grocery-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("dbaab45d-7ac7-5878-b8e7-84ec06179f88"),
            Name = "Kidney Lab Log",
            Description = "Track eGFR, creatinine, potassium, phosphorus, blood pressure, and between-visit symptoms on a single-page kidney lab log that keeps the essentials in one place for nephrology appointments.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/kidney-lab-essential.pdf"
        },
        new()
        {
            Id = Guid.Parse("6b54ca42-68ba-51c7-ac93-4379785b6ca3"),
            Name = "Minimal Kidney Lab Log",
            Description = "Record eGFR, creatinine, potassium, phosphorus, and blood pressure on a single-page, large-print kidney lab log built for quick checkups between nephrology visits.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#ECEFF3", "#D4DAE2", "#334155"),
            PdfBlobName = "guides/kidney-lab-minimal.pdf"
        },
        new()
        {
            Id = Guid.Parse("45a9e33b-12c5-5e67-974d-b111f68e7ef0"),
            Name = "Kidney Lab Tracker",
            Description = "Track eGFR, creatinine, potassium, phosphorus, blood pressure, and monthly symptoms on a single-page kidney lab log built to bring a clear trend to every nephrology appointment.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/kidney-lab-tracker.pdf"
        },
        new()
        {
            Id = Guid.Parse("cc1d394f-12dd-5e31-8174-79fde70c23ea"),
            Name = "Kidney Lab Tracker — Pink",
            Description = "Track eGFR, creatinine, potassium, phosphorus, blood pressure, and monthly symptoms on a single-page kidney lab log built to bring a clear trend to every nephrology appointment.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/kidney-lab-tracker-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("78d07102-03ba-5626-98a2-4b497d59f84c"),
            Name = "Kidney Lab Tracker — Serif",
            Description = "Track eGFR, creatinine, potassium, phosphorus, blood pressure, and monthly symptoms on a single-page kidney lab log built to bring a clear trend to every nephrology appointment.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/kidney-lab-tracker-serif.pdf"
        },
        new()
        {
            Id = Guid.Parse("8011d94c-c09b-54f8-a19e-fb1b9283c4a3"),
            Name = "Low Potassium Food Swap Chart",
            Description = "Swap higher-potassium foods for lower-potassium alternatives at a glance, with typical milligram values, realistic serving sizes and kidney-friendly substitutes on one printable page.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/lowpotassium-swap-chart.pdf"
        },
        new()
        {
            Id = Guid.Parse("fc341164-bed2-5c31-b979-16c10326cf2f"),
            Name = "First Apartment Move-In Budget Planner",
            Description = "A single-page move-in budget worksheet that breaks first-apartment setup into room-by-room items with buy-now versus can-wait priorities, estimated and actual cost columns, a bought checkbox, a before-you-move cash section for deposits and utilities, and a move-in basics shopping list.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/movein-budget.pdf"
        },
        new()
        {
            Id = Guid.Parse("e5ee24ac-fcff-5ac8-b6b3-1c9d4c7ff5b2"),
            Name = "Power Outage Food List",
            Description = "A one-page power-outage food list that sequences fridge and freezer food before it spoils, lists shelf-stable no-cook foods, and maps three days of no-heat meals with a stock-up checklist.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#ECEFF3", "#D4DAE2", "#334155"),
            PdfBlobName = "guides/power-outage-foodlist.pdf"
        },
        new()
        {
            Id = Guid.Parse("f757decb-ada9-5e2b-8612-2780668675c9"),
            Name = "Power Outage Food List — Blue",
            Description = "A one-page power-outage food list that sequences fridge and freezer food before it spoils, lists shelf-stable no-cook foods, and maps three days of no-heat meals with a stock-up checklist.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/power-outage-foodlist-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("70f9aab6-245d-56e2-994b-852a28cde4fb"),
            Name = "Power Outage Food List — Green",
            Description = "A one-page power-outage food list that sequences fridge and freezer food before it spoils, lists shelf-stable no-cook foods, and maps three days of no-heat meals with a stock-up checklist.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/power-outage-foodlist-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("b373c0f8-033d-5b66-ad28-470f2dc5881e"),
            Name = "Power Outage Food List — Light Blue",
            Description = "A one-page power-outage food list that sequences fridge and freezer food before it spoils, lists shelf-stable no-cook foods, and maps three days of no-heat meals with a stock-up checklist.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/power-outage-foodlist-lightblue.pdf"
        },
        new()
        {
            Id = Guid.Parse("c257f59a-f3bc-5f3d-845b-32f8f7f7c10d"),
            Name = "Power Outage Food List — Pink",
            Description = "A one-page power-outage food list that sequences fridge and freezer food before it spoils, lists shelf-stable no-cook foods, and maps three days of no-heat meals with a stock-up checklist.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/power-outage-foodlist-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("d740a989-b1b0-500e-84ad-46941f2ec193"),
            Name = "Renal Diet Restaurant Guide",
            Description = "Order at any restaurant with less guesswork using nine cuisine cards that pair what to order, what to leave on the plate, and the exact phrase to say, plus a spot to write in your own targets.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/renal-eating-out.pdf"
        },
        new()
        {
            Id = Guid.Parse("b8c77ca3-a478-552c-840c-70b47bf856f3"),
            Name = "Renal Diet Restaurant Guide — Baby Blue",
            Description = "Order at any restaurant with less guesswork using nine cuisine cards that pair what to order, what to leave on the plate, and the exact phrase to say, plus a spot to write in your own targets.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/renal-eating-out-babyblue.pdf"
        },
        new()
        {
            Id = Guid.Parse("a80c1136-717d-53b1-85a9-4a5f682a340f"),
            Name = "Renal Diet Restaurant Guide — Blue",
            Description = "Order at any restaurant with less guesswork using nine cuisine cards that pair what to order, what to leave on the plate, and the exact phrase to say, plus a spot to write in your own targets.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/renal-eating-out-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("8841334e-1406-5c85-bc4e-5b32b01096a9"),
            Name = "Renal Diet Restaurant Guide — Pink",
            Description = "Order at any restaurant with less guesswork using nine cuisine cards that pair what to order, what to leave on the plate, and the exact phrase to say, plus a spot to write in your own targets.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/renal-eating-out-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("f69d2238-b28e-5178-ac3f-9871efdf8565"),
            Name = "Renal Diet Restaurant Guide — Serif",
            Description = "Order at any restaurant with less guesswork using nine cuisine cards that pair what to order, what to leave on the plate, and the exact phrase to say, plus a spot to write in your own targets.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/renal-eating-out-serif.pdf"
        },
        new()
        {
            Id = Guid.Parse("e8f1d947-627a-5aa3-a4ad-68d15fe9430d"),
            Name = "Renal Diet Meal Plan",
            Description = "Plan a full week of kidney-friendly meals with a single-page PDF that pairs seven days of low-potassium, low-sodium menus with portion cues, typical mineral ranges and a matching aisle-by-aisle shopping list for simpler CKD grocery trips.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/renal-meal-plan.pdf"
        },
        new()
        {
            Id = Guid.Parse("4ce64c3b-166d-5bdd-b408-a64ac15fe060"),
            Name = "50 30 20 Budget Worksheet",
            Description = "A one-page 50/30/20 budget worksheet that splits take-home pay into needs, wants and savings, with plan and spent columns for every category, a zero check that proves each dollar is assigned, and three fixes for when the split does not fit.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/split-budget.pdf"
        },
        new()
        {
            Id = Guid.Parse("8c9570db-9d8c-557b-b923-6e672bdf3e52"),
            Name = "50 30 20 Budget Worksheet — Balanced",
            Description = "A one-page 50/30/20 budget worksheet with a colour-coded needs, wants and savings column, a dollar target for each share, nine categories per column and a zero check that proves the month is fully assigned.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/split-budget-balanced.pdf"
        },
        new()
        {
            Id = Guid.Parse("74f88971-d286-5bc9-a650-65daea155c68"),
            Name = "50 30 20 Budget Worksheet — Balanced Blue",
            Description = "A one-page 50/30/20 budget worksheet with a colour-coded needs, wants and savings column, a dollar target for each share, nine categories per column and a zero check that proves the month is fully assigned.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/split-budget-balanced-blue.pdf"
        },
        new()
        {
            Id = Guid.Parse("a8a80324-6198-5188-9df4-60c10ea2b48e"),
            Name = "50 30 20 Budget Worksheet — Balanced Pink",
            Description = "A one-page 50/30/20 budget worksheet with a colour-coded needs, wants and savings column, a dollar target for each share, nine categories per column and a zero check that proves the month is fully assigned.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),
            PdfBlobName = "guides/split-budget-balanced-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("0b112974-018e-5265-ac71-6fd014055d38"),
            Name = "50 30 20 Budget Worksheet — Balanced Serif",
            Description = "A one-page 50/30/20 budget worksheet with a colour-coded needs, wants and savings column, a dollar target for each share, nine categories per column and a zero check that proves the month is fully assigned.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/split-budget-balanced-serif.pdf"
        },
        new()
        {
            Id = Guid.Parse("5c366176-ca6a-5907-bac6-8c3fb1891d03"),
            Name = "50 30 20 Budget Worksheet — Minimal",
            Description = "A stripped-back 50/30/20 budget worksheet with three oversized percentage columns, one amount line per category, and a single zero check that proves every dollar of take-home pay is assigned.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#ECEFF3", "#D4DAE2", "#334155"),
            PdfBlobName = "guides/split-budget-minimal.pdf"
        },
        new()
        {
            Id = Guid.Parse("62ef6a04-a14b-5734-94ee-4ae8e1e89b4b"),
            Name = "Warehouse Club Shopping List",
            Description = "A one-page warehouse club stock-up sheet with three pack-buying rules, four aisle columns of 112 items, pack sizes, and a pack-count box beside every item.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/warehouse-bulk-list.pdf"
        },
        new()
        {
            Id = Guid.Parse("d9b9ae8e-7899-56f0-ae51-07064d4e0f09"),
            Name = "Yearly Budget Planner",
            Description = "A yearly budget planner with a column for each of the twelve months, so the annual picture sits on one sheet and the expensive months, insurance renewals and the run-up to December are visible in advance instead of arriving as a surprise.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/yearly-budget.pdf"
        },
        new()
        {
            Id = Guid.Parse("9abe51fa-5b3c-55f1-b360-47b968ab35fd"),
            Name = "Bill Calendar Budget Planner",
            Description = "Fill in each bill on its due date and carry the running balance down the column to see what is left before the next check arrives.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/bill-calendar-budget.pdf"
        },
        new()
        {
            Id = Guid.Parse("5e7aaa32-4875-5adf-ad42-9a02f83265b4"),
            Name = "Calendario de Pagos",
            Description = "Anota cada factura en su fecha de vencimiento y resta el saldo línea por línea para ver cuánto queda antes del próximo pago.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/bill-calendar-budget-es.pdf"
        },
        new()
        {
            Id = Guid.Parse("27ef762d-55bf-595f-bf00-8f49a91ca3b0"),
            Name = "Bill Calendar Budget Planner — Green",
            Description = "Fill in each bill on its due date and carry the running balance down the column to see what is left before the next check arrives.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/bill-calendar-budget-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("f839654c-af7e-59b5-9a78-141c6d5ee76c"),
            Name = "Bill Calendar Budget Planner — Pink",
            Description = "Fill in each bill on its due date and carry the running balance down the column to see what is left before the next check arrives.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/bill-calendar-budget-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("75e044b1-f381-5700-a490-370455b3223a"),
            Name = "Bill Calendar Budget Planner — Plum",
            Description = "Fill in each bill on its due date and carry the running balance down the column to see what is left before the next check arrives.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/bill-calendar-budget-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("57db2a1a-f838-5572-91b2-405fab57e987"),
            Name = "Cortisol Food List",
            Description = "A one-page cortisol food list that sorts everyday foods into eat freely, keep small, and leave out, with a steadier-day eating timeline, four easy swaps, and the daily pattern that matters more than any single food.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/cortisol-foodlist.pdf"
        },
        new()
        {
            Id = Guid.Parse("b812c8cd-d45a-5ea9-ba07-7b1050469a14"),
            Name = "Lista de Alimentos para el Cortisol",
            Description = "Una hoja que ordena los alimentos de cada día en sin límite, con moderación y evitar, con un horario diario y cuatro cambios fáciles para estabilizar la energía.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/cortisol-foodlist-es.pdf"
        },
        new()
        {
            Id = Guid.Parse("f1281264-4d94-5853-a731-6a24dcfe6028"),
            Name = "Cortisol Food List — Green",
            Description = "A one-page cortisol food list that sorts everyday foods into eat freely, keep small, and leave out, with a steadier-day eating timeline, four easy swaps, and the daily pattern that matters more than any single food.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/cortisol-foodlist-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("a6c3c648-569a-570a-b735-6f801c47f143"),
            Name = "Cortisol Food List — Pink",
            Description = "A one-page cortisol food list that sorts everyday foods into eat freely, keep small, and leave out, with a steadier-day eating timeline, four easy swaps, and the daily pattern that matters more than any single food.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/cortisol-foodlist-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("fefc4091-d716-50f9-aec5-5e1d4e718e96"),
            Name = "Cortisol Food List — Plum",
            Description = "A one-page cortisol food list that sorts everyday foods into eat freely, keep small, and leave out, with a steadier-day eating timeline, four easy swaps, and the daily pattern that matters more than any single food.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/cortisol-foodlist-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("0888d213-9155-5d7e-8164-49f082bd5e55"),
            Name = "Diverticulitis Flare-Up Meal Plan",
            Description = "Plan five gentle low-fiber days with breakfast, lunch, dinner and snacks, a one-page shopping list and a step-by-step return to high-fiber eating after a mild diverticulitis flare.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/diverticulitis-meal-plan.pdf"
        },
        new()
        {
            Id = Guid.Parse("e8632cf1-a132-58f9-8339-6070043157cb"),
            Name = "Plan de Comidas para Diverticulitis",
            Description = "Organiza cinco días suaves y bajos en fibra con desayuno, almuerzo, cena y meriendas, lista de compras y una guía paso a paso para volver a la fibra después de un brote leve de diverticulitis.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/diverticulitis-meal-plan-es.pdf"
        },
        new()
        {
            Id = Guid.Parse("20b0e67e-64f2-57fb-acdc-38ce4be9b3e5"),
            Name = "Diverticulitis Flare-Up Meal Plan — Green",
            Description = "Plan five gentle low-fiber days with breakfast, lunch, dinner and snacks, a one-page shopping list and a step-by-step return to high-fiber eating after a mild diverticulitis flare.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/diverticulitis-meal-plan-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("5063ce83-a031-5c6e-adcc-2cbca48baeae"),
            Name = "Diverticulitis Flare-Up Meal Plan — Pink",
            Description = "Plan five gentle low-fiber days with breakfast, lunch, dinner and snacks, a one-page shopping list and a step-by-step return to high-fiber eating after a mild diverticulitis flare.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/diverticulitis-meal-plan-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("ce22c90f-feff-566b-baf8-d1134b990db5"),
            Name = "Diverticulitis Flare-Up Meal Plan — Plum",
            Description = "Plan five gentle low-fiber days with breakfast, lunch, dinner and snacks, a one-page shopping list and a step-by-step return to high-fiber eating after a mild diverticulitis flare.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/diverticulitis-meal-plan-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("92716db1-b6c0-52f1-a93c-f0e5f17ccbf3"),
            Name = "First Apartment Essentials — Costs",
            Description = "A one-page first apartment budget sheet with six room-by-room sections, a price box beside every item, a room total for each section, the move-in fees that come before signing, and a grand total to fill in.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/first-apartment-costs.pdf"
        },
        new()
        {
            Id = Guid.Parse("02240381-6f4b-5a08-8112-6bd984cd3c4d"),
            Name = "Lista de Gastos Primer Apartamento",
            Description = "Una hoja para poner precio a cada cosa del primer apartamento, con seis cuartos, total por cuarto, gastos de entrada y total general.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/first-apartment-costs-es.pdf"
        },
        new()
        {
            Id = Guid.Parse("594672f9-4f2a-5b6c-a8f5-4417f4fd2ca4"),
            Name = "First Apartment Essentials — Costs Green",
            Description = "A one-page first apartment budget sheet with six room-by-room sections, a price box beside every item, a room total for each section, the move-in fees that come before signing, and a grand total to fill in.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/first-apartment-costs-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("27d3e21b-aded-5c6c-853c-e9f7430fc198"),
            Name = "First Apartment Essentials — Costs Pink",
            Description = "A one-page first apartment budget sheet with six room-by-room sections, a price box beside every item, a room total for each section, the move-in fees that come before signing, and a grand total to fill in.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/first-apartment-costs-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("94473088-26ac-52dd-bd4e-918c16998e87"),
            Name = "First Apartment Essentials — Costs Plum",
            Description = "A one-page first apartment budget sheet with six room-by-room sections, a price box beside every item, a room total for each section, the move-in fees that come before signing, and a grand total to fill in.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/first-apartment-costs-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("940f690e-2fc5-5f47-ab76-8094d716515b"),
            Name = "Kidney Friendly Holiday Menu",
            Description = "A single-page holiday menu sorts turkey, sides, desserts and drinks into enjoy, small-serving and skip picks, with portions and lower-sodium swaps for CKD and dialysis tables.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/holiday-kidney-table.pdf"
        },
        new()
        {
            Id = Guid.Parse("e2367d39-4404-5491-a4d7-4ecee5ef6fe9"),
            Name = "Mesa Festiva para el Riñón",
            Description = "Una hoja de fiestas ordena pavo, acompañamientos, postres y bebidas en disfruta, porción pequeña y evita, con porciones y cambios bajos en sodio para la mesa renal.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/holiday-kidney-table-es.pdf"
        },
        new()
        {
            Id = Guid.Parse("2d86acf2-8637-5e5a-a08e-a68d8712da24"),
            Name = "Kidney Friendly Holiday Menu — Green",
            Description = "A single-page holiday menu sorts turkey, sides, desserts and drinks into enjoy, small-serving and skip picks, with portions and lower-sodium swaps for CKD and dialysis tables.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/holiday-kidney-table-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("59cfa1f8-d3d2-5220-8d2f-701806abc010"),
            Name = "Kidney Friendly Holiday Menu — Pink",
            Description = "A single-page holiday menu sorts turkey, sides, desserts and drinks into enjoy, small-serving and skip picks, with portions and lower-sodium swaps for CKD and dialysis tables.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/holiday-kidney-table-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("dfd06a5d-8503-5b78-9b47-88919273248a"),
            Name = "Kidney Friendly Holiday Menu — Plum",
            Description = "A single-page holiday menu sorts turkey, sides, desserts and drinks into enjoy, small-serving and skip picks, with portions and lower-sodium swaps for CKD and dialysis tables.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "healthy-living",
            Accent = new ProductAccent("#EAF7F1", "#C6EBDD", "#0F766E"),
            PdfBlobName = "guides/holiday-kidney-table-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("f1271983-386a-58c7-9862-4edd4ce94064"),
            Name = "Ultimate Move Out Checklist",
            Description = "A room-by-room move-out checklist for renters that records condition and photos in each room, logs damage and repair costs, and totals the deposit deductions so the security deposit comes back on time.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/move-out-deposit.pdf"
        },
        new()
        {
            Id = Guid.Parse("3cdd8206-171e-50fc-98dc-e9aa49231a3d"),
            Name = "Guía Completa de Mudanza Imprimible",
            Description = "Una hoja imprimible para inquilinos que registra la condición y las fotos de cada habitación, anota daños y costos de reparación, y calcula el saldo del depósito para recuperarlo a tiempo.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/move-out-deposit-es.pdf"
        },
        new()
        {
            Id = Guid.Parse("9b402c7e-acfc-589e-aeaa-2043fde57cd0"),
            Name = "Ultimate Move Out Checklist — Green",
            Description = "A room-by-room move-out checklist for renters that records condition and photos in each room, logs damage and repair costs, and totals the deposit deductions so the security deposit comes back on time.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/move-out-deposit-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("3a66153a-acfd-56b5-97e6-17a049ebdf6d"),
            Name = "Ultimate Move Out Checklist — Pink",
            Description = "A room-by-room move-out checklist for renters that records condition and photos in each room, logs damage and repair costs, and totals the deposit deductions so the security deposit comes back on time.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/move-out-deposit-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("f7d8943e-f18e-5005-99a3-8010bb119d87"),
            Name = "Ultimate Move Out Checklist — Plum",
            Description = "A room-by-room move-out checklist for renters that records condition and photos in each room, logs damage and repair costs, and totals the deposit deductions so the security deposit comes back on time.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/move-out-deposit-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("90d1725e-60b3-5605-9a00-013275deb134"),
            Name = "14 Day Emergency Meal Plan",
            Description = "Fourteen days of breakfast, lunch, dinner and snacks built only from shelf-stable food, with household quantities and a two-week shopping list so the pantry gets bought once and eaten in order.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/no-power-meal-plan.pdf"
        },
        new()
        {
            Id = Guid.Parse("844764a2-1b33-574f-8fb7-9d0ad5620919"),
            Name = "Plan de Comidas 14 Días Sin Luz",
            Description = "Catorce días de desayuno, almuerzo, cena y merienda armados solo con comida de despensa, con raciones por persona y una lista de compras de dos semanas para comprar una vez y comer en orden.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/no-power-meal-plan-es.pdf"
        },
        new()
        {
            Id = Guid.Parse("2ea59c26-555a-5130-8769-8149afd8539e"),
            Name = "14 Day Emergency Meal Plan — Green",
            Description = "Fourteen days of breakfast, lunch, dinner and snacks built only from shelf-stable food, with household quantities and a two-week shopping list so the pantry gets bought once and eaten in order.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/no-power-meal-plan-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("e1ea81cf-0ae8-5a7e-8f3e-8369c10bedc4"),
            Name = "14 Day Emergency Meal Plan — Pink",
            Description = "Fourteen days of breakfast, lunch, dinner and snacks built only from shelf-stable food, with household quantities and a two-week shopping list so the pantry gets bought once and eaten in order.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/no-power-meal-plan-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("29730c3c-be29-552a-b7db-6ce390fd7353"),
            Name = "14 Day Emergency Meal Plan — Plum",
            Description = "Fourteen days of breakfast, lunch, dinner and snacks built only from shelf-stable food, with household quantities and a two-week shopping list so the pantry gets bought once and eaten in order.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/no-power-meal-plan-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("fcc8becd-67ee-5b12-bb07-716b3229cf46"),
            Name = "Emergency Food List 2",
            Description = "A four-week dinner rotation built entirely from shelf-stable staples, with calorie counts, no-power cooking notes and a monthly top-up routine so the emergency pantry gets eaten and restocked instead of forgotten.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/pantry-meal-rotation.pdf"
        },
        new()
        {
            Id = Guid.Parse("200ba525-db23-5699-853f-9db13e5136f4"),
            Name = "Rotación de Comidas 28 Días",
            Description = "Una rotación de cuatro semanas de cenas hechas solo con conservas y productos de despensa, con calorías contadas, notas para cocinar sin electricidad y una rutina mensual de reposición.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/pantry-meal-rotation-es.pdf"
        },
        new()
        {
            Id = Guid.Parse("5dfb2afa-9587-53eb-8a23-079eb64298c4"),
            Name = "Emergency Food List — Green",
            Description = "A four-week dinner rotation built entirely from shelf-stable staples, with calorie counts, no-power cooking notes and a monthly top-up routine so the emergency pantry gets eaten and restocked instead of forgotten.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/pantry-meal-rotation-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("8faee11b-62ad-508e-b4ef-658eda3d5b6b"),
            Name = "Emergency Food List — Pink",
            Description = "A four-week dinner rotation built entirely from shelf-stable staples, with calorie counts, no-power cooking notes and a monthly top-up routine so the emergency pantry gets eaten and restocked instead of forgotten.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/pantry-meal-rotation-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("8043ac2e-2d28-52b0-b0dd-5a23f46bcaaa"),
            Name = "Emergency Food List — Plum",
            Description = "A four-week dinner rotation built entirely from shelf-stable staples, with calorie counts, no-power cooking notes and a monthly top-up routine so the emergency pantry gets eaten and restocked instead of forgotten.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/pantry-meal-rotation-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("d3661f69-76af-550c-a7cd-ca1f17b2320f"),
            Name = "Sinking Funds Tracker",
            Description = "A one-page sinking funds worksheet that gives every non-monthly bill its own line with target, saved, and monthly set-aside amounts plus twelve numbered month boxes to tick off each transfer, so insurance, repairs, holidays, and annual subscriptions are funded before they arrive.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/sinking-funds-tracker.pdf"
        },
        new()
        {
            Id = Guid.Parse("ad3efb9c-7f2a-533a-a8fc-40a699d082fe"),
            Name = "Control de Fondos de Reserva",
            Description = "Una hoja para organizar los fondos de reserva: cada gasto que no es mensual tiene su meta, lo ahorrado y la cantidad mensual, con doce casillas numeradas para marcar cada apartado.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/sinking-funds-tracker-es.pdf"
        },
        new()
        {
            Id = Guid.Parse("4b4b3750-8e60-5f63-b81f-9e621b7f9dfc"),
            Name = "Sinking Funds Tracker — Green",
            Description = "A one-page sinking funds worksheet that gives every non-monthly bill its own line with target, saved, and monthly set-aside amounts plus twelve numbered month boxes to tick off each transfer, so insurance, repairs, holidays, and annual subscriptions are funded before they arrive.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/sinking-funds-tracker-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("755c23ff-0aaf-5a7b-8e75-230dc33fdcd9"),
            Name = "Sinking Funds Tracker — Pink",
            Description = "A one-page sinking funds worksheet that gives every non-monthly bill its own line with target, saved, and monthly set-aside amounts plus twelve numbered month boxes to tick off each transfer, so insurance, repairs, holidays, and annual subscriptions are funded before they arrive.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/sinking-funds-tracker-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("52c9a6a3-cbec-599d-909c-5f7891408cd7"),
            Name = "Sinking Funds Tracker — Plum",
            Description = "A one-page sinking funds worksheet that gives every non-monthly bill its own line with target, saved, and monthly set-aside amounts plus twelve numbered month boxes to tick off each transfer, so insurance, repairs, holidays, and annual subscriptions are funded before they arrive.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/sinking-funds-tracker-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("33c5bff3-4e54-5c26-af55-dfef29d3e0ad"),
            Name = "Lista de Compras al Por Mayor",
            Description = "Una hoja imprimible para el club de compras con tres reglas para comprar por volumen, cuatro columnas de 112 productos, tamaños de paquete y una casilla para anotar cuántos paquetes llevar.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/warehouse-bulk-list-es.pdf"
        },
        new()
        {
            Id = Guid.Parse("1166b8b4-e676-529c-ac53-ebfe2a75d46b"),
            Name = "Warehouse Club Shopping List — Green",
            Description = "A one-page warehouse club stock-up sheet with three pack-buying rules, four aisle columns of 112 items, pack sizes, and a pack-count box beside every item.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/warehouse-bulk-list-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("29fa06a3-d064-592b-9abc-47e1d709918e"),
            Name = "Warehouse Club Shopping List — Pink",
            Description = "A one-page warehouse club stock-up sheet with three pack-buying rules, four aisle columns of 112 items, pack sizes, and a pack-count box beside every item.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/warehouse-bulk-list-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("94a8c5fe-7b10-5b56-aa54-cdba1c44c396"),
            Name = "Warehouse Club Shopping List — Plum",
            Description = "A one-page warehouse club stock-up sheet with three pack-buying rules, four aisle columns of 112 items, pack sizes, and a pack-count box beside every item.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "moving-out",
            Accent = new ProductAccent("#E7EEFF", "#C7D8FF", "#1D4ED8"),
            PdfBlobName = "guides/warehouse-bulk-list-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("febe87e2-ecd0-5f39-887e-01e7efb3a58e"),
            Name = "Half Hour Daily Planner",
            Description = "A day ruled into half-hour blocks from 6 AM to 9:30 PM, with room at the top for the three things that have to happen, so the plan for the day gets written down instead of held in your head.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/halfhour-daily-planner.pdf"
        },
        new()
        {
            Id = Guid.Parse("dda515eb-d54e-5a5e-a6da-048906238869"),
            Name = "Agenda Diaria por Media Hora",
            Description = "Un día dividido en bloques de media hora desde las 6:00 hasta las 21:30, con espacio arriba para las tres cosas que tienen que pasar, para que el plan del día quede escrito y no en la cabeza.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/halfhour-daily-planner-es.pdf"
        },
        new()
        {
            Id = Guid.Parse("981d0ad1-5ca4-5752-9ba7-a9ca7b10a231"),
            Name = "Half Hour Daily Planner — Green",
            Description = "A day ruled into half-hour blocks from 6 AM to 9:30 PM, with room at the top for the three things that have to happen, so the plan for the day gets written down instead of held in your head.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/halfhour-daily-planner-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("4cf0be0a-63d1-5e92-b5e3-dcdeee63ddf7"),
            Name = "Half Hour Daily Planner — Pink",
            Description = "A day ruled into half-hour blocks from 6 AM to 9:30 PM, with room at the top for the three things that have to happen, so the plan for the day gets written down instead of held in your head.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/halfhour-daily-planner-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("f8798ef1-956f-53fb-9d99-1ca22798fe33"),
            Name = "Half Hour Daily Planner — Plum",
            Description = "A day ruled into half-hour blocks from 6 AM to 9:30 PM, with room at the top for the three things that have to happen, so the plan for the day gets written down instead of held in your head.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "guides",
            Accent = new ProductAccent("#F1ECFF", "#DCD0FF", "#6D28D9"),
            PdfBlobName = "guides/halfhour-daily-planner-plum.pdf"
        },
        new()
        {
            Id = Guid.Parse("79199fac-9db1-592f-81c4-3925becefd1d"),
            Name = "Simple Monthly Budget Template",
            Description = "A one-page monthly budget sheet with planned and actual columns, bills, everyday spending and savings subtotals, and a bold left-to-spend total for beginner budgeters.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/simple-monthly-budget.pdf"
        },
        new()
        {
            Id = Guid.Parse("ce1b0190-94f6-5d78-922e-36a011f6f802"),
            Name = "Presupuesto Mensual Simple",
            Description = "Una hoja de presupuesto mensual de una página con columnas de planeado y gastado, totales de cuentas, gastos diarios y ahorros, y un renglón destacado con el dinero que queda al final del mes.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/simple-monthly-budget-es.pdf"
        },
        new()
        {
            Id = Guid.Parse("e445172f-781c-5f49-82e7-f8e6e5f68df3"),
            Name = "Simple Monthly Budget Template — Green",
            Description = "A one-page monthly budget sheet with planned and actual columns, bills, everyday spending and savings subtotals, and a bold left-to-spend total for beginner budgeters.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/simple-monthly-budget-green.pdf"
        },
        new()
        {
            Id = Guid.Parse("3e62e5fe-0380-5489-b305-ec0fd7a3442a"),
            Name = "Simple Monthly Budget Template — Pink",
            Description = "A one-page monthly budget sheet with planned and actual columns, bills, everyday spending and savings subtotals, and a bold left-to-spend total for beginner budgeters.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/simple-monthly-budget-pink.pdf"
        },
        new()
        {
            Id = Guid.Parse("8c7b171b-1b3e-5c96-9146-f4be0b115c0c"),
            Name = "Simple Monthly Budget Template — Plum",
            Description = "A one-page monthly budget sheet with planned and actual columns, bills, everyday spending and savings subtotals, and a bold left-to-spend total for beginner budgeters.",
            Price = 5.0m,
            Fulfillment = FulfillmentType.DigitalPdf,
            Category = "money-budgeting",
            Accent = new ProductAccent("#FEF7E7", "#FBE3B5", "#92400E"),
            PdfBlobName = "guides/simple-monthly-budget-plum.pdf"
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
