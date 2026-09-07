// <copyright file="VipChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.PlugIns;

public class VipCommandConfiguration
{
    [Display(Name = "Preço Bronze (WCoin)")]
    public int BronzePrice { get; set; } = 50;

    [Display(Name = "Preço Silver (WCoin)")]
    public int SilverPrice { get; set; } = 100;

    [Display(Name = "Preço Gold (WCoin)")]
    public int GoldPrice { get; set; } = 200;

    [Display(Name = "Preço Platinum (WCoin)")]
    public int PlatinumPrice { get; set; } = 400;

    [Display(Name = "Duração padrão (dias)")]
    public int DurationDays { get; set; } = 30;

    [Display(Name = "Comando habilitado")]
    public bool Enabled { get; set; } = true;
}

public class VipCommandArguments : ArgumentsBase
{
    public string? Action { get; set; }

    public string? Plan { get; set; }
}

[System.Runtime.InteropServices.Guid("E1F2A3B4-C5D6-7890-ABCD-EF1234567890")]
[PlugIn]
[Display(Name = "VIP Command", Description = "Ativa planos VIP usando WCoin")]
[ChatCommandHelp("/vip", typeof(VipCommandArguments), CharacterStatus.Normal)]
public class VipChatCommandPlugIn : ChatCommandPlugInBase<VipCommandArguments>,
    ISupportCustomConfiguration<VipCommandConfiguration>, ISupportDefaultCustomConfiguration
{
    public VipCommandConfiguration? Configuration { get; set; }

    public override string Key => "/vip";

    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    public object CreateDefaultConfig() => new VipCommandConfiguration();

    protected override async ValueTask DoHandleCommandAsync(Player player, VipCommandArguments arguments)
    {
        var config = this.Configuration ?? new VipCommandConfiguration();
        if (!config.Enabled)
        {
            await ShowMessageAsync(player, "O sistema de VIP esta desligado no momento.").ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(arguments.Action) || !arguments.Action.Equals("ativar", StringComparison.OrdinalIgnoreCase))
        {
            await ShowMessageAsync(player, "Uso: /vip ativar <Bronze|Silver|Gold|Platinum>").ConfigureAwait(false);
            return;
        }

        if (!VipService.TryGetPlanByName(arguments.Plan, out var plan))
        {
            await ShowMessageAsync(player, "Plano invalido. Use: Bronze, Silver, Gold ou Platinum.").ConfigureAwait(false);
            return;
        }

        var price = this.GetPrice(config, plan);

        if (player.Account is null)
        {
            await ShowMessageAsync(player, "Erro de conta. Fale com um GM.").ConfigureAwait(false);
            return;
        }

        var accountId = player.Account.Id;
        var context = player.PersistenceContext;

        // 1. Verifica saldo de WCoin.
        var balance = await WCoinService.GetBalanceAsync(context, accountId).ConfigureAwait(false);
        if (balance < price)
        {
            await ShowMessageAsync(player, $"Saldo insuficiente. Voce precisa de {price} WCoin. Seu saldo: {balance}.").ConfigureAwait(false);
            return;
        }

        // 2. Debita o WCoin (transação atômica na carteira).
        var debited = await WCoinService.TryDebitAsync(
            context,
            accountId,
            price,
            WCoinTransactionType.Purchase,
            $"Ativacao VIP {plan.Name}").ConfigureAwait(false);

        if (!debited)
        {
            await ShowMessageAsync(player, "Erro ao processar o pagamento. Tente novamente.").ConfigureAwait(false);
            return;
        }

        // 3. Ativa o VIP. Se falhar por qualquer motivo, ESTORNA o WCoin — o jogador nunca
        //    pode pagar sem receber o benefício.
        try
        {
            var expiresAt = await VipService.ActivateVipAsync(
                context,
                accountId,
                plan,
                config.DurationDays,
                "ChatCommand").ConfigureAwait(false);

            await ShowMessageAsync(player, $"VIP {plan.Name} ativado com sucesso! Expira em: {expiresAt:dd/MM/yyyy HH:mm}.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            player.Logger.LogError(ex, "Falha ao ativar o VIP {Plan} para a conta {AccountId}. Estornando {Price} WCoin.", plan.Name, accountId, price);

            try
            {
                await WCoinService.CreditAsync(
                    context,
                    accountId,
                    price,
                    WCoinTransactionType.AdminAdjust,
                    $"Estorno VIP {plan.Name} (falha na ativacao)").ConfigureAwait(false);

                await ShowMessageAsync(player, "Nao foi possivel ativar o VIP. Seu WCoin foi estornado. Tente novamente.").ConfigureAwait(false);
            }
            catch (Exception refundEx)
            {
                player.Logger.LogError(refundEx, "FALHA CRITICA ao estornar {Price} WCoin da conta {AccountId} apos erro na ativacao do VIP.", price, accountId);
                await ShowMessageAsync(player, "Erro ao ativar o VIP. Contate um GM informando o horario.").ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask ShowMessageAsync(Player player, string message)
    {
        await player.InvokeViewPlugInAsync<IShowMessagePlugIn>(
            p => p.ShowMessageAsync(message, MessageType.GoldenCenter)).ConfigureAwait(false);
    }

    private int GetPrice(VipCommandConfiguration config, VipPlanDefinition plan)
    {
        if (plan.Id == VipService.Bronze.Id)
        {
            return config.BronzePrice;
        }

        if (plan.Id == VipService.Silver.Id)
        {
            return config.SilverPrice;
        }

        if (plan.Id == VipService.Gold.Id)
        {
            return config.GoldPrice;
        }

        return config.PlatinumPrice;
    }
}
