using System;

internal static class Program
{
    private static int Main()
    {
        const string currentApiJson =
            "{\"sched_current\":{\"type\":\"Election\",\"songs\":[{\"id\":123,\"title\":\"Dire, Dire Docks\",\"artists\":[{\"id\":1,\"name\":\"Koji Kondo\"}],\"albums\":[{\"id\":2,\"name\":\"Super Mario 64\"}]}]},\"sched_next\":[]}";

        string display;
        if (!VGMRadio.TryParseRainwaveNowPlayingJson(currentApiJson, out display))
        {
            Console.Error.WriteLine("Rainwave current API metadata did not parse.");
            return 1;
        }

        if (!string.Equals(
                display,
                "Now playing: Koji Kondo - Dire, Dire Docks",
                StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Unexpected current API display: " + display);
            return 2;
        }

        const string legacyApiJson =
            "{\"sched_current\":{\"song_data\":{\"title\":\"Stickerbrush Symphony\",\"artists\":[{\"name\":\"David Wise\"}]}},\"sched_next\":[]}";

        if (!VGMRadio.TryParseRainwaveNowPlayingJson(legacyApiJson, out display))
        {
            Console.Error.WriteLine("Rainwave legacy metadata did not parse.");
            return 3;
        }

        if (!string.Equals(
                display,
                "Now playing: David Wise - Stickerbrush Symphony",
                StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Unexpected legacy API display: " + display);
            return 4;
        }

        Console.WriteLine("PASS: Rainwave now-playing metadata parser supports current and legacy response layouts.");
        return 0;
    }
}
