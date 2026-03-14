<div align="center">
  <img src="https://pan.samyyc.dev/s/VYmMXE" />
  <h2><strong>BetterStore</strong></h2>
  <h3>A fully-featured, modular in-game store plugin for Counter-Strike 2 servers running SwiftlyS2.</h3>
</div>

<p align="center">
  <img src="https://img.shields.io/badge/build-passing-brightgreen" alt="Build Status">
  <img src="https://img.shields.io/github/downloads/btnrv/BetterStore/total" alt="Downloads">
  <img src="https://img.shields.io/github/stars/btnrv/BetterStore?style=flat&logo=github" alt="Stars">
  <img src="https://img.shields.io/github/license/btnrv/BetterStore" alt="License">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4" alt=".NET 10">
</p>

> [!WARNING]
> **This plugin contains AI-assisted generated code.** Portions of the codebase were written with the help of AI code generation tools. While the code has been reviewed and tested, please test some of the features carefully before deploying in your server if you want to make sure.

---

## Table of Contents

- [Features](#features)
- [Commands](#commands)
- [Configuration](#configuration)
- [Available Modules](#available-modules)
- [Available BetterModules](#available-bettermodules)
- [API](#api)
- [Building from Source](#building-from-source)
- [Installation](#installation)

---

## Features

- **Multi-currency support** — Define any number of named currencies (e.g. credits, tokens, gems), each with its own icon, balance commands, and gifting permissions. All currencies integrate with the Economy API.
- **Hierarchical store menu** — Organize items into nested categories and subcategories via a simple JSONC config file. No code changes required to add new items.
- **Inventory system** — Players can view all owned items, see remaining time for timed items, equip/unequip, inspect, and sell items from a single menu.
- **Timed & permanent items** — Items can be permanent or expire after a configurable duration (`7d`, `1h30m`, `permanent`, etc.). Expired items are automatically purged every 60 seconds.
- **Item selling** — Players can sell items back for a configurable fraction of the purchase price (default 50%).
- **Purchase confirmation** — Optional confirm dialog before finalizing a purchase.
- **Inspect / preview** — Player skin and weapon skin items can be previewed before purchasing.
- **Free-use permission** — Players with `betterstore.freeuse` can equip any item without purchasing it.
- **Top-10 leaderboard** — `!rich` command shows the top 10 wealthiest players per currency.
- **Currency gifting** — Players can gift currency to each other (per-currency opt-in).
- **Async MySQL persistence** — All database writes are queued and flushed in batches, either on a timer, on round end, or on map change — minimising performance impact.
- **Orphan cleanup** — On startup, items referencing item IDs that no longer exist in the config are automatically removed from the database.
- **Modular architecture** — New item types can be added as external plugins implementing `IItemModule` without modifying BetterStore itself.
- **Fully translatable** — All player-facing strings are in JSONC translation files (`en.jsonc`, `tr.jsonc`, etc.).
- **Default model management** — Configure default T-side and CT-side models; admin commands (`!model0` / `!model1`) can toggle player models server-wide.
- **Gradient menu titles** — Configurable hex color gradient for the store menu title.

---

## Commands

All command names are fully configurable in `BetterStore.jsonc`.

### Player Commands

| Command (default) | Description |
|---|---|
| `!store` / `!shop` | Open the main store menu |
| `!inventory` / `!inv` | Open your inventory |
| `!gift <player> <amount> [currency]` | Gift currency to another player |
| `!rich` | View the top-10 wealthiest players leaderboard |
| `!<currency>` | Check your balance for that currency (one command per configured currency) |

### Admin Commands

| Command | Permission | Description |
|---|---|---|
| `givecurrency <player> <amount> <currency>` | `betterstore.currency` | Give currency to a player |
| `removecurrency <player> <amount> <currency>` | `betterstore.currency` | Remove currency from a player |
| `resetplayer <player>` | `betterstore.reset` | Wipe all store data and currency for a player |
| `resetdatabase` | `betterstore.reset` | Wipe the entire store database |
| `model0` | `betterstore.admin` | Force all players back to their default model |
| `model1` | `betterstore.admin` | Reapply all player custom models |

---

## Configuration

The main config file is `cfg/betterstore/BetterStore.jsonc`. Key options:

| Option | Default | Description |
|---|---|---|
| `PluginPrefix` | `[BetterStore]` | Chat message prefix |
| `DefaultCurrency` | `"credits"` | Fallback currency when none is specified |
| `Currencies` | `{}` | Dictionary of named currencies (see below) |
| `DatabaseConnection` | `"betterstore"` | Database connection name |
| `SaveQueueIntervalSeconds` | `0` | DB flush interval in seconds (`0` = map change only) |
| `SaveOnRoundEnd` | `true` | Also flush the DB queue on round end |
| `SellRatio` | `0.5` | Sell-back price as a fraction of purchase price |
| `EnableSelling` | `true` | Allow players to sell items |
| `EnableConfirmMenu` | `true` | Show a confirmation dialog before purchases |
| `InspectCooldownSeconds` | `10.0` | Seconds between inspect/preview uses |
| `ModelApplyCooldownSeconds` | `0.5` | Minimum seconds between model reapplications |
| `MenuTitle` | `"BetterStore"` | Title shown at the top of menus |
| `MenuTitleGradient` | `["#5EB8FF","#A8D8FF"]` | Gradient and accent colors for the menu |
| `DefaultModels.Terrorist` | `[]` | Default model paths for T-side |
| `DefaultModels.CounterTerrorist` | `[]` | Default model paths for CT-side |
| `DefaultModels.DisableLeg` | `false` | Disable the leg model |
| `MaxHealth` | `200` | Maximum health cap |
| `MaxArmor` | `200` | Maximum armor cap |
| `MenuCommands` | `["store","shop"]` | Commands that open the store |
| `InventoryCommands` | `["inventory","inv"]` | Commands that open the inventory |
| `GiftCommands` | `["gift"]` | Commands for gifting currency |
| `RichCommands` | `["rich"]` | Commands for the leaderboard |

### Currency Configuration

Each entry under `Currencies` supports:

| Field | Default | Description |
|---|---|---|
| `HTMLIcon` | `"$"` | Icon shown in HTML menus |
| `ChatIcon` | `"$"` | Icon shown in chat messages |
| `BalanceCommands` | `[]` | Commands players type to check balance |
| `Giftable` | `true` | Whether this currency can be gifted with `!gift` |

### Permissions

| Permission Flag | Controls |
|---|---|
| `betterstore.freeuse` | Equip any item without purchasing |
| `betterstore.currency` | Use `givecurrency` / `removecurrency` |
| `betterstore.reset` | Use `resetplayer` / `resetdatabase` |
| `betterstore.admin` | Use `model0` / `model1` |

### Store Items Config

Items and categories are defined in `cfg/betterstore/store_items.jsonc`. Each item node supports:

| Field | Description |
|---|---|
| `type` | Module type string (required, e.g. `playerskin`, `customweapon`) |
| `item_id` | Unique identifier (auto-derived from display name if omitted) |
| `name` | Display name shown in menus |
| `price` | Integer purchase price |
| `currency` | Which currency to charge (defaults to `DefaultCurrency`) |
| `duration` | Time string (`"7d"`, `"1h30m"`, `"permanent"`) |
| `disabled` | `"true"` — removes item from the store entirely |
| `hide` | `"true"` — hides from menu but item type still loads |

Additional fields are module-specific (see [Available Modules](#available-modules)).

---

## Available Modules

Modules are item types built into BetterStore. Each is registered automatically from the assembly.

### `playerskin` — Player Model
Replaces the player's character model.

| Item Field | Description |
|---|---|
| `model` | *(required)* Path to the model file |
| `skin` | Submodel skin variant (optional) |
| `team` | Team restriction: `t`, `ct`, or absent for both |

Multiple player skins can coexist if they target different teams. Supports in-menu **Inspect** preview.

---

### `customweapon` — Weapon Skin
Applies a weapon subclass (skin variant) to a specific weapon.

| Item Field | Description |
|---|---|
| `weapon` | `"weapon_ak47:SubclassId"` — weapon name and subclass ID separated by `:` |

Supports in-menu **Inspect** preview (player must hold the weapon).

---

### `coloredskin` — Colored Player Tint
Applies a per-tick color tint to the player pawn.

| Item Field | Description |
|---|---|
| `color` | `"R G B"` string (0–255 each). Omit for a random color each tick |

---

### `trail` — Player Trail
Leaves a beam or particle trail behind the player as they move.

| Item Field | Description |
|---|---|
| `color` | `"R G B"` string — creates a beam trail |
| `model` | Particle effect name — creates a particle trail (used when `color` is absent) |
| `lifetime` | Trail segment lifetime in seconds (default `1.3`) |

---

### `grenadetrail` — Grenade Trail
Attaches a particle trail to grenades thrown by the player.

| Item Field | Description |
|---|---|
| `model` | Particle effect name |

---

### `tracer` — Bullet Tracer
Renders a beam from the player's eye to each bullet impact point.

| Item Field | Description |
|---|---|
| `model` | Beam model path |
| `lifetime` | Beam lifetime in seconds (default `0.3`) |

---

### `smoke` — Custom Smoke Color
Changes the color of smoke grenades thrown by the player.

| Item Field | Description |
|---|---|
| `color` | `"R G B"` float string. Omit (or use item_id `colorsmoke`) for a random color |

---

### `equipment` — Wearable Prop
Spawns a `prop_dynamic_override` entity that follows the player.

| Item Field | Description |
|---|---|
| `model` | Model path |
| `slot` | Slot number for conflict resolution (same slot = only one equipped) |

---

### `wings` — Wings / Back Decoration
Spawns a `prop_dynamic_override` entity on the player's back with optional animation.

| Item Field | Description |
|---|---|
| `model` | Model path |
| `slot` | Slot number for conflict resolution |
| `animation` | Animation name to play (optional) |

---

### `weapon` — Give Weapon *(one-time use)*
Gives the player a weapon immediately. Consumed on use; not stored in inventory.

| Item Field | Description |
|---|---|
| `weapon` | Weapon designer name (e.g. `weapon_ak47`) |

Requires the player to be alive.

---

### `respawn` — Instant Respawn *(one-time use)*
Respawns a dead player immediately. Consumed on use; not stored in inventory.

No additional item fields required.

---

## Available BetterModules

BetterModules are standalone plugins that integrate with the Economy API to provide additional gameplay content. They live in the `BetterModules/` directory.

### Blackjack

A classic Blackjack card game played via chat commands.

| Command | Description |
|---|---|
| `!blackjack <amount>` | Start a game and place your bet |
| `!hit` | Draw another card |
| `!stand` | End your turn — dealer then plays out |

- Standard Blackjack rules: beat the dealer without exceeding 21.
- Real-time HUD shows current hand values.
- Configurable min/max bet, inactivity timeout, and currency (`WalletType`).
- Players are refunded automatically on disconnect.

---

### Roulette

A round-based roulette game where players bet before each round starts.

| Command | Description |
|---|---|
| `!red <amount>` | Bet on Red |
| `!black <amount>` | Bet on Black |
| `!green <amount>` | Bet on Green |

- Bets open at round start and close at MVP announcement.
- Win chances: Green ≈ 1/15, Red ≈ 7/15, Black ≈ 7/15 (configurable multipliers).
- Configurable min/max bet and currency (`WalletType`).
- All pending bets are refunded on plugin unload.

---

### Wage

A cooldown-based wage system — players claim currency once per configured interval.

| Command | Description |
|---|---|
| `!wage` | Claim your wage |

- Tiered rewards: assign different amounts per permission group.
- Cooldown tracked in MySQL.
- Configurable cool-down in hours, currency (`WalletType`), and permission tiers.

---

## API

BetterStore exposes a public API via the `BetterStore.Contract` assembly (`IBetterStoreAPIv1`). Other plugins can depend on it to interact with the store without referencing BetterStore directly.

### Events

| Event | Signature | Description |
|---|---|---|
| `OnPlayerPurchaseItem` | `(IPlayer, string currency, int price, string itemId)` | Fired after a successful purchase |
| `OnPlayerEquipItem` | `(IPlayer, string itemId)` | Fired when a player equips an item |
| `OnPlayerUnequipItem` | `(IPlayer, string itemId)` | Fired when a player unequips an item |
| `OnPlayerSellItem` | `(IPlayer, string itemId, int sellPrice)` | Fired after a player sells an item |

### Methods

| Method | Description |
|---|---|
| `GiveItem(player, itemId)` | Give an item to a player without charging currency |
| `PurchaseItem(player, itemId)` | Purchase an item for a player (charges currency) |
| `EquipItem(player, itemId)` | Equip an owned item |
| `UnequipItem(player, itemId)` | Unequip an item |
| `SellItem(player, itemId)` | Sell an item and refund currency |
| `PlayerHasItem(player, type, itemId)` | Check if a player owns an item |
| `PlayerUsingItem(player, type, itemId)` | Check if a player has an item equipped |
| `GetItem(itemId)` | Get item config data by ID |
| `GetItemsByType(type)` | Get all items of a given module type |
| `RegisterModule(module)` | Register a custom `IItemModule` at runtime |
| `GetPlayerBalance(player, currency?)` | Get a player's balance (defaults to `DefaultCurrency`) |
| `AddPlayerBalance(player, amount, currency?)` | Add to a player's balance |
| `SubtractPlayerBalance(player, amount, currency?)` | Subtract from a player's balance |
| `SetPlayerBalance(player, amount, currency?)` | Set a player's balance |
| `HasSufficientFunds(player, amount, currency?)` | Check if a player can afford an amount |

### Registering a Custom Module

Implement `IItemModule` from `BetterStore.Contract`, decorate the class with `[StoreItemType("mytype")]`, and register via `IBetterStoreAPIv1.RegisterModule(...)` or let BetterStore auto-discover it from your assembly.

```csharp
[StoreItemType("mytype")]
public class MyModule : IItemModule
{
    public bool Equipable => true;
    public bool? RequiresAlive => null;

    public void OnPluginStart() { }
    public void OnMapStart() { }

    public bool OnEquip(IPlayer player, Dictionary<string, string> item)
    {
        // apply effect...
        return true;
    }

    public bool OnUnequip(IPlayer player, Dictionary<string, string> item, bool update)
    {
        // remove effect...
        return true;
    }
}
```

---

## Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A MySQL-compatible database
- SwiftlyS2 server runtime

### 1. Clone the repository with submodules

This project depends on the **Economy** plugin as a git submodule located at `BetterStore/deps/Economy`. You must initialize submodules before building:

```bash
git clone --recurse-submodules https://github.com/btnrv/BetterStore.git
```

If you have already cloned the repository without submodules, run:

```bash
git submodule update --init --recursive
```

### 2. Publish

```bash
dotnet publish -c Release
```

This produces `BetterStore.zip` in the publish directory, ready for server deployment.

---

## Installation

1. Extract `BetterStore.zip` into your SwiftlyS2 `plugins/` directory.
2. Configure `cfg/betterstore/BetterStore.jsonc` — set up your currencies, database connection, and command names.
3. Configure `cfg/betterstore/store_items.jsonc` — define your item categories and items.
4. Ensure the Economy plugin is loaded (BetterStore registers wallet kinds against it on startup).
5. Restart your server or reload SwiftlyS2.

---

## Acknowledgements

This project is inspired by [cs2-store](https://github.com/schwarper/cs2-store) by schwarper.

## License

See [LICENSE](LICENSE) for details.