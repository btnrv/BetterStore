using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Wage.Config;

public class WageTierConfig
{
    [JsonPropertyName("Permission")]
    public string Permission { get; set; } = "";

    [JsonPropertyName("Amount")]
    public int Amount { get; set; } = 100;
}

public class WageConfig
{
    [JsonPropertyName("WalletType")]
    public string WalletType { get; set; } = "credits";

    [JsonPropertyName("CooldownHours")]
    public int CooldownHours { get; set; } = 24;

    [JsonPropertyName("DatabaseConnection")]
    public string DatabaseConnection { get; set; } = "default";

    [JsonPropertyName("Commands")]
    public List<string> Commands { get; set; } = new() { "wage" };

    [JsonPropertyName("Tiers")]
    public List<WageTierConfig> Tiers { get; set; } = new()
    {
        new WageTierConfig { Permission = "vip.daily", Amount = 500 },
        new WageTierConfig { Permission = "admin.wage", Amount = 100 }
    };
}
