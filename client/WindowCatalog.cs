namespace DarkAges350;

/// <summary>Default on-screen positions for the in-game windows.
/// Most window sprites carry their sub-widget layout internally (see ui-350.json), but the
/// window's own screen position is chosen by the client at open time — these are sensible
/// defaults inside the 640x480 play area, matching the interactive shell. A live client would
/// let the player drag them.</summary>
public static class WindowCatalog
{
    private static readonly Dictionary<string, (int x, int y)> Pos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["stat001.epf"] = (8, 8),
        ["equip01.epf"] = (40, 20),
        ["spell001.epf"] = (352, 2),
        ["skill001.epf"] = (210, 40),
        ["exchange.epf"] = (112, 70),
        ["friend.epf"] = (90, 70),
        ["gset01.epf"] = (90, 70),
        ["macro01.epf"] = (90, 70),
        ["option01.epf"] = (450, 10),
        ["help.epf"] = (90, 30),
        ["legend.epf"] = (90, 30),
        ["users01.epf"] = (84, 70),
    };

    public static IEnumerable<string> Keys => Pos.Keys.Select(k => k.Replace(".epf", ""));

    public static (int x, int y) PositionOf(string asset) =>
        Pos.TryGetValue(asset, out var p) ? p : (60, 40);
}
