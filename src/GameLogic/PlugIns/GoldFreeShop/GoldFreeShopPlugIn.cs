using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
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

/// <summary>
/// Configuration for the Gold Free Shop plugin.
/// </summary>
public class GoldFreeShopConfiguration
{
    /// <summary>
    /// Gets or sets the price of Jewel of Bless in GOLD FREE coins (0 = disabled).
    /// </summary>
    [Display(Name = "Jewel of Bless - price (0 = desligada)")]
    public int BlessPrice { get; set; } = 3;

    /// <summary>
    /// Gets or sets the price of Jewel of Soul in GOLD FREE coins (0 = disabled).
    /// </summary>
    [Display(Name = "Jewel of Soul - price (0 = desligada)")]
    public int SoulPrice { get; set; } = 3;

    /// <summary>
    /// Gets or sets the price of Jewel of Chaos in GOLD FREE coins (0 = disabled).
    /// </summary>
    [Display(Name = "Jewel of Chaos - price (0 = desligada)")]
    public int ChaosPrice { get; set; } = 2;

    /// <summary>
    /// Gets or sets the price of Jewel of Life in GOLD FREE coins (0 = disabled).
    /// </summary>
    [Display(Name = "Jewel of Life - price (0 = desligada)")]
    public int LifePrice { get; set; } = 4;

