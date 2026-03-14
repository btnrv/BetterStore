using System.Collections.Generic;

namespace BetterStore.Items;

internal static class ItemTeamHelper
{
    public const int AnyTeam = 1;
    public const int Terrorist = 2;
    public const int CounterTerrorist = 3;

    public static int ResolvePlayerskinTeamSlot(Dictionary<string, string> item)
    {
        if (item.TryGetValue("team", out string? teamRaw) && TryParseTeamValue(teamRaw, out int team))
            return team;

        if (item.TryGetValue("slot", out string? slotRaw) && TryParseTeamValue(slotRaw, out team))
            return team;

        return AnyTeam;
    }

    public static bool AppliesToTeam(Dictionary<string, string> item, int teamNum)
    {
        int itemTeam = ResolvePlayerskinTeamSlot(item);
        return itemTeam == AnyTeam || itemTeam == teamNum;
    }

    public static bool IsPlayerskinTeamConflict(Dictionary<string, string> existingItem, Dictionary<string, string> newItem)
    {
        int existingTeam = ResolvePlayerskinTeamSlot(existingItem);
        int newTeam = ResolvePlayerskinTeamSlot(newItem);

        // ALL skin overrides every existing playerskin
        if (newTeam == AnyTeam)
            return true;

        // Team-specific only conflicts with the same team slot
        return existingTeam == newTeam;
    }

    public static string GetTeamLabel(int team) => team switch
    {
        Terrorist => "T",
        CounterTerrorist => "CT",
        _ => "ALL"
    };

    public static bool TryParseTeamValue(string? rawValue, out int team)
    {
        team = 0;
        if (string.IsNullOrWhiteSpace(rawValue))
            return false;

        switch (rawValue.Trim().ToLowerInvariant())
        {
            case "1": case "all": case "any":
                team = AnyTeam;
                return true;
            case "2": case "t": case "terrorist": case "terrorists":
                team = Terrorist;
                return true;
            case "3": case "ct": case "counterterrorist": case "counterterrorists":
            case "counter-terrorist": case "counter-terrorists":
                team = CounterTerrorist;
                return true;
        }

        return false;
    }
}
