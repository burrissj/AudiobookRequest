namespace AudiobookRequest.Core.Recommendations;

/// <summary>How a category is drawn on the For You page. The mix is what keeps the page from looking like one repeated grid.</summary>
public enum CategoryLayout
{
    /// <summary>Three covers across with listening time underneath.</summary>
    Grid,
    /// <summary>A diagonal, tilted mosaic of covers -- the hero treatment.</summary>
    Tilted,
    /// <summary>A horizontally scrolling row of larger covers.</summary>
    Shelf,
}

/// <param name="AudibleCategoryId">Audible's own genre id (from its /catalog/categories tree); narrows results to that genre.</param>
/// <param name="Keywords">Extra search words, alone or on top of the genre id.</param>
/// <param name="Sort">An Audible products_sort_by value: BestSellers, -ReleaseDate or Relevance.</param>
/// <param name="Tags">Synonyms that make the category findable from the search box ("scary" finds Spooky Reads).</param>
public record BrowseCategory(
    string Id,
    string Title,
    string Description,
    CategoryLayout Layout,
    string? AudibleCategoryId = null,
    string? Keywords = null,
    string Sort = "BestSellers",
    IReadOnlyList<string>? Tags = null,
    string? Author = null)
{
    public const string CustomId = "q";
    public const string AuthorId = "a";

    /// <summary>Everything by one author, shown with the same screens as a curated category.</summary>
    public static BrowseCategory ForAuthor(string name) =>
        new(AuthorId, name.Trim(), $"Audiobooks by {name.Trim()}.", CategoryLayout.Grid, null, null, "BestSellers", null, name.Trim());

    /// <summary>An on-the-fly category for a free-text search ("dragons"), shown with the same screens as a curated one.</summary>
    public static BrowseCategory Custom(string text) =>
        new(CustomId, text.Trim(), $"Audiobooks matching \"{text.Trim()}\".", CategoryLayout.Grid, null, text.Trim(), "Relevance");
}

