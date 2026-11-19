/**
 * The collection pages ("hubs") that sit above the individual guides.
 *
 * A guide page can only rank for a long-tail query; nobody's product page ranks for
 * "kidney diet printables". Each hub owns one of those broader searches, links down to
 * every guide in its subject, and gives those guides an inbound internal link from a page
 * a crawler reaches from the home page — which is the part the catalogue index alone never
 * provided.
 *
 * Membership is a prefix list over the guide slug (a listing id with underscores turned
 * into dashes), so a new colourway or translation joins its hub the moment it is
 * published, with no second list to keep in step. `scripts/generate-seo.mjs` walks the
 * hubs in this order and gives each guide to the first one that claims it, which is why
 * `renal_meal_plan` lands in kidney rather than in meal planning: `renal` is tested first.
 *
 * Copy lives here rather than in the generator because it is editorial, and because the
 * hubs are the only pages in the collection whose text is not derived from a listing.
 */
import { HUB_LINKS } from './hubLinks.js'

/**
 * The label for a collection, taken from the shared link list so the navbar, the home page
 * chips and the hub page itself can never disagree about what a hub is called.
 *
 * @param {string} slug Hub slug.
 * @returns {string} Its label.
 */
function hubLabel(slug) {
  const match = HUB_LINKS.find((hub) => hub.slug === slug)
  if (!match) throw new Error(`no hub link is defined for ${slug}`)
  return match.label
}

