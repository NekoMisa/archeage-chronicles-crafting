using System.Text.Json;

namespace CraftingCalculator;

internal sealed record TradePack(string Name, string Slug, string Origin, string Company, int? Fee,
    Dictionary<string, int> Ingredients, TradePayout[] Routes);

internal sealed record TradePayout(string Destination, string Reward, int Amount);

internal sealed record TradeRoute(TradePack Pack, TradePayout Payout);

internal sealed record TradeLeg(TradeRoute Route, long Packs, bool AlreadyPacked = false)
{
    public long Amount => Packs * Route.Payout.Amount;
    public long? Fee => AlreadyPacked ? 0L : Packs * Route.Pack.Fee;
}

internal sealed record TradeOption(TradeLeg First, TradeLeg? Second = null)
{
    public TradeLeg Last => Second ?? First;
    public string Reward => Last.Route.Payout.Reward;
    public long Amount => Last.Amount;
    public long Packs => First.Packs + (Second?.Packs ?? 0);
    public long? Fee => Second is null ? First.Fee : First.Fee + Second.Fee;
}

internal sealed class TradeCatalog
{
    public const string Gold = "Gold";
    public const string SourceUrl = "https://raiddex.com/en/archeage-chronicles/trade-routes";
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    private readonly TradeRoute[] routes;
    private readonly ILookup<string, TradeRoute> byReward;
    private readonly ILookup<string, TradeRoute> byIngredient;

    private TradeCatalog(TradePack[] packs)
    {
        routes = packs.SelectMany(pack => pack.Routes.Select(payout => new TradeRoute(pack, payout))).ToArray();
        byReward = routes.ToLookup(route => route.Payout.Reward, Names);
        byIngredient = routes.SelectMany(route => route.Pack.Ingredients.Keys.Select(item => (item, route)))
            .ToLookup(entry => entry.item, entry => entry.route, Names);
        Rewards = byReward.Select(group => group.Key).OrderBy(name => name, Names).ToArray();
        Inputs = packs.Select(pack => pack.Name).Concat(byIngredient.Select(group => group.Key))
            .Distinct(Names).OrderBy(name => name, Names).ToArray();
    }

    public string[] Rewards { get; }
    public string[] Inputs { get; }

    public static TradeCatalog Load(string path)
    {
        var packs = JsonSerializer.Deserialize<TradePack[]>(File.ReadAllText(path), JsonSerializerOptions.Web);
        if (packs is null || packs.Length == 0 || packs.Any(pack => pack is null
            || string.IsNullOrWhiteSpace(pack.Name) || string.IsNullOrWhiteSpace(pack.Slug)
            || !pack.Slug.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            || string.IsNullOrWhiteSpace(pack.Origin) || string.IsNullOrWhiteSpace(pack.Company)
            || pack.Fee < 0 || pack.Ingredients is null
            || pack.Ingredients.Any(input => string.IsNullOrWhiteSpace(input.Key) || input.Value < 1)
            || pack.Ingredients.Keys.Distinct(Names).Count() != pack.Ingredients.Count
            || pack.Routes is null || pack.Routes.Length == 0 || pack.Routes.Any(route => route is null
                || route.Amount < 1 || string.IsNullOrWhiteSpace(route.Reward)
                || string.IsNullOrWhiteSpace(route.Destination) || Names.Equals(route.Destination, pack.Origin))))
            throw new InvalidDataException("Expected trade goods with positive quantities and valid routes.");

        return new TradeCatalog(packs.Select(pack => pack with { Ingredients = new(pack.Ingredients, Names) }).ToArray());
    }

    public static string PackUrl(TradePack pack) =>
        $"https://raiddex.com/en/archeage-chronicles/trade-goods/{pack.Slug}";

    public TradeOption[] Get(string reward, long quantity, bool includeChains) => Find(reward, quantity, includeChains)
        .OrderBy(option => option.Second is not null)
        .ThenBy(option => option.Packs).ThenBy(option => option.Fee ?? long.MaxValue)
        .ThenBy(option => option.Amount).ToArray();

    private IEnumerable<TradeOption> Find(string reward, long quantity, bool includeChains)
    {
        foreach (var route in byReward[reward])
        {
            var final = new TradeLeg(route, PacksFor(quantity, route.Payout.Amount));
            yield return new TradeOption(final);
            if (!includeChains)
                continue;

            foreach (var (item, amount) in route.Pack.Ingredients)
            foreach (var first in byReward[item])
            {
                if (Names.Equals(item, reward) || first.Pack == route.Pack || Ingredient(first.Pack, reward) > 0)
                    continue;

                yield return new TradeOption(new TradeLeg(first, PacksFor(final.Packs * amount, first.Payout.Amount)), final);
            }
        }
    }

    public TradeOption[] Offer(string item, long quantity, bool includeChains) => Available(item, quantity, includeChains)
        .OrderBy(option => option.Reward != Gold).ThenBy(option => option.Reward, Names)
        .ThenByDescending(option => option.Reward == Gold ? option.Amount - (option.Fee ?? 0) : option.Amount)
        .ThenBy(option => option.Second is not null).ThenBy(option => option.Fee ?? long.MaxValue)
        .ThenBy(option => option.Packs).ToArray();

    public int MinimumToTrade(string item) => routes.Select(route => InputPerPack(route.Pack, item))
        .Where(amount => amount > 0).DefaultIfEmpty().Min();

    private IEnumerable<TradeOption> Available(string item, long quantity, bool includeChains)
    {
        foreach (var route in routes)
        {
            var input = InputPerPack(route.Pack, item);
            if (input == 0 || quantity < input)
                continue;

            var packed = Names.Equals(route.Pack.Name, item);
            var first = new TradeLeg(route, quantity / input, packed);
            yield return new TradeOption(first);
            if (!includeChains || Names.Equals(route.Payout.Reward, item))
                continue;

            foreach (var second in byIngredient[route.Payout.Reward])
            {
                if (second.Pack == route.Pack || Names.Equals(second.Payout.Reward, item)
                    || Names.Equals(second.Payout.Reward, route.Payout.Reward))
                    continue;

                var intermediate = Ingredient(second.Pack, route.Payout.Reward);
                var additional = Ingredient(second.Pack, item);
                var packs = ChainCapacity(first, quantity, input, intermediate, additional);
                if (packs > 0)
                {
                    var needed = PacksFor(packs * intermediate, route.Payout.Amount);
                    yield return new TradeOption(first with { Packs = needed }, new TradeLeg(second, packs));
                }
            }
        }
    }

    private static long ChainCapacity(TradeLeg first, long quantity, int input, int intermediate, int additional)
    {
        var max = first.Amount / intermediate;
        if (additional == 0)
            return max;

        // The starting item can be required by both packs. Reserve enough for each leg.
        var min = 0L;
        max = Math.Min(max, quantity / additional);
        while (min < max)
        {
            var candidate = min + (max - min + 1) / 2;
            var firstPacks = PacksFor(candidate * intermediate, first.Route.Payout.Amount);
            if (firstPacks * input <= quantity - candidate * additional)
                min = candidate;
            else
                max = candidate - 1;
        }
        return min;
    }

    private static int InputPerPack(TradePack pack, string item) => Names.Equals(pack.Name, item) ? 1 : Ingredient(pack, item);

    private static int Ingredient(TradePack pack, string item) => pack.Ingredients.GetValueOrDefault(item);

    private static long PacksFor(long quantity, int output) => 1 + (quantity - 1) / output;
}
