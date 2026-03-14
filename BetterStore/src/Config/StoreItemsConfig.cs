using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BetterStore.Config;

public class StoreNode
{
    public string DisplayName { get; set; } = "";
    public string ItemId { get; set; } = "";
    public bool IsItem { get; set; }
    public Dictionary<string, string> ItemData { get; set; } = new();
    public List<StoreNode> Children { get; set; } = new();
}

public static class StoreItemsConfig
{
    public static StoreNode LoadFromFile(string filePath)
    {
        string json = File.ReadAllText(filePath);

        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        StoreNode root = new() { DisplayName = "Store" };
        ParseChildren(doc.RootElement, root);
        return root;
    }

    private static void ParseChildren(JsonElement element, StoreNode parent)
    {
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
                continue;

            string displayName = prop.Name;
            StoreNode node = new() { DisplayName = displayName };

            if (prop.Value.TryGetProperty("type", out _))
            {
                node.IsItem = true;
                foreach (JsonProperty itemProp in prop.Value.EnumerateObject())
                {
                    node.ItemData[itemProp.Name] = itemProp.Value.ToString();
                }

                string itemId = node.ItemData.GetValueOrDefault("item_id", SanitizeId(displayName));
                node.ItemId = itemId;
                node.ItemData["item_id"] = itemId;
                node.ItemData["name"] = displayName;
            }
            else
            {
                node.IsItem = false;
                ParseChildren(prop.Value, node);
            }

            parent.Children.Add(node);
        }
    }

    public static Dictionary<string, Dictionary<string, string>> FlattenItems(StoreNode root)
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        CollectItems(root, result);
        return result;
    }

    private static void CollectItems(StoreNode node, Dictionary<string, Dictionary<string, string>> result)
    {
        if (node.IsItem && !string.IsNullOrEmpty(node.ItemId))
        {
            if (node.ItemData.TryGetValue("disabled", out var disabledVal))
            {
                if (bool.TryParse(disabledVal, out var isDisabled) && isDisabled)
                    return;

                if (string.Equals(disabledVal, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(disabledVal, "true", StringComparison.OrdinalIgnoreCase))
                    return;
            }

            result[node.ItemId] = node.ItemData;
        }

        foreach (StoreNode child in node.Children)
        {
            CollectItems(child, result);
        }
    }

    public static long ParseDurationToSeconds(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration) || duration == "0" || duration.Equals("permanent", StringComparison.OrdinalIgnoreCase))
            return 0;

        long totalSeconds = 0;
        var matches = Regex.Matches(duration, @"(\d+)\s*(y|d|h|m|s)", RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            long value = long.Parse(match.Groups[1].Value);
            string unit = match.Groups[2].Value.ToLowerInvariant();

            totalSeconds += unit switch
            {
                "y" => Math.Min(value, 1) * 365 * 24 * 3600,
                "d" => value * 24 * 3600,
                "h" => value * 3600,
                "m" => value * 60,
                "s" => value,
                _ => 0
            };
        }

        if (totalSeconds <= 0 && long.TryParse(duration, out long rawSeconds))
        {
            totalSeconds = rawSeconds;
        }

        long maxSeconds = 365 * 24 * 3600;
        return totalSeconds > maxSeconds ? maxSeconds : totalSeconds;
    }

    private static string SanitizeId(string name)
    {
        return name.ToLowerInvariant()
            .Replace(' ', '_')
            .Replace('-', '_');
    }
}
