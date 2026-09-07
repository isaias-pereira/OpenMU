// <copyright file="ZenToGoldFreeExchangePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.ComponentModel.DataAnnotations;
using MUnique.OpenMU.GameLogic.Views.Inventory;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Configuration for Zen to GOLD FREE exchange rate.
/// </summary>
public class ZenToGoldFreeExchangeConfiguration
{
    /// <summary>
    /// Amount of Zen required for 1 GOLD FREE.
    /// Default: 1,000,000,000 (1 billion zen = 1 GOLD FREE)
    /// </summary>
    [Display(Name = "Zen per GOLD FREE", Description = "Amount of Zen required to exchange for 1 GOLD FREE")]
    public long ZenPerGoldFree { get; set; } = 1_000_000_000L;

    /// <summary>
    /// Maximum GOLD FREE that can be exchanged in a single command.
    /// </summary>
    [Display(Name = "Max GOLD FREE per exchange", Description = "Maximum number of GOLD FREE items that can be exchanged in one command")]
    public int MaxGoldFreePerExchange { get; set; } = 100;
}

/// <summary>
/// Arguments for the /trocar command.
/// </summary>
public class ZenToGoldFreeExchangeArguments : ArgumentsBase
{
    /// <summary>
    /// Number of GOLD FREE items to exchange for.
    /// </summary>
    public int Quantidade { get; set; }
}

