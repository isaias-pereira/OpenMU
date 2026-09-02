using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.Inventory;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.PlugIns;

namespace MUnique.OpenMU.GoldFreeShop;

public class GoldFreeShopConfiguration
{
    [Display(Name = "Jewel of Bless - price (0 = desligada)")]
    public int BlessPrice { get; set; } = 3;

    [Display(Name = "Jewel of Soul - price (0 = desligada)")]
    public int SoulPrice { get; set; } = 3;

    [Display(Name = "Jewel of Chaos - price (0 = desligada)")]
    public int ChaosPrice { get; set; } = 2;

    [Display(Name = "Jewel of Life - price (0 = desligada)")]
    public int LifePrice { get; set; } = 4;

    [Display(Name = "Shop enabled")]
    public bool Enabled { get; set; } = true;
}

public class GoldFreeShopArguments : ArgumentsBase
{
    public string? Code { get; set; }
}

[Guid("9A8B7C6D-5E4F-3A2B-1C0D-9E8F7A6B5C4D")]
[PlugIn]
[Display(Name = "GOLD FREE Shop", Description = "Loja oficial: compra itens com GOLD FREE")]
[ChatCommandHelp("/loja", typeof(GoldFreeShopArguments), CharacterStatus.Normal)]
public class GoldFreeShopPlugIn : ChatCommandPlugInBase<GoldFreeShopArguments>,
    ISupportCustomConfiguration<GoldFreeShopConfiguration>, ISupportDefaultCustomConfiguration
{
    private const byte CoinGroup = 14;
    private const short CoinNumber = 100;

    public GoldFreeShopConfiguration? Configuration { get; set; }
    public override string Key => "/loja";
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;
    public object CreateDefaultConfig() => new GoldFreeShopConfiguration();

    protected override async ValueTask DoHandleCommandAsync(Player player, GoldFreeShopArguments arguments)
    {
        var config = this.Configuration ?? new GoldFreeShopConfiguration();
        if (!config.Enabled)
        {
            await ShowMessageAsync(player, "A loja esta fechada no momento.").ConfigureAwait(false);
            return;
        }

        var entries = BuildCatalog(config);
        var balance = CountCoins(player);

        if (string.IsNullOrWhiteSpace(arguments.Code))
        {
            await ShowMessageAsync(player, $"=== GOLD FREE SHOP === Saldo: {balance} moeda(s)").ConfigureAwait(false);
            foreach (var e in entries)
            {
                await ShowMessageAsync(player, $"{e.Code} = {e.Name} +{e.Level} | {e.Price} GOLD FREE").ConfigureAwait(false);
            }

            await ShowMessageAsync(player, "Use /loja <code> para comprar").ConfigureAwait(false);
            return;
        }

        var entry = entries.FirstOrDefault(e => e.Code.Equals(arguments.Code, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            await ShowMessageAsync(player, "Codigo invalido. Use /loja para ver o catalogo.").ConfigureAwait(false);
            return;
        }

        var inventory = player.Inventory;
        if (inventory is null || player.SelectedCharacter?.Inventory is null)
        {
            return;
        }

        if (balance < entry.Price)
        {
            await ShowMessageAsync(player, $"Voce precisa de {entry.Price} GOLD FREE (voce tem {balance}).").ConfigureAwait(false);
            return;
        }

        if (player.InventorySize - inventory.Items.Count() < 1)
        {
            await ShowMessageAsync(player, "Inventario cheio.").ConfigureAwait(false);
            return;
        }

        var definition = player.GameContext?.Configuration?.Items
            .FirstOrDefault(d => d.Group == entry.Group && d.Number == entry.Number);
        if (definition is null)
        {
            await ShowMessageAsync(player, "Item do catalogo nao existe no servidor.").ConfigureAwait(false);
            return;
        }

        var coins = inventory.Items
            .Where(i => i.Definition?.Group == CoinGroup && i.Definition?.Number == CoinNumber)
            .Take(entry.Price)
            .ToList();
        foreach (var coin in coins)
        {
            await player.DestroyInventoryItemAsync(coin).ConfigureAwait(false);
        }

        var item = player.PersistenceContext.CreateNew<Item>();
        item.Definition = definition;
        item.Level = entry.Level;
        item.Durability = definition.Durability;

        await inventory.AddItemAsync(item).ConfigureAwait(false);
        await player.InvokeViewPlugInAsync<IUpdateInventoryListPlugIn>(p => p.UpdateInventoryListAsync()).ConfigureAwait(false);
        await ShowMessageAsync(player, $"Compra efetuada: {entry.Name} (-{entry.Price} GOLD FREE). Saldo: {balance - entry.Price}").ConfigureAwait(false);
    }

    private static List<CatalogEntry> BuildCatalog(GoldFreeShopConfiguration c)
    {
        var list = new List<CatalogEntry>();
        void Add(string code, string name, byte group, short number, int price)
        {
            if (price > 0)
            {
                list.Add(new CatalogEntry(code, name, group, number, 0, price));
            }
        }

        Add("bless", "Jewel of Bless", 14, 13, c.BlessPrice);
        Add("soul", "Jewel of Soul", 14, 14, c.SoulPrice);
        Add("chaos", "Jewel of Chaos", 12, 15, c.ChaosPrice);
        Add("life", "Jewel of Life", 14, 16, c.LifePrice);
        return list;
    }

    private static async ValueTask ShowMessageAsync(Player player, string message)
    {
        await player.InvokeViewPlugInAsync<IShowMessagePlugIn>(
            p => p.ShowMessageAsync(message, MessageType.GoldenCenter)).ConfigureAwait(false);
    }

    private static int CountCoins(Player player)
    {
        return player.Inventory?.Items
            .Count(i => i.Definition?.Group == CoinGroup && i.Definition?.Number == CoinNumber) ?? 0;
    }

    private sealed record CatalogEntry(string Code, string Name, byte Group, short Number, byte Level, int Price);
}