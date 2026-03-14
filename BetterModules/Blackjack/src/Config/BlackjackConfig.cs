using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Blackjack.Config;

public class BlackjackConfig
{
    [JsonPropertyName("WalletType")]
    public string WalletType { get; set; } = "credits";

    [JsonPropertyName("MinBet")]
    public int MinBet { get; set; } = 100;

    [JsonPropertyName("MaxBet")]
    public int MaxBet { get; set; } = 999999;

    [JsonPropertyName("InactivityTimeoutSeconds")]
    public int InactivityTimeoutSeconds { get; set; } = 60;

    [JsonPropertyName("Commands")]
    public List<string> Commands { get; set; } = new() { "bj", "blackjack" };

    [JsonPropertyName("HitCommand")]
    public string HitCommand { get; set; } = "hit";

    [JsonPropertyName("StandCommand")]
    public string StandCommand { get; set; } = "stand";

    [JsonPropertyName("AccentColor")]
    public string AccentColor { get; set; } = "#5599FF";

    [JsonPropertyName("DealerColor")]
    public string DealerColor { get; set; } = "#dc2828";

    [JsonPropertyName("PlayerColor")]
    public string PlayerColor { get; set; } = "#55aadc";

    [JsonPropertyName("ValueColor")]
    public string ValueColor { get; set; } = "#00FFFF";

    [JsonPropertyName("WinColor")]
    public string WinColor { get; set; } = "#00FF00";

    [JsonPropertyName("LoseColor")]
    public string LoseColor { get; set; } = "#FF0000";

    [JsonPropertyName("DrawColor")]
    public string DrawColor { get; set; } = "#FFA500";
}