/// <summary>
/// Chat command plugin to exchange Zen for GOLD FREE currency.
/// Usage: /trocar &lt;quantidade&gt; (e.g., /trocar 5 = 5 GOLD FREE)
/// </summary>
[Guid("3F8A2C91-7B4E-4D6A-9C1E-5A2B8D4F6E10")]
[PlugIn]
[Display(Name = "Zen to GOLD FREE Exchange", Description = "Allows players to exchange Zen for GOLD FREE currency")]
[ChatCommandHelp("/trocar", typeof(ZenToGoldFreeExchangeArguments), CharacterStatus.Normal)]
public class ZenToGoldFreeExchangePlugIn : ChatCommandPlugInBase<ZenToGoldFreeExchangeArguments>,
    ISupportCustomConfiguration<ZenToGoldFreeExchangeConfiguration>, ISupportDefaultCustomConfiguration
{
    private const byte GoldFreeGroup = 14;
    private const short GoldFreeNumber = 100;

    /// <inheritdoc />
    public override string Key => "/trocar";

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <summary>
    /// Gets or sets the configuration.
    /// </summary>
    public ZenToGoldFreeExchangeConfiguration? Configuration { get; set; }

    /// <inheritdoc />
    public object CreateDefaultConfig()
    {
        return new ZenToGoldFreeExchangeConfiguration();
    }

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, ZenToGoldFreeExchangeArguments arguments)
    {
        if (player.Inventory is null || player.GameContext?.Configuration is null)
            return;

        var inventory = player.Inventory;
        var config = this.Configuration ??= (ZenToGoldFreeExchangeConfiguration)this.CreateDefaultConfig();

        if (arguments.Quantidade <= 0)
        {
            await player.ShowBlueMessageAsync("Usage: /trocar <quantidade> (e.g., /trocar 5)").ConfigureAwait(false);
            return;
        }

        if (arguments.Quantidade > config.MaxGoldFreePerExchange)
        {
            await player.ShowBlueMessageAsync($"Maximum {config.MaxGoldFreePerExchange} GOLD FREE per exchange.").ConfigureAwait(false);
            return;
        }

        var totalZenRequired = config.ZenPerGoldFree * arguments.Quantidade;

        // Check for overflow: totalZenRequired must fit in int since player.Money is int
        if (totalZenRequired > int.MaxValue)
        {
            await player.ShowBlueMessageAsync($"Exchange amount too large. Maximum allowed: {int.MaxValue / config.ZenPerGoldFree} GOLD FREE.").ConfigureAwait(false);
            return;
        }

        var totalZenRequiredInt = (int)totalZenRequired;

        if (player.Money < totalZenRequiredInt)
        {
            await player.ShowBlueMessageAsync($"Not enough Zen. Required: {totalZenRequiredInt:N0}, you have: {player.Money:N0}").ConfigureAwait(false);
            return;
        }

        var goldFreeDefinition = player.GameContext.Configuration.Items
            .FirstOrDefault(def => def.Group == GoldFreeGroup && def.Number == GoldFreeNumber);

        if (goldFreeDefinition is null)
        {
            await player.ShowBlueMessageAsync("GOLD FREE item (14,100) not found in server configuration.").ConfigureAwait(false);
            return;
        }

        // Remove o zen do jogador (valida saldo internamente; a checagem acima já garante
        // que há saldo, mas usamos o método idiomático em vez de mexer em Money na mão).
        if (!player.TryRemoveMoney(totalZenRequiredInt))
        {
            await player.ShowBlueMessageAsync($"Not enough Zen. Required: {totalZenRequiredInt:N0}, you have: {player.Money:N0}").ConfigureAwait(false);
            return;
        }

        var remaining = arguments.Quantidade;
        var itemsAdded = 0;
        var newItems = new List<Item>();
        var modifiedStacks = new HashSet<Item>();

        while (remaining > 0)
        {
            // Procura uma pilha existente de GOLD FREE com espaço (< 255)
            var stack = inventory.Items.FirstOrDefault(i =>
                i.Definition?.Group == GoldFreeGroup
                && i.Definition?.Number == GoldFreeNumber
                && i.Durability < 255);

            if (stack is { })
            {
                // Soma na pilha existente (1 slot só)
                var add = Math.Min(255 - (int)stack.Durability, remaining);
                stack.Durability += add;
                remaining -= add;
                itemsAdded += add;
                modifiedStacks.Add(stack);
            }
            else
            {
                // Cria UMA pilha nova com a contagem
                var add = Math.Min(255, remaining);
                var newItem = player.PersistenceContext.CreateNew<Item>();
                newItem.Definition = goldFreeDefinition;
                newItem.Level = 0;
                newItem.Durability = add;

                if (await inventory.AddItemAsync(newItem).ConfigureAwait(false) is false)
                {
                    break; // inventário cheio
                }

                remaining -= add;
                itemsAdded += add;
                newItems.Add(newItem);
            }
        }

        // Devolve o zen de qualquer quantidade que não entrou
        var notAdded = arguments.Quantidade - itemsAdded;
        if (notAdded > 0)
        {
            var refundZen = (int)(config.ZenPerGoldFree * notAdded);
            player.TryAddMoney(refundZen);
        }

        if (itemsAdded > 0)
        {
            // Notifica o cliente em tempo real (sem precisar relogar):
            // - itens novos aparecem no slot correto via IItemAppearPlugIn;
            // - pilhas existentes que só tiveram a quantidade (Durability) alterada
            //   são atualizadas via IItemDurabilityChangedPlugIn.
            foreach (var newItem in newItems)
            {
                await player.InvokeViewPlugInAsync<IItemAppearPlugIn>(p => p.ItemAppearAsync(newItem)).ConfigureAwait(false);
            }

            foreach (var stack in modifiedStacks)
            {
                await player.InvokeViewPlugInAsync<IItemDurabilityChangedPlugIn>(p => p.ItemDurabilityChangedAsync(stack, false)).ConfigureAwait(false);
            }

            var zenSpent = config.ZenPerGoldFree * itemsAdded;
            await player.ShowBlueMessageAsync($"Exchanged {zenSpent:N0} Zen for {itemsAdded} GOLD FREE!").ConfigureAwait(false);
        }
        else
        {
            player.TryAddMoney(totalZenRequiredInt);
            await player.ShowBlueMessageAsync("Inventory full. Exchange cancelled, Zen refunded.").ConfigureAwait(false);
        }
    }
}