    /// <summary>
    /// Gets or sets the exchange rate of GOLD FREE per WCoin (0 = exchange disabled).
    /// </summary>
    [Display(Name = "GOLD FREE per WCoin (0 = troca desligada)")]
    public int GoldFreePerWcoin { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether the shop is enabled.
    /// </summary>
    [Display(Name = "Shop enabled")]
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Arguments for the Gold Free Shop chat command.
/// </summary>
public class GoldFreeShopArguments : ArgumentsBase
{
    /// <summary>
    /// Gets or sets the item code to purchase.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Gets or sets the amount to purchase or exchange.
    /// </summary>
    /// <remarks>
    /// É string (e não int) de propósito: o parser posicional do OpenMU usa
    /// Convert.ChangeType para cada argumento e, se a conversão para int falhar
    /// (quantidade não numérica, espaço extra, etc.), ele emite a mensagem azul
    /// "invalid type ... Int32". Recebendo como string a conversão nunca falha e
    /// nós validamos manualmente em HandleWcoinAsync.
    /// </remarks>
    public string? Amount { get; set; }
}

/// <summary>
/// Plugin that implements the GOLD FREE shop, allowing players to buy items with GOLD FREE coins.
/// </summary>
[GuidAttribute("9A8B7C6D-5E4F-3A2B-1C0D-9E8F7A6B5C4D")]
[PlugIn]
[Display(Name = "GOLD FREE Shop", Description = "Loja oficial: compra itens com GOLD FREE")]
[ChatCommandHelp("/loja", typeof(GoldFreeShopArguments), CharacterStatus.Normal)]
public class GoldFreeShopPlugIn : ChatCommandPlugInBase<GoldFreeShopArguments>,
    ISupportCustomConfiguration<GoldFreeShopConfiguration>, ISupportDefaultCustomConfiguration
{
    private const byte CoinGroup = 14;
    private const short CoinNumber = 100;

    /// <summary>
    /// Gets or sets the configuration for this plugin.
    /// </summary>
    public GoldFreeShopConfiguration? Configuration { get; set; }

    /// <inheritdoc />
    public override string Key => "/loja";

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <summary>
    /// Creates the default configuration for this plugin.
    /// </summary>
    /// <returns>The default configuration.</returns>
    public object CreateDefaultConfig() => new GoldFreeShopConfiguration();

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, GoldFreeShopArguments arguments)
    {
        var config = this.Configuration ?? new GoldFreeShopConfiguration();
        if (!config.Enabled)
        {
            await ShowMessageAsync(player, "A loja esta fechada no momento.").ConfigureAwait(false);
            return;
        }

        var code = arguments.Code ?? string.Empty;
        if (code.StartsWith("wcoin", StringComparison.OrdinalIgnoreCase))
        {
            await this.HandleWcoinAsync(player, config, arguments).ConfigureAwait(false);
            return;
        }

        var entries = BuildCatalog(config);
        var balance = CountCoins(player);

        if (string.IsNullOrWhiteSpace(arguments.Code))
        {
            var wcoinBalance = await GetWCoinBalanceAsync(player).ConfigureAwait(false);
            await ShowMessageAsync(player, $"=== GOLD FREE SHOP === GOLD FREE: {balance} | WCoin: {wcoinBalance}").ConfigureAwait(false);
            foreach (var e in entries)
            {
                await ShowMessageAsync(player, $"{e.Code} = {e.Name} +{e.Level} | {e.Price} GOLD FREE").ConfigureAwait(false);
            }

            if (config.GoldFreePerWcoin > 0)
            {
                await ShowMessageAsync(player, $"wcoin = troca GOLD FREE por WCoin | 1 WCoin = {config.GoldFreePerWcoin} GOLD FREE").ConfigureAwait(false);
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

        await ConsumeCoinsAsync(player, entry.Price).ConfigureAwait(false);

        var item = player.PersistenceContext.CreateNew<Item>();
        item.Definition = definition;
        item.Level = entry.Level;
        item.Durability = definition.Durability;

        await inventory.AddItemAsync(item).ConfigureAwait(false);
        // Faz o item aparecer no inventario do cliente em tempo real (sem relogar).
        await player.InvokeViewPlugInAsync<IItemAppearPlugIn>(p => p.ItemAppearAsync(item)).ConfigureAwait(false);
        await ShowMessageAsync(player, $"Compra efetuada: {entry.Name} (-{entry.Price} GOLD FREE).").ConfigureAwait(false);
    }

    private async ValueTask HandleWcoinAsync(Player player, GoldFreeShopConfiguration config, GoldFreeShopArguments arguments)
    {
        if (config.GoldFreePerWcoin <= 0)
        {
            await ShowMessageAsync(player, "A troca GOLD FREE -> WCoin esta desligada.").ConfigureAwait(false);
            return;
        }

        if (player.Account is null)
        {
            await ShowMessageAsync(player, "Conta invalida.").ConfigureAwait(false);
            return;
        }

        // O parser posicional já separou "wcoin" (Code) da quantidade (Amount, string).
        // Validamos a conversão aqui para controlar a mensagem em vez de deixar o
        // parser genérico emitir "invalid type ... Int32".
        var physical = CountCoins(player);
        var rawAmount = arguments.Amount?.Trim();
        if (string.IsNullOrEmpty(rawAmount))
        {
            var wcoinBalance = await GetWCoinBalanceAsync(player).ConfigureAwait(false);
            await ShowMessageAsync(player, $"GOLD FREE no inventario: {physical} | WCoin na conta: {wcoinBalance}").ConfigureAwait(false);
            await ShowMessageAsync(player, $"1 WCoin = {config.GoldFreePerWcoin} GOLD FREE. Use /loja wcoin <quantidade>.").ConfigureAwait(false);
            return;
        }

        if (!int.TryParse(rawAmount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
        {
            await ShowMessageAsync(player, "Quantidade invalida. Use /loja wcoin <numero>. Ex.: /loja wcoin 5").ConfigureAwait(false);
            return;
        }

        var cost = quantity * config.GoldFreePerWcoin;
        if (physical < cost)
        {
            await ShowMessageAsync(player, $"Voce precisa de {cost} GOLD FREE (voce tem {physical}).").ConfigureAwait(false);
            return;
        }

        await ConsumeCoinsAsync(player, cost).ConfigureAwait(false);

        long newBalance;
        try
        {
            newBalance = await WCoinService.CreditAsync(
                player.PersistenceContext,
                player.Account.Id,
                quantity,
                WCoinTransactionType.Exchange,
                $"Troca de {cost} GOLD FREE por WCoin").ConfigureAwait(false);
        }
        catch (Exception)
        {
            await ShowMessageAsync(player, "Erro ao creditar WCoin. Fale com um GM.").ConfigureAwait(false);
            return;
        }

        await ShowMessageAsync(player, $"+{quantity} WCoin (-{cost} GOLD FREE). Saldo WCoin: {newBalance}").ConfigureAwait(false);
    }

    private static async ValueTask<long> GetWCoinBalanceAsync(Player player)
    {
        if (player.Account is null)
        {
            return 0;
        }

        return await WCoinService.GetBalanceAsync(player.PersistenceContext, player.Account.Id).ConfigureAwait(false);
    }

    private static async ValueTask ConsumeCoinsAsync(Player player, int amount)
    {
        var stacks = player.Inventory!.Items
            .Where(i => i.Definition?.Group == CoinGroup && i.Definition?.Number == CoinNumber)
            .ToList();

        var remaining = amount;
        foreach (var stack in stacks)
        {
            if (remaining <= 0)
            {
                break;
            }

            var qty = StackQuantity(stack);
            if (qty <= remaining)
            {
                remaining -= qty;
                await player.DestroyInventoryItemAsync(stack).ConfigureAwait(false);
            }
            else
            {
                stack.Durability = (byte)(qty - remaining);
                remaining = 0;
                // Atualiza a quantidade exibida da pilha no cliente (consumo parcial).
                await player.InvokeViewPlugInAsync<IItemDurabilityChangedPlugIn>(p => p.ItemDurabilityChangedAsync(stack, true)).ConfigureAwait(false);
            }
        }
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
            .Where(i => i.Definition?.Group == CoinGroup && i.Definition?.Number == CoinNumber)
            .Sum(StackQuantity) ?? 0;
    }

    private static int StackQuantity(Item item) => item.Durability > 0 ? (int)item.Durability : 1;

    private sealed record CatalogEntry(string Code, string Name, byte Group, short Number, byte Level, int Price);
}