/// <summary>
/// The curated browse categories behind the For You page. Genre ids come from Audible's public category
/// tree, so results are real genre members rather than keyword guesses; a few lean on keywords instead
/// (LitRPG, full-cast productions) where no genre exists.
/// </summary>
public static class CategoryCatalog
{
    public static readonly IReadOnlyList<BrowseCategory> All =
    [
        new("romantasy", "Romantasy", "Fae courts, dragon riders and slow-burn magic.", CategoryLayout.Tilted, "18580523011", "romantasy", "Relevance", ["romance", "fantasy", "fae", "spicy"]),
        new("epic-fantasy", "Epic Fantasy", "Sprawling worlds, ancient magic and wars that decide kingdoms.", CategoryLayout.Grid, "18580615011", null, "BestSellers", ["fantasy", "magic", "sword", "quest"]),
        new("dragons", "Dragons & Mythical Creatures", "Wings, scales and things that go bump in the myth.", CategoryLayout.Shelf, "18580613011", null, "BestSellers", ["fantasy", "dragon", "creatures", "magic"]),
        new("sword-sorcery", "Sword & Sorcery", "Fast, gritty adventures with a blade in one hand and a spell in the other.", CategoryLayout.Grid, "18580627011", null, "BestSellers", ["fantasy", "barbarian", "adventure"]),
        new("fairy-tales", "Fairy Tale Retellings", "Old stories, new teeth.", CategoryLayout.Shelf, "18580616011", null, "BestSellers", ["fantasy", "retelling", "folk"]),
        new("litrpg", "LitRPG & Progression", "Levels, stats and heroes who just keep getting stronger.", CategoryLayout.Grid, "18580606011", "litrpg", "BestSellers", ["gaming", "progression", "fantasy", "cultivation"]),
        new("space-opera", "Space Opera", "Starships, empires and the long dark between worlds.", CategoryLayout.Grid, "18580645011", null, "BestSellers", ["science fiction", "sci-fi", "space", "aliens"]),
        new("military-sf", "Military Sci-Fi", "Boots on the ground, on every planet you can name.", CategoryLayout.Shelf, "18580641011", null, "BestSellers", ["science fiction", "sci-fi", "war", "soldiers"]),
        new("dystopian", "Dystopian Worlds", "Futures that went wrong and the people who push back.", CategoryLayout.Tilted, "18580635011", null, "BestSellers", ["science fiction", "sci-fi", "future", "ya"]),
        new("post-apocalyptic", "After the End", "Survivors, ruins and what comes next.", CategoryLayout.Grid, "18580643011", null, "BestSellers", ["science fiction", "sci-fi", "apocalypse", "survival", "zombies"]),
        new("time-travel", "Time Travel", "Change the past, risk the future.", CategoryLayout.Shelf, "18580647011", null, "BestSellers", ["science fiction", "sci-fi", "history"]),
        new("cyberpunk", "Cyberpunk", "Neon, chrome and corporations that own everything.", CategoryLayout.Grid, "18580633011", null, "BestSellers", ["science fiction", "sci-fi", "hacker", "ai"]),
        new("cozy-mystery", "Cozy Mysteries", "Small towns, tea, and a body in the library.", CategoryLayout.Shelf, "18574609011", null, "BestSellers", ["mystery", "cozy", "amateur", "murder", "light"]),
        new("psychological-thrillers", "Psychological Thrillers", "Nobody is telling you everything.", CategoryLayout.Grid, "18574631011", null, "BestSellers", ["thriller", "suspense", "twist", "mind"]),
        new("domestic-thrillers", "Domestic Thrillers", "The scariest place is home.", CategoryLayout.Grid, "18574624011", null, "BestSellers", ["thriller", "suspense", "family", "marriage"]),
        new("police-procedurals", "Police Procedurals", "Detectives, evidence and the long grind to the truth.", CategoryLayout.Shelf, "18574617011", null, "BestSellers", ["mystery", "crime", "detective", "cops"]),
        new("true-crime", "True Crime", "Real cases, real consequences.", CategoryLayout.Tilted, "18572017011", null, "BestSellers", ["crime", "murder", "nonfiction", "documentary"]),
        new("spooky", "Spooky Reads", "Spook yourself with one of these titles.", CategoryLayout.Tilted, "18574490011", null, "BestSellers", ["horror", "scary", "ghost", "halloween", "creepy"]),
        new("gothic", "Gothic & Haunted", "Crumbling manors, restless dead, and secrets in the walls.", CategoryLayout.Grid, "18574493011", null, "BestSellers", ["horror", "ghost", "haunted", "victorian"]),
        new("historical-fiction", "Historical Fiction", "Step into another century.", CategoryLayout.Grid, "18574482011", null, "BestSellers", ["history", "historical", "war", "period"]),
        new("classics", "Classics", "The books everyone says you should have read.", CategoryLayout.Shelf, "18574449011", null, "BestSellers", ["literature", "old", "timeless", "required"]),
        new("contemporary-romance", "Contemporary Romance", "Modern love, meet-cutes and happily ever afters.", CategoryLayout.Grid, "18580522011", null, "BestSellers", ["romance", "love", "modern"]),
        new("romcom", "Romantic Comedy", "Banter first, feelings later.", CategoryLayout.Shelf, "18580535011", null, "BestSellers", ["romance", "funny", "comedy", "love", "light"]),
        new("paranormal-romance", "Paranormal Romance", "Vampires, shifters and love that bites.", CategoryLayout.Tilted, "18580534011", null, "BestSellers", ["romance", "vampire", "werewolf", "shifter", "supernatural"]),
        new("romantic-suspense", "Romantic Suspense", "Danger brings them together. Falling for each other is the real risk.", CategoryLayout.Grid, "18580536011", null, "BestSellers", ["romance", "thriller", "suspense"]),
        new("regency", "Regency Romance", "Balls, scandal and very proper rules broken.", CategoryLayout.Shelf, "18580528011", null, "BestSellers", ["romance", "historical", "bridgerton", "period"]),
        new("ya-fantasy", "Young Adult Fantasy", "Chosen ones, secret schools and first loves.", CategoryLayout.Grid, "18581049011", null, "BestSellers", ["ya", "teen", "fantasy", "young adult"]),
        new("ya-scifi", "Young Adult Sci-Fi", "Big futures for the next generation.", CategoryLayout.Shelf, "18581062011", null, "BestSellers", ["ya", "teen", "science fiction", "sci-fi", "young adult"]),
        new("humor", "Humor & Satire", "Listen at full volume, ideally in public.", CategoryLayout.Grid, "18574496011", null, "BestSellers", ["funny", "comedy", "satire", "laugh"]),
        new("biographies", "Biographies & Memoirs", "Lives told by the people who lived them.", CategoryLayout.Shelf, "18571951011", null, "BestSellers", ["nonfiction", "memoir", "autobiography", "life"]),
        new("full-cast", "Full-Cast Productions", "Many voices, sound effects, a movie for your ears.", CategoryLayout.Shelf, "18580606011", "full cast", "Relevance", ["dramatized", "graphic audio", "performance", "production"]),
        new("graphic-audio", "Graphic Audio", "Cinematic, multi-voice adaptations.", CategoryLayout.Grid, null, "graphic audio", "Relevance", ["dramatized", "full cast", "cinematic", "production"]),
    ];