export const HUBS = [
  {
    slug: 'kidney',
    label: hubLabel('kidney'),
    title: 'Kidney-friendly food lists and renal diet printables',
    metaDescription:
      'Printable kidney food lists, CKD stage 3 charts, low-potassium swaps, dialysis and renal meal plans, lab logs and renal restaurant guides. Instant PDF, $5.',
    lede: 'Food lists, meal plans, lab logs and restaurant guides for CKD, dialysis and a low-potassium diet.',
    intro: [
      'Diet advice is easy to find and hard to use in a supermarket. These sheets are built for the two places a kidney diet is actually decided, the shop and the kitchen, and each one names the foods rather than describing a category: what to eat freely, what to keep small, what to leave out, and the potassium and phosphorus figures beside it.',
      'Start with the list that matches the constraint you have. The stage 3 food list is the general one, the dialysis sheet adds fluid and the minerals that matter on treatment, and the low-potassium list is for when a single number is the whole problem. The lab logs cover the other half of the job: eGFR, creatinine, potassium, phosphorus and blood pressure written down, so an appointment starts from a trend instead of a memory.',
      'These are general food guidance, not a treatment plan. The sheet your dietitian gave you always wins.',
    ],
    prefixes: ['kidney', 'ckd', 'renal', 'dialysis', 'lowpotassium', 'holiday-kidney'],
  },
  {
    slug: 'emergency',
    label: hubLabel('emergency'),
    title: 'Emergency food and power outage printables',
    metaDescription:
      'Printable 72-hour emergency plans, go-bag checklists, pantry rotation lists, stock logs and no-power meal plans for when the fridge stops and the shop shuts.',
    lede: 'Go bags, stock logs, pantry rotations and meal plans that assume no stove and no electricity.',
    intro: [
      'An emergency stockpile is only useful if you know what is in it, when it expires, and what you would actually cook without power. These sheets answer those three questions: a 72-hour plan, a go-bag checklist for home, car and work, and a stock log that keeps best-by dates rotating instead of buried.',
      'The food lists cover the smaller version of the same problem. A power outage has an order to it, the fridge warms from the top down and the freezer holds longer than people expect, so what to eat first and what can wait is a decision that can be made in advance, on a normal Tuesday.',
      'Print one, fill it in, keep it where the torch is.',
    ],
    prefixes: ['emergency', 'no-power', 'power-outage', 'pantry-meal'],
  },
  {
    slug: 'moving',
    label: hubLabel('moving'),
    title: 'First apartment checklists and moving printables',
    metaDescription:
      'Printable room-by-room first apartment checklists, setup cost worksheets, move-out deposit checklists, dorm lists and move-in budgets. Instant PDF, $5.',
    lede: 'Room-by-room lists for the first flat and the last one, and the costs that arrive before the furniture.',
    intro: [
      'The expensive mistakes in a first apartment are not the big purchases. They are the small things bought twice because nobody wrote them down, and the deposit deductions nobody photographed on the way in. These checklists walk the flat room by room: what to buy before day one, what can wait until the second month, and what to record before the keys go back.',
      'The cost sheets do the arithmetic that surprises people, deposit, first month, connection fees and the van, listed ahead of the furniture because that is the order the money actually leaves. The dorm sheets are the same idea for a room that has to be a bedroom, a kitchen and an office at once.',
    ],
    prefixes: ['apartment', 'first-apartment', 'movein', 'move-out', 'dorm'],
  },
  {
    slug: 'budgeting',
    label: hubLabel('budgeting'),
    title: 'Budget planners and money printables',
    metaDescription:
      'Printable monthly budget templates, 50/30/20 worksheets, bill calendars, paycheck budgets, sinking fund trackers and yearly planners. Instant PDF, $5.',
    lede: 'Monthly templates, 50/30/20 worksheets, bill calendars and sinking fund trackers.',
    intro: [
      'Every budget sheet here answers the same question in a different way: what is actually left. Some are built around the month, some around the payday, some around the three-way split of needs, wants and savings, and one is a single page for a household that has never budgeted before and needs to finish the first attempt.',
      'The bill calendar and the sinking funds tracker cover the other half of the problem, the costs that arrive on a schedule nobody chose: insurance, renewals, repairs, the subscription that renews annually. Funding them a little each month is the difference between a tight month and a crisis.',
      'All of them are PDFs you print and write on, so the month gets entered by hand and the number stays where you can see it.',
    ],
    prefixes: [
      'bill',
      'budget',
      'monthly-template',
      'paycheck',
      'yearly',
      'sinking',
      'planner-grid',
      'investment',
      'simple-monthly-budget',
      'split-budget',
    ],
  },
  {
    slug: 'diet-sheets',
    label: hubLabel('diet-sheets'),
    title: 'Printable food lists and diet charts',
    metaDescription:
      'Printable eat, limit and avoid food lists and diet charts, from low carb and high protein to liver-friendly, gut-friendly, low purine and heart-friendly eating.',
    lede: 'Eat, limit and avoid charts for one way of eating at a time, with the numbers where they matter.',
    intro: [
      'Which foods are worth buying depends on what you are eating for, and a general healthy-eating list does not tell you which. Each sheet takes one way of eating and sorts real foods into eat, limit and avoid: low carb and high protein, liver-friendly and gut-friendly, low purine, low cholesterol, steady blood sugar and folate, plus first foods for a baby starting solids.',
      'Where a number changes the answer, purine milligrams, portion sizes, calorie counts, the sheet carries it rather than leaving it to memory, so the decision in the aisle is a comparison instead of a guess.',
      'These sheets are general food guidance, not personalised advice.',
    ],
    prefixes: [
      'fatty',
      'diverticulitis',
      'gout',
      'glp1',
      'folicacid',
      'lowcarb',
      'heart-health',
      'high-protein',
      'cortisol',
      'baby-first',
      'bloodsugar',
    ],
  },
  {
    slug: 'meal-planning',
    label: hubLabel('meal-planning'),
    title: 'Weekly meal plans and grocery list printables',
    metaDescription:
      'Printable weekly meal plans with aisle-by-aisle grocery lists, plus warehouse club shopping sheets with pack counts and unit prices. Instant PDF, $5.',
    lede: 'A week of meals and the shopping list that follows from it.',
    intro: [
      'Meal planning fails at the shop, not at the table. These sheets carry the week from the menu to the trolley: seven days of breakfast, lunch and dinner written out first, then the produce, meat, dairy, bakery, pantry and freezer lists that follow from them, so nothing in the trolley is there without a meal behind it.',
      'The warehouse sheets are for buying a month at once. Pack counts and unit prices are on the page because the trip that was meant to save money is exactly the trip that ends with a year of something nobody wanted.',
    ],
    prefixes: ['meal-plan', 'grocery', 'warehouse'],
  },
  {
    slug: 'planners',
    label: hubLabel('planners'),
    title: 'Daily and weekly planner printables',
    metaDescription:
      'Printable half-hour daily planners, a half-hour weekly schedule and a plain weekly plan for the few things that have to happen.',
    lede: 'Half-hour daily sheets and a plain weekly plan, for days that are full and days that are not.',
    intro: [
      'There are two kinds of planner here, on purpose. The half-hour sheets rule the day from morning to night, for days that are full and need a shape they can actually fit into. The weekly plan leaves the seven days plain, for the few things that must happen and nothing else.',
      'Neither is a productivity system. They are paper where the day gets written down before it is spent, which is most of the difference between a plan and an intention.',
    ],
    prefixes: ['halfhour', 'weekly-plan'],
  },
  {
    slug: 'household',
    label: hubLabel('household'),
    title: 'Household and cleaning schedule printables',
    metaDescription:
      'Printable household checklists and cleaning schedules split into daily, weekly, monthly and yearly work. Instant PDF, $5.',
    lede: 'Cleaning schedules and household checklists sorted by how often the work comes round.',
    intro: [
      'Housework is a set of jobs with different rhythms, daily, every weekday, monthly, once a year, and most lists mix them together so the Saturday reset looks as long as the spring clean. These sheets separate them by how often the work comes round, which is what makes a routine something you can finish rather than a list you abandon.',
      'The household checklist covers the same ground a second way, by type of work: dishwashing, laundry, floors, windows, bathroom, supplies.',
    ],
    prefixes: ['household', 'cleaning'],
  },
  {
    slug: 'packing',
    label: hubLabel('packing'),
    title: 'Travel and carry-on packing list printables',
    metaDescription:
      'Printable carry-on packing lists and travel checklists that set clothing counts by trip length and group what goes in each bag.',
    lede: 'Clothing counts by trip length, grouped by which bag it goes in.',
    intro: [
      'Packing lists are worth writing once and keeping, because the things people forget are the same every trip: chargers, medication, the one adapter, the paperwork for the rental. These sheets set tops, bottoms, underwear and shoes by trip length, then group the load into the personal item, the clothing, what you wear on the plane and what to check before you zip.',
      'There is a carry-on version for a short trip and a longer-trip checklist for everything that does not fit in one.',
    ],
    prefixes: ['travel', 'carryon'],
  },
]
