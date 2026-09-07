// <copyright file="WCoinService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic;

using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Operações de leitura/crédito/débito de WCOIN. Todo crédito ou débito grava também
/// uma linha no extrato (<see cref="WCoinTransaction"/>) e salva os dois junto, no
/// mesmo <see cref="IContext.SaveChangesAsync"/> — ou os dois são gravados, ou nenhum.
/// </summary>
/// <remarks>
/// Chame estes métodos passando <c>player.PersistenceContext</c> (implementa
/// <see cref="IContext"/>) e <c>player.Account!.Id</c>, do mesmo jeito que o
/// GoldFreeShopPlugIn já usa player.PersistenceContext para itens.
/// </remarks>
public static class WCoinService
{
    /// <summary>Consulta o saldo atual da conta (0 se ela ainda não tiver carteira).</summary>
    public static async ValueTask<long> GetBalanceAsync(IContext context, Guid accountId)
    {
        var wallet = await context.GetByIdAsync<WCoinWallet>(accountId).ConfigureAwait(false);
        return wallet?.Balance ?? 0;
    }

    /// <summary>
    /// Credita WCOIN na carteira da conta (criando a carteira se for a primeira vez)
    /// e grava o extrato. Retorna o novo saldo.
    /// </summary>
    public static async ValueTask<long> CreditAsync(
        IContext context,
        Guid accountId,
        long amount,
        WCoinTransactionType type,
        string? description = null)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "O valor a creditar precisa ser positivo.");
        }

        var wallet = await context.GetByIdAsync<WCoinWallet>(accountId).ConfigureAwait(false)
                     ?? context.CreateNew<WCoinWallet>(accountId);

        wallet.Balance = checked(wallet.Balance + amount);
        wallet.UpdatedAt = DateTime.UtcNow;

        var entry = context.CreateNew<WCoinTransaction>();
        entry.AccountId = accountId;
        entry.Type = type;
        entry.Amount = amount;
        entry.BalanceAfter = wallet.Balance;
        entry.Description = description;

        await context.SaveChangesAsync().ConfigureAwait(false);
        return wallet.Balance;
    }

    /// <summary>
    /// Tenta debitar WCOIN da carteira da conta. Se o saldo for insuficiente, não
    /// altera nada e retorna <c>false</c> — sem isso, dois comandos simultâneos
    /// (ex.: dois cliques rápidos na loja) poderiam gastar o mesmo saldo duas vezes.
    /// </summary>
    public static async ValueTask<bool> TryDebitAsync(
        IContext context,
        Guid accountId,
        long amount,
        WCoinTransactionType type,
        string? description = null)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "O valor a debitar precisa ser positivo.");
        }

        var wallet = await context.GetByIdAsync<WCoinWallet>(accountId).ConfigureAwait(false);
        if (wallet is null || wallet.Balance < amount)
        {
            return false;
        }

        wallet.Balance -= amount;
        wallet.UpdatedAt = DateTime.UtcNow;

        var entry = context.CreateNew<WCoinTransaction>();
        entry.AccountId = accountId;
        entry.Type = type;
        entry.Amount = amount;
        entry.BalanceAfter = wallet.Balance;
        entry.Description = description;

        return await context.SaveChangesAsync().ConfigureAwait(false);
    }
}