    public static BrowseCategory? Find(string id) => All.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Case-insensitive match of every typed word against the title, description and tags.</summary>
    public static IReadOnlyList<BrowseCategory> Search(string query)
    {
        var words = query.Split([' ', ',', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return [];
        }

        return All
            .Where(c =>
            {
                var haystack = $"{c.Title} {c.Description} {string.Join(' ', c.Tags ?? [])}";
                return words.All(w => haystack.Contains(w, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    /// <summary>
    /// A stable-per-seed random pick (same seed, same page; a new seed is a "shuffle"), shaped so the page
    /// always has some variety: at least one tilted hero and one shelf when enough exist, and never more
    /// than two tilted mosaics.
    /// </summary>
    public static IReadOnlyList<BrowseCategory> PickForHome(int seed, int count)
    {
        var rng = new Random(seed);
        var shuffled = All.OrderBy(_ => rng.Next()).ToList();
        var picked = new List<BrowseCategory>();

        void TakeFirst(CategoryLayout layout)
        {
            var match = shuffled.FirstOrDefault(c => c.Layout == layout && !picked.Contains(c));
            if (match is not null && picked.Count < count)
            {
                picked.Add(match);
            }
        }

        TakeFirst(CategoryLayout.Tilted);
        TakeFirst(CategoryLayout.Shelf);

        foreach (var category in shuffled.Where(c => !picked.Contains(c)))
        {
            if (picked.Count >= count)
            {
                break;
            }
            if (category.Layout == CategoryLayout.Tilted && picked.Count(c => c.Layout == CategoryLayout.Tilted) >= 2)
            {
                continue;
            }
            picked.Add(category);
        }

        // The tilted cap is for variety, not a reason to come up short when more were asked for.
        foreach (var category in shuffled.Where(c => !picked.Contains(c)).Take(Math.Max(0, count - picked.Count)))
        {
            picked.Add(category);
        }

        // Lead with a regular section and put the first tilted mosaic second, so the page opens calmly and
        // the hero treatment lands right under the fold.
        var order = picked.OrderBy(_ => rng.Next()).ToList();
        var hero = order.FirstOrDefault(c => c.Layout == CategoryLayout.Tilted);
        if (hero is not null && order.Count > 1)
        {
            order.Remove(hero);
            order.Insert(order.Count > 1 && order[0].Layout != CategoryLayout.Tilted ? 1 : 0, hero);
        }

        var firstRegular = order.FindIndex(c => c.Layout != CategoryLayout.Tilted);
        if (order.Count > 1 && order[0].Layout == CategoryLayout.Tilted && firstRegular > 0)
        {
            (order[0], order[firstRegular]) = (order[firstRegular], order[0]);
        }
        return order;
    }
}
