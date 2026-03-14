using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace BetterStore.Config;

public class CurrencyConfig
{
    [JsonPropertyName("HTMLIcon")]
    public string HTMLIcon { get; set; } = "$";

    [JsonPropertyName("ChatIcon")]
    public string ChatIcon { get; set; } = "$";

    [JsonPropertyName("BalanceCommands")]
    public List<string> BalanceCommands { get; set; } = new();

    [JsonPropertyName("Giftable")]
    public bool Giftable { get; set; } = true;
}

public class BetterStoreConfig
{
    [JsonPropertyName("PluginPrefix")]
    public string PluginPrefix { get; set; } = "[green][BetterStore] [default]";

    [JsonPropertyName("Currencies")]
    public Dictionary<string, CurrencyConfig> Currencies { get; set; } = new();

    [JsonPropertyName("DefaultCurrency")]
    public string DefaultCurrency { get; set; } = "credits";

    [JsonIgnore]
    public List<string> CurrencyNames => Currencies.Keys.ToList();

    public string GetCurrencyIcon(string currency) =>
        Currencies.TryGetValue(currency, out var cfg) ? cfg.HTMLIcon : "";

    public string GetCurrencyChatIcon(string currency) =>
        Currencies.TryGetValue(currency, out var cfg) ? cfg.ChatIcon : "";


    [JsonPropertyName("DatabaseConnection")]
    public string DatabaseConnection { get; set; } = "betterstore";

    [JsonPropertyName("SaveQueueIntervalSeconds")]
    public int SaveQueueIntervalSeconds { get; set; } = 0;

    [JsonPropertyName("SaveOnRoundEnd")]
    public bool SaveOnRoundEnd { get; set; } = true;
    
    [JsonPropertyName("MenuCommands")]
    public List<string> MenuCommands { get; set; } = new() { "store", "shop" };

    [JsonPropertyName("InventoryCommands")]
    public List<string> InventoryCommands { get; set; } = new() { "inventory", "inv" };

    [JsonPropertyName("GiftCommands")]
    public List<string> GiftCommands { get; set; } = new() { "gift" };

    [JsonPropertyName("RichCommands")]
    public List<string> RichCommands { get; set; } = new() { "rich" };

    [JsonPropertyName("SellRatio")]
    public float SellRatio { get; set; } = 0.5f;

    [JsonPropertyName("EnableSelling")]
    public bool EnableSelling { get; set; } = true;

    [JsonPropertyName("EnableLogging")]
    public bool EnableLogging { get; set; } = true;

    [JsonPropertyName("EnableConfirmMenu")]
    public bool EnableConfirmMenu { get; set; } = true;

    [JsonPropertyName("InspectCooldownSeconds")]
    public float InspectCooldownSeconds { get; set; } = 10.0f;

    [JsonPropertyName("ModelApplyCooldownSeconds")]
    public float ModelApplyCooldownSeconds { get; set; } = 0.5f;

    [JsonPropertyName("MenuTitle")]
    public string MenuTitle { get; set; } = "BetterStore";

    [JsonPropertyName("MenuTitleGradient")]
    public List<string> MenuTitleGradient { get; set; } = new() { "#5EB8FF", "#A8D8FF" };

    [JsonPropertyName("DefaultModels")]
    public DefaultModelsConfig DefaultModels { get; set; } = new();

    [JsonPropertyName("AdminCommands")]
    public AdminCommandsConfig AdminCommands { get; set; } = new();

    [JsonPropertyName("Permissions")]
    public PermissionsConfig Permissions { get; set; } = new();

    [JsonPropertyName("MaxHealth")]
    public int MaxHealth { get; set; } = 200;

    [JsonPropertyName("MaxArmor")]
    public int MaxArmor { get; set; } = 200;
}

public class DefaultModelsConfig
{
    [JsonPropertyName("Terrorist")]
    public List<string> Terrorist { get; set; } = new();

    [JsonPropertyName("CounterTerrorist")]
    public List<string> CounterTerrorist { get; set; } = new();

    [JsonPropertyName("DisableLeg")]
    public bool DisableLeg { get; set; } = false;
}

public class AdminCommandsConfig
{
    [JsonPropertyName("GiveCurrency")]
    public List<string> GiveCurrency { get; set; } = new() { "givecurrency" };

    [JsonPropertyName("RemoveCurrency")]
    public List<string> RemoveCurrency { get; set; } = new() { "removecurrency" };

    [JsonPropertyName("ResetPlayer")]
    public List<string> ResetPlayer { get; set; } = new() { "resetplayer" };

    [JsonPropertyName("ResetDatabase")]
    public List<string> ResetDatabase { get; set; } = new() { "resetdatabase" };

    [JsonPropertyName("Model0")]
    public List<string> Model0 { get; set; } = new() { "model0" };

    [JsonPropertyName("Model1")]
    public List<string> Model1 { get; set; } = new() { "model1" };
}

public class PermissionsConfig
{
    [JsonPropertyName("FreeUse")]
    public string FreeUse { get; set; } = "betterstore.freeuse";

    [JsonPropertyName("AdminCommands")]
    public AdminCommandPermissionsConfig AdminCommands { get; set; } = new();
}

public class AdminCommandPermissionsConfig
{
    [JsonPropertyName("GiveCurrency")]
    public string GiveCurrency { get; set; } = "betterstore.currency";

    [JsonPropertyName("RemoveCurrency")]
    public string RemoveCurrency { get; set; } = "betterstore.currency";

    [JsonPropertyName("ResetPlayer")]
    public string ResetPlayer { get; set; } = "betterstore.reset";

    [JsonPropertyName("ResetDatabase")]
    public string ResetDatabase { get; set; } = "betterstore.reset";

    [JsonPropertyName("Model0")]
    public string Model0 { get; set; } = "betterstore.admin";

    [JsonPropertyName("Model1")]
    public string Model1 { get; set; } = "betterstore.admin";
}
