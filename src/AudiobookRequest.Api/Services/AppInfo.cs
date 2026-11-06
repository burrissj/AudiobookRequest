using System.Reflection;

namespace AudiobookRequest.Api.Services;

public record ChangelogEntry(string Version, string Date, IReadOnlyList<string> Items);

public record VersionInfo(string Version, IReadOnlyList<ChangelogEntry> Changelog);

/// <summary>The running version (stamped in by the publish script from the installer's version) and what changed in each release.</summary>
public static class AppInfo
{
    public static string Version { get; } = ReadVersion();

    // The informational version carries a "+commit" suffix from the SDK; users only need the number.
    private static string ReadVersion()
    {
        var informational = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }

    /// <summary>Newest first. Add an entry here whenever the installer version is bumped.</summary>
    public static readonly IReadOnlyList<ChangelogEntry> Changelog =
    [
        new("0.21.0", "2026-11-03",
        [
            "Admins can now fix a book's details. Open a book from the Listen or Library tab and tap \"Edit details\" to set its author, put it in a series (with its number in the series), or mark it as not part of any series. Your correction beats what Jellyfin, Audible and the title say, applies for everyone, and survives Audible being asked again. The boxes suggest the authors and series already in the library so a name isn't retyped differently. \"Reset to automatic\" removes it.",
            "A book's page now shows its series and number (for example \"The Mistborn Saga · Book 2\") under the author.",
            "To find books that need fixing, open Library > Series and look at \"Not in a series\", or Library > Authors and look at \"Unknown author\".",
        ]),
        new("0.20.0", "2026-11-03",
        [
            "Series in the Library now come from Audible instead of only the book titles. In the background the app looks up each book in Audible's catalog and remembers which series it is in and its number, so a series is listed in the right order even when the titles don't say. It only accepts a match when the title and author are the same book. While this first pass runs (a few books at a time, so a big library takes a while) the Series list says how many books are still being looked up and fills in by itself; books it can't find on Audible are still grouped by what their titles say.",
            "Authors with a middle initial no longer split into two: \"Amber Nicole\" and \"Amber V. Nicole\" (and \"J.R. Ward\" and \"J. R. Ward\") are one author in the Library's Authors list. Different first names stay separate.",
            "When Audible lists a book in several series, such as a whole universe (\"The Cosmere\") and the numbered series (\"The Mistborn Saga\", book 1), the numbered series is used. This also corrects the series shown in Browse.",
        ]),
        new("0.19.0", "2026-11-03",
        [
            "The Library can now be browsed by Authors or by Series. Pick Authors or Series next to A to Z, and you get a list with a small stack of covers and how many books each has (and how many you have finished); tap one to see its books. A series lists its books in order (\"Book 1\", \"Book 2\"), and an author's books come series by series. The phone's back button returns to the list, and the search box finds an author or series by name.",
            "Series are worked out from the book titles, such as \"Title (Series, #2)\" or \"Title: Series, Book 2\", because Jellyfin doesn't keep series information for audiobooks. Books whose title doesn't say go under \"Not in a series\"; books with no author in Jellyfin go under \"Unknown author\".",
        ]),
        new("0.18.1", "2026-11-03",
        [
            "A book's page on the Listen and Requests tabs now finds its description far more reliably and quickly. Books with no author in Jellyfin used to get no lookup at all; they are now looked up by title. A title like \"Name (Unabridged)\" is retried without the extra words, Jellyfin's own description is shown straight away when it has one, a description already found is kept for the rest of the visit, and the lookup starts the moment a book is touched. When nothing can be found the page now says so instead of staying blank.",
        ]),
        new("0.18.0", "2026-11-03",
        [
            "Tapping a book on the Listen tab now opens its page instead of starting it playing: the description, narrator and length, your own star rating, your tags and your bookmark notes, with the Play (or Resume) button on it.",
            "The Requests tab opens the same kind of page for a request: what the book is about, who asked for it and when, and a Requested, Searching, Downloading, Processing, Ready tracker showing how far along it is, next to the download progress and the usual buttons.",
            "Listen is now split in two: Listening (Continue, and the books saved on this device) and Library (everything, with A to Z, Recently added, Rated, Finished and your tags). A Grid or Shelf switch on both changes how the books are laid out; Shelf stands the covers on wooden shelves like a bookcase, and the choice is remembered on that device.",
        ]),
        new("0.17.3", "2026-10-28",
        [
            "Fixed requests that looped on \"Chaptarr no longer recognizes foreign book id\". When Chaptarr can't find a book by the id saved with the request, the app now looks it up again by title and author and switches to the new id if it finds the same book. If it can't, the request fails after 15 minutes (even if it was set to keep trying) so \"Retry with different search\" is offered, instead of retrying every 20 seconds forever.",
        ]),
        new("0.17.2", "2026-10-28",
        [
            "Fixed requests that stayed stuck on \"Couldn't add this to Chaptarr yet: Object reference not set to an instance of an object\" for some books. When Chaptarr already has the book, the app now uses that entry instead of adding it a second time, and it copes with the different replies Chaptarr can give when it adds one.",
        ]),
        new("0.17.1", "2026-10-28",
        [
            "When adding a book to Chaptarr fails, the request now says why, using Chaptarr's own words, instead of a bare \"Object reference not set to an instance of an object\". A missing root folder setting, a reply from Chaptarr with no book id, and Chaptarr refusing the book are each named, and a programming error shows where it happened.",
        ]),
        new("0.17.0", "2026-10-28",
        [
            "Books that aren't out yet no longer fail a day later. Requesting one now stops and asks you to confirm first, naming the release date and saying nothing can be downloaded until then. Unreleased books are tagged with \"Not out yet\" and their date on search results and the book page, and \"Out Nov 5\" on covers in Browse.",
            "A request for an unreleased book waits in its own Upcoming releases list on the Requests tab (and an Upcoming filter in Admin), with its release date on it. It is checked on a slow schedule in case it turns up early or was mis-dated: weekly when it is months away, daily in the last two weeks, every six hours in the last three days. Check now looks straight away.",
            "On the release date it moves into the normal flow as a keep-trying request, and searches speed up: every 30 minutes for the first two days, every 2 hours to the end of the first week, every 6 hours to the end of the first month, then your usual keep-trying pace.",
        ]),
        new("0.16.0", "2026-10-22",
        [
            "Safer to use away from home. Sign-in now locks for up to 15 minutes after repeated wrong passwords (five wrong guesses for one account from one device, or more from many), so nobody can sit and guess a password.",
            "Closed a gap in setup: the Settings tab's \"test this address\" buttons could be used by anyone who could reach the site to make the server connect to addresses on your network. They now need an admin sign-in, and first-time setup is only open from the home network.",
            "Other websites can no longer make the app do things on your behalf, and the app now sends the standard browser security headers (no framing, a strict content policy, HTTPS-only once seen over HTTPS).",
            "New guide in the project's docs folder, remote-access.md, for reaching the app away from home with Tailscale connecting automatically on mobile data and Wi-Fi other than your own.",
        ]),
        new("0.15.0", "2026-10-13",
        [
            "The Listen tab now looks like For You: your books are a grid of covers, three across, instead of one long list. Each cover shows how far through you are, your star rating, and a tick once it's finished, with the title, author and time left underneath. The download button is now a small icon on the cover.",
            "Listen opens on a Continue listening section with a Recently added shelf you can swipe sideways, and See all for the full list. Every view shows 18 books at a time with a Show more button, so the page stays short.",
            "The Requests tab is a grid too, grouped into Needs attention, Awaiting approval, On its way and Ready to listen. Each cover has a status on it (Downloading 42%, Failed, Ready). Tap a cover to open its details and actions (search again, cancel, approve, fix import, play) in a panel from the bottom.",
            "Books you've downloaded to your phone (Listen > Downloaded) use the same grid.",
        ]),
        new("0.14.0", "2026-10-13",
        [
            "Admin > Import now has a conversion queue. Pick a book, press Import, and it joins the queue and you move straight on to identifying the next one while the earlier ones convert in the background. Books convert one at a time, in the order you queued them.",
            "The queue shows what is converting, what is waiting (and its place in line) and how each one ended, with the filed location or the reason it failed. You can stop the one converting, take a waiting one out of line, try a failed one again, or clear the finished ones.",
            "Fixed Stop not really stopping: pressing it used to leave ffmpeg quietly carrying on and then report a confusing \"Failed\" error. It now stops the conversion, shows Cancelled, and leaves nothing behind.",
        ]),
        new("0.13.0", "2026-10-13",
        [
            "New Admin > Import tool: point it at a folder of audiobooks you got from elsewhere and work through them one at a time. Pick the right book from Audible's list (or type the details), and each one is converted to a single .m4b with the title, author, series, cover art and chapters written in, filed in your library the same way as everything else, and Jellyfin is told to rescan. The original files are never deleted.",
            "Fixed \"Jellyfin hasn't picked this book up yet\" showing for books that are already in Jellyfin, which happened when the series was written differently (for example \"Gods & Monsters\" and \"Gods and Monsters\", or a title with the series number added). The same fix makes \"already in your library\" more accurate.",
            "Finished requests whose book has since been deleted from Jellyfin can now be cleaned up. Each finished request has a Remove from list button, and Admin > Health has a Clean up the Requests list panel that checks Jellyfin and removes the ones that are gone in one go. The audiobook files are never touched, and the book can be requested again.",
            "Quieter logs: a brief network hiccup reaching Audible or another service is now logged as one short line instead of a long error with a stack trace.",
        ]),
        new("0.12.1", "2026-10-12",
        [
            "iPhone: the Play button on the lock screen now resumes playback without unlocking the phone. Pause already worked; Play now does too.",
        ]),
        new("0.12.0", "2026-10-09",
        [
            "A proper playback-speed picker: tap the speed in the player to see your current speed, one-tap presets (0.50x up to 3.00x) and a fine-tune slider that moves in 0.05x steps, for the speed that's just right.",
            "The - and + buttons still move in quarter-speed steps, and from a fine-tuned speed they land on the next familiar one (1.15x goes to 1.25x).",
        ]),
        new("0.11.0", "2026-10-09",
        [
            "Star ratings: rate any book 1 to 5 stars from the player. Your ratings are yours alone, show next to each book on the Listen tab, and there is a Rated view that lists your favourites first.",
            "Tags: label books however you like (Favorites, Re-listen, Great narrator). Your tags appear as one-tap filters on the Listen tab, and the player suggests the ones you already use.",
            "Bookmarks: save a place in a book with one tap, add a note, and jump back to it later. Bookmarks made with no signal are kept on your phone and added when you're back online.",
            "Long titles now slide sideways on the Listen tab so you can read the whole name, which tells apart the parts of a multi-part book that share a cover.",
        ]),
        new("0.10.1", "2026-10-09",
        [
            "Fixed the bottom navigation bar disappearing after switching away from Browse and back.",
            "On a phone, opening Browse no longer pops the keyboard up by itself.",
        ]),
        new("0.10.0", "2026-10-09",
        [
            "Listen offline: tap the download button on a book to save it on your phone, then play it with no signal, on the plane or off the VPN. When the server can't be reached the app now says so and offers your saved books, instead of dropping you on the setup screen.",
            "Books Jellyfin stores in a format browsers can't play (such as E-AC3) are now converted on the fly and play in the app.",
            "Up next: when a book in a series ends, the next one starts by itself (you can turn that off under Player settings, which also lets you choose the skip lengths and how far to step back when you resume).",
            "Your listening stats: time listened today, this week and this year, your streak, a 30-day chart, your most-listened books and authors, and the time saved by listening faster.",
            "A short \"What's new\" notice after each update, a clear sync status in the player, and signing out now also ends your session in Jellyfin.",
            "Better on iPhone: the screen no longer jumps when the keyboard opens, the clock stays readable in the home-screen app, and fields no longer zoom in when tapped.",
        ]),
        new("0.9.0", "2026-10-08",
        [
            "Listen in the app: the new Listen tab plays your Jellyfin audiobooks right here, with no separate Jellyfin sign-in. Pick up where you left off (your place stays in sync with Jellyfin), jump between chapters, skip 30 seconds, and set a sleep timer.",
            "Playback speed from 0.25x to 3x in quarter steps, with a - and + button; the speed is remembered per book. Lock-screen and headset controls work too.",
            "A Play here button on finished requests and owned books. Books Jellyfin stores in a format browsers can't play (such as E-AC3) offer Open in Jellyfin instead.",
            "Everyone needs to sign out and back in once so the app can listen as them.",
        ]),
        new("0.8.0", "2026-10-08",
        [
            "Open in Jellyfin: a link in the header, and a Listen button on every finished book that goes straight to that book. Set a \"Jellyfin address for users\" in Settings so the links work away from home.",
            "Personal notifications: each person can get their own alerts by ntfy, email or Telegram from the new Account page, on top of the shared household channels.",
            "A setup checklist at the top of Admin → Health shows what is still needed (and what is optional) in plain words.",
        ]),
        new("0.7.1", "2026-10-08",
        [
            "Works behind an HTTPS reverse proxy such as Nginx Proxy Manager: a proxy on your home network is now believed, so sign-in cookies are marked Secure.",
            "Admin → Health shows how you're connected (HTTPS or plain HTTP) and flags a proxy the app doesn't trust.",
        ]),
        new("0.7.0", "2026-10-07",
        [
            "Follow authors and series: get told about new books, and optionally have them requested automatically.",
            "Author pages: tap an author on any book to see everything they have written.",
            "Auto-fix imports (opt-in): blocked downloads that only matched a sibling edition are imported without waiting for an admin.",
            "Daily database backups, with download from the Admin tab, and this version and changelog page.",
            "Groundwork for installing the app on a phone's home screen (needs HTTPS to take effect).",
        ]),
        new("0.6.1", "2026-10-06",
        [
            "Fix import: one-click import of a download Chaptarr refused to import because it matched a different edition.",
        ]),
        new("0.6.0", "2026-10-06",
        [
            "New For You page: categories with grid, tilted-mosaic and shelf layouts, category pages and category search.",
        ]),
        new("0.5.0", "2026-10-02",
        [
            "Approval queue and weekly request limits.",
            "Notifications to Discord, Telegram, ntfy, a webhook or email.",
            "Admin tab with all requests, a health page and a log viewer.",
            "Failure diagnostics on stuck requests, wishlist (keep trying), search again, series completion, narrator and length filters.",
        ]),
        new("0.4.x", "2026-09-30",
        [
            "Processing switches for moving, renaming and tagging, library rescans, and the \"already in your library\" check.",
        ]),
    ];
}
