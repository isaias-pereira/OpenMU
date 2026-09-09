// <copyright file="VipChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat command plugin which handles VIP activation and status.
/// </summary>
[Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
[PlugIn]
[Display(Name = nameof(PlugInResources.VipChatCommandPlugIn_Name), Description = nameof(PlugInResources.VipChatCommandPlugIn_Description), ResourceType = typeof(PlugInResources))]
[ChatCommandHelp(Command, typeof(VipCommandArguments), CharacterStatus.Normal)]
public class VipChatCommandPlugIn : ChatCommandPlugInBase<VipChatCommandPlugIn.VipCommandArguments>, ISupportCustomConfiguration<VipChatCommandPlugIn.VipCommandConfiguration>, ISupportDefaultCustomConfiguration
{
    private const string Command = "/vip";

    /// <summary>
    /// Gets or sets the configuration.
    /// </summary>
    public VipCommandConfiguration? Configuration { get; set; }

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public object CreateDefaultConfig() => new VipCommandConfiguration();

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, VipCommandArguments arguments)
    {
        var config = this.Configuration ?? new VipCommandConfiguration();
        if (!config.Enabled)
        {
            await ShowMessageAsync(player, "O sistema de VIP esta desligado no momento.").ConfigureAwait(false);
            return;
        }

        // Se não passou argumentos ou passou apenas "status"/"info", mostra o status do VIP
        if (string.IsNullOrWhiteSpace(arguments.Action)
            || arguments.Action.Equals("status", StringComparison.OrdinalIgnoreCase)
            || arguments.Action.Equals("info", StringComparison.OrdinalIgnoreCase))
        {
            await ShowVipStatusAsync(player).ConfigureAwait(false);
            return;
        }

        if (!arguments.Action.Equals("ativar", StringComparison.OrdinalIgnoreCase))
        {
            await ShowMessageAsync(player, "Uso: /vip [status] | /vip ativar <Bronze|Silver|Gold|Platinum>").ConfigureAwait(false);
            return;
        }

        var planName = arguments.Plan ?? string.Empty;
        Guid planId;
        int price;

        switch (planName.ToLowerInvariant())
        {
            case "bronze": planId = VipService.Bronze.Id; price = config.BronzePrice; break;
            case "silver": planId = VipService.Silver.Id; price = config.SilverPrice; break;
            case "gold": planId = VipService.Gold.Id; price = config.GoldPrice; break;
            case "platinum": planId = VipService.Platinum.Id; price = config.PlatinumPrice; break;
            default:
                await ShowMessageAsync(player, "Plano invalido. Use: Bronze, Silver, Gold ou Platinum.").ConfigureAwait(false);
                return;
        }

        if (player.Account is null)
        {
            await ShowMessageAsync(player, "Erro de conta. Fale com um GM.").ConfigureAwait(false);
            return;
        }

        // 1. Verifica saldo de WCoin
        var balance = await WCoinService.GetBalanceAsync(player.PersistenceContext, player.Account.Id).ConfigureAwait(false);
        if (balance < price)
        {
            await ShowMessageAsync(player, $"Saldo insuficiente. Voce precisa de {price} WCoin. Seu saldo: {balance}.").ConfigureAwait(false);
            return;
        }

        // 2. Tenta ativar o VIP PRIMEIRO (valida o plano antes de debitar)
        try
        {
            await VipService.ActivateVipAsync(
                player.PersistenceContext,
                player.Account.Id,
                planId,
                config.DurationDays,
                "ChatCommand").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(player, $"Erro ao ativar VIP: {ex.Message}. Fale com um GM.").ConfigureAwait(false);
            return;
        }

        // 3. Só debita o WCoin se o VIP foi ativado com sucesso
        var debited = await WCoinService.TryDebitAsync(
            player.PersistenceContext,
            player.Account.Id,
            price,
            WCoinTransactionType.Purchase,
            $"Ativacao VIP {planName}").ConfigureAwait(false);

        if (!debited)
        {
            await ShowMessageAsync(player, "VIP ativado, mas erro ao debitar WCoin. Fale com um GM.").ConfigureAwait(false);
            return;
        }

        // 4. Mensagem de sucesso
        var expiresAt = DateTime.UtcNow.AddDays(config.DurationDays);
        await ShowMessageAsync(player, $"VIP {planName} ativado! Expira: {expiresAt:dd/MM/yyyy HH:mm} (-{price} WCoin).").ConfigureAwait(false);
    }

    private async ValueTask ShowVipStatusAsync(Player player)
    {
        if (player.Account is null)
        {
            await ShowMessageAsync(player, "Erro de conta. Fale com um GM.").ConfigureAwait(false);
            return;
        }

        var status = await VipService.GetVipStatusAsync(player.PersistenceContext, player.Account.Id).ConfigureAwait(false);
        var balance = await WCoinService.GetBalanceAsync(player.PersistenceContext, player.Account.Id).ConfigureAwait(false);

        if (!status.IsActive)
        {
            await ShowMessageAsync(player, "=== SEU STATUS VIP ===").ConfigureAwait(false);
            await ShowMessageAsync(player, "Voce nao possui VIP ativo.").ConfigureAwait(false);
            await ShowMessageAsync(player, $"Saldo WCoin: {balance}").ConfigureAwait(false);
            await ShowMessageAsync(player, "Use /vip ativar <Bronze|Silver|Gold|Platinum> para ativar.").ConfigureAwait(false);
            return;
        }

        await ShowMessageAsync(player, "=== SEU STATUS VIP ===").ConfigureAwait(false);
        await ShowMessageAsync(player, $"Plano: {status.PlanName}").ConfigureAwait(false);
        await ShowMessageAsync(player, $"Expira em: {status.ExpiresAt:dd/MM/yyyy HH:mm}").ConfigureAwait(false);
        await ShowMessageAsync(player, $"Dias restantes: {status.DaysRemaining:F0}").ConfigureAwait(false);
        await ShowMessageAsync(player, $"Bonus de Drop: +{(status.DropMultiplier - 1.0f) * 100:F0}%").ConfigureAwait(false);
        await ShowMessageAsync(player, $"Saldo WCoin: {balance}").ConfigureAwait(false);
    }

    private async ValueTask ShowMessageAsync(Player player, string message)
    {
        await player.ShowBlueMessageAsync(message).ConfigureAwait(false);
    }

    /// <summary>
    /// Arguments for the VIP chat command.
    /// </summary>
    public class VipCommandArguments : ArgumentsBase
    {
        /// <summary>
        /// Gets or sets the action (status, info, ativar).
        /// </summary>
        [Argument("action")]
        public string? Action { get; set; }

        /// <summary>
        /// Gets or sets the plan name (Bronze, Silver, Gold, Platinum).
        /// </summary>
        [Argument("plan")]
        public string? Plan { get; set; }
    }

    /// <summary>
    /// The configuration of a <see cref="VipChatCommandPlugIn"/>.
    /// </summary>
    public class VipCommandConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether the VIP system is enabled.
        /// </summary>
        [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.VipCommandConfiguration_Enabled_Name), Description = nameof(PlugInResources.VipCommandConfiguration_Enabled_Description))]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the duration in days for VIP plans.
        /// </summary>
        [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.VipCommandConfiguration_DurationDays_Name), Description = nameof(PlugInResources.VipCommandConfiguration_DurationDays_Description))]
        public int DurationDays { get; set; } = 30;

        /// <summary>
        /// Gets or sets the price in WCoin for Bronze plan.
        /// </summary>
        [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.VipCommandConfiguration_BronzePrice_Name), Description = nameof(PlugInResources.VipCommandConfiguration_BronzePrice_Description))]
        public int BronzePrice { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the price in WCoin for Silver plan.
        /// </summary>
        [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.VipCommandConfiguration_SilverPrice_Name), Description = nameof(PlugInResources.VipCommandConfiguration_SilverPrice_Description))]
        public int SilverPrice { get; set; } = 2500;

        /// <summary>
        /// Gets or sets the price in WCoin for Gold plan.
        /// </summary>
        [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.VipCommandConfiguration_GoldPrice_Name), Description = nameof(PlugInResources.VipCommandConfiguration_GoldPrice_Description))]
        public int GoldPrice { get; set; } = 5000;

        /// <summary>
        /// Gets or sets the price in WCoin for Platinum plan.
        /// </summary>
        [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.VipCommandConfiguration_PlatinumPrice_Name), Description = nameof(PlugInResources.VipCommandConfiguration_PlatinumPrice_Description))]
        public int PlatinumPrice { get; set; } = 10000;
    }
}