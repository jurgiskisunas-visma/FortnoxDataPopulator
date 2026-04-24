namespace FortnoxDataPopulator.Services;

public static class NameGenerator
{
    private static readonly Random Rng = new Random();

    private static readonly string[] Adjectives =
    {
        "Flying", "Dancing", "Grumpy", "Jolly", "Quantum", "Cosmic", "Tiny", "Mighty",
        "Silent", "Thunderous", "Purple", "Emerald", "Golden", "Crimson", "Electric",
        "Mystical", "Sneaky", "Chubby", "Regal", "Reluctant", "Overcaffeinated",
        "Existential", "Pragmatic", "Unreasonable", "Suspicious", "Whimsical",
    };

    private static readonly string[] Critters =
    {
        "Panda", "Falcon", "Octopus", "Narwhal", "Platypus", "Wombat", "Badger",
        "Penguin", "Otter", "Sloth", "Dragon", "Phoenix", "Unicorn", "Kraken",
        "Goblin", "Capybara", "Lemur", "Llama", "Alpaca", "Hedgehog", "Raccoon",
        "Moose", "Ferret", "Pigeon", "Axolotl",
    };

    private static readonly string[] Suffixes =
    {
        "AB", "HB", "Consulting", "Industries", "Holdings", "Solutions", "Studios",
        "Ventures", "Collective", "Guild", "Syndicate", "& Co", "Partners",
        "International", "Group",
    };

    private static readonly string[] FirstNames =
    {
        "Bob", "Alice", "Ingrid", "Olof", "Greta", "Magnus", "Astrid", "Sven",
        "Linnea", "Gunnar", "Sigrid", "Harald", "Freja", "Erik", "Maja",
    };

    private static readonly string[] LastNames =
    {
        "Svensson", "Andersson", "Johansson", "Lindqvist", "Bergström", "Holmberg",
        "Nordin", "Ekström", "Forsberg", "Dahl", "Lundgren", "Åkesson", "Sjöberg",
    };

    private static readonly string[] ServiceAdjectives =
    {
        "Artisanal", "Premium", "Emergency", "Handcrafted", "Quarterly", "Monthly",
        "Enchanted", "Deluxe", "Organic", "Gourmet", "Ceremonial", "Tactical",
        "Boutique", "Industrial-grade", "Small-batch", "Freshly-pressed",
    };

    private static readonly string[] Services =
    {
        "cat herding", "unicorn audit", "spreadsheet crafting", "llama grooming",
        "cloud polishing", "SEO sprinkles", "vibe realignment", "meeting recovery",
        "buzzword curation", "deadline massage", "keyboard calibration",
        "synergy extraction", "pivot facilitation", "dashboard feng shui",
        "bug whispering", "standup therapy", "roadmap divination",
    };

    private static readonly string[] Goods =
    {
        "artisanal paperclips", "handmade post-it notes", "free-range coffee beans",
        "small-batch printer ink", "organic whiteboard markers", "gluten-free toner",
        "ethically-sourced rubber bands", "vintage stapler refills",
        "premium office plants", "ambient office lighting kit",
    };

    private static readonly string[] Notes =
    {
        "Thanks for the business!", "Payment terms: cash, beans, or vibes.",
        "Please remit before the next full moon.", "Signed, your humble supplier.",
        "Delivery was scheduled but the pigeons unionised.",
        "Rendered while under the influence of coffee.",
        "Any resemblance to real services is purely coincidental.",
        "Ask for our loyalty program: we remember faces.",
        "Now with 40% more synergy.", "No llamas were harmed.",
    };

    public static string CompanyName() =>
        $"{Pick(Adjectives)} {Pick(Critters)} {Pick(Suffixes)}";

    public static string PersonName() =>
        $"{Pick(FirstNames)} {Pick(LastNames)}";

    public static string ShortTag()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        Span<char> chars = stackalloc char[5];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[Rng.Next(alphabet.Length)];
        }

        return new string(chars);
    }

    public static string CustomerRowDescription(int index) =>
        $"{Pick(ServiceAdjectives, index)} {Pick(Services, index * 7 + 1)}";

    public static string SupplierRowDescription(int index) =>
        $"{Pick(ServiceAdjectives, index + 3)} {Pick(Goods, index * 5 + 2)}";

    public static string InvoiceComment(int index) => Pick(Notes, index);

    public static string CustomerName() =>
        Rng.Next(3) == 0 ? PersonName() : CompanyName();

    private static string Pick(string[] source) =>
        source[Rng.Next(source.Length)];

    private static string Pick(string[] source, int seed) =>
        source[Math.Abs(seed) % source.Length];
}
