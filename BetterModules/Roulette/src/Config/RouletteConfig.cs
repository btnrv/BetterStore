using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Roulette.Config;

public class RouletteConfig
{
    [JsonPropertyName("WalletType")]
    public string WalletType { get; set; } = "credits";

    [JsonPropertyName("MinBet")]
    public int MinBet { get; set; } = 10;

    [JsonPropertyName("MaxBet")]
    public int MaxBet { get; set; } = 10000;

    [JsonPropertyName("RedMultiplier")]
    public double RedMultiplier { get; set; } = 2.0;

    [JsonPropertyName("BlackMultiplier")]
    public double BlackMultiplier { get; set; } = 2.0;

    [JsonPropertyName("GreenMultiplier")]
    public double GreenMultiplier { get; set; } = 14.0;

    [JsonPropertyName("RedCommands")]
    public List<string> RedCommands { get; set; } = new() { "red" };

    [JsonPropertyName("GreenCommands")]
    public List<string> GreenCommands { get; set; } = new() { "green" };

    [JsonPropertyName("BlackCommands")]
    public List<string> BlackCommands { get; set; } = new() { "black" };
}
