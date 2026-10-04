// <copyright file="WCoinService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic;

using System.Threading;
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
    /// Tenta debitar WCOIN da carteira da conta <b>sem salvar</b>. Se o saldo for insuficiente,
    /// não altera nada e devolve <c>null</c>. Caso contrário, altera a carteira e cria a linha do
    /// extrato, deixando ambos pendentes no <paramref name="context"/> para que o chamador decida
    /// quando persistir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Usado pelo fluxo de compra da Cash Shop, que precisa gravar o débito e a entrega do item
    /// na <b>mesma</b> unidade de trabalho: um único <c>SaveChangesAsync</c> vira uma única
    /// transação no banco, então ou os dois são gravados ou nenhum é.
    /// </para>
    /// <para>
    /// <b>Escopo da serialização.</b> O chamador precisa serializar as operações da mesma conta
    /// (por exemplo com o <c>AsyncLock</c> por conta de <c>CashShopBuyService</c>) para que duas
    /// leituras concorrentes do mesmo saldo não passem ambas na verificação. Essa serialização
    /// cobre <b>um único processo</b>.
    /// </para>
    /// <para>
    /// <b>Limite conhecido:</b> a constraint <c>CK_WCoinWallet_Balance (Balance &gt;= 0)</c> só
    /// impede saldo negativo. Ela <b>não</b> impede perda de atualização nem débito duplicado
    /// quando dois processos leem o mesmo saldo ao mesmo tempo — ambos gravariam o mesmo
    /// <c>Balance</c> derivado. Proteção contra esse cenário exigiria uma atualização
    /// condicional no banco, que a abstração <see cref="IContext"/> não oferece.
    /// </para>
    /// </remarks>
    /// <param name="context">O contexto onde a alteração é preparada.</param>
    /// <param name="accountId">O id da conta.</param>
    /// <param name="amount">O valor a debitar (tem de ser positivo).</param>
    /// <param name="type">O tipo da movimentação.</param>
    /// <param name="description">Descrição livre da movimentação.</param>
    /// <returns>
    /// O débito preparado, ou <c>null</c> se não há saldo. Guarde o resultado: ele é o que
    /// permite a <see cref="RevertDebitAsync"/> desfazer a operação sem afetar outras alterações
    /// do contexto.
    /// </returns>
    public static async ValueTask<WCoinDebit?> TryDebit(
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
            return null;
        }

        var previousBalance = wallet.Balance;
        var previousUpdatedAt = wallet.UpdatedAt;
        wallet.Balance -= amount;
        wallet.UpdatedAt = DateTime.UtcNow;

        WCoinTransaction entry;
        try
        {
            entry = context.CreateNew<WCoinTransaction>();
        }
        catch (Exception)
        {
            // Creating the statement failed after the wallet was already mutated. Reverting here
            // is what keeps the in-memory balance from staying debited without any handle to undo
            // it later: the caller would never receive a WCoinDebit, so the mutation would
            // survive and be persisted by the next save of this context.
            wallet.Balance = previousBalance;
            wallet.UpdatedAt = previousUpdatedAt;
            throw;
        }

        entry.AccountId = accountId;
        entry.Type = type;
        entry.Amount = amount;
        entry.BalanceAfter = wallet.Balance;
        entry.Description = description;

        // The wallet instance travels with the handle so that reverting never depends on a
        // second lookup that could come back empty and silently skip the restore.
        return new WCoinDebit(entry, wallet, previousBalance, previousUpdatedAt);
    }

    /// <summary>
    /// Desfaz um débito preparado por <see cref="TryDebit"/> que não chegou a ser gravado:
    /// restaura a carteira exatamente ao valor anterior e desanexa a linha do extrato.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Só a carteira e o extrato criados por <b>aquele</b> débito são tocados; as demais
    /// entidades do contexto (inclusive alterações legítimas anteriores do jogador) permanecem
    /// intactas.
    /// </para>
    /// <para>
    /// A restauração usa a <i>mesma instância</i> de carteira que <see cref="TryDebit"/>
    /// alterou, guardada em <see cref="WCoinDebit.Wallet"/>. Uma segunda busca
    /// (<c>GetByIdAsync</c>) seria frágil: o contexto pode já ter sido desfazido pelo
    /// <c>SaveChangesAsync</c> que falhou, e nesse caso a busca devolveria <c>null</c> e a
    /// carteira ficaria com o saldo debitado em memória, para ser gravada por um salvamento
    /// posterior. Por isso a restauração não faz consulta alguma.
    /// </para>
    /// </remarks>
    /// <param name="context">O contexto onde o débito foi preparado.</param>
    /// <param name="debit">O débito a desfazer.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public static ValueTask RevertDebitAsync(IContext context, WCoinDebit debit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(debit);
        cancellationToken.ThrowIfCancellationRequested();

        // TryDebit guardou a instância que ele alterou; usá-la diretamente garante que o saldo
        // em memória volte exatamente ao valor anterior, sem depender de nova consulta.
        debit.Wallet.Balance = debit.PreviousBalance;
        debit.Wallet.UpdatedAt = debit.PreviousUpdatedAt;

        // Desanexa apenas a linha do extrato deste débito. A carteira continua rastreada
        // (e restaurada), preservando alterações legítimas que o jogador já tivesse.
        context.Detach(debit.Entry);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Tenta debitar WCOIN da carteira da conta em uma única unidade de trabalho. Se o saldo for
    /// insuficiente, não altera nada e retorna <c>false</c> — sem isso, dois comandos
    /// simultâneos (ex.: dois cliques rápidos na loja) poderiam gastar o mesmo saldo duas vezes.
    /// </summary>
    /// <remarks>
    /// Diferente de <see cref="TryDebit"/>, que só prepara o débito para o chamador decidir, este
    /// método faz o commit. Se a gravação lançar, o débito é desfeito com
    /// <see cref="RevertDebitAsync"/> e a exceção original é propagada. Sem essa reversão, uma
    /// falha de gravação deixaria o saldo já debitado em memória e um salvamento posterior do
    /// mesmo contexto cobraria o jogador por uma entrega que nunca aconteceu.
    /// </remarks>
    public static async ValueTask<bool> TryDebitAsync(
        IContext context,
        Guid accountId,
        long amount,
        WCoinTransactionType type,
        string? description = null)
    {
        var debit = await TryDebit(context, accountId, amount, type, description).ConfigureAwait(false);
        if (debit is null)
        {
            return false;
        }

        try
        {
            return await context.SaveChangesAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best effort, but never silent: if the revert itself fails the caller must learn
            // that the context is dirty instead of assuming a clean state.
            await RevertDebitAsync(context, debit).ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>
/// Um débito de WCOIN preparado por <see cref="WCoinService.TryDebit"/> e ainda não gravado.
/// </summary>
/// <remarks>
/// Guarda os valores anteriores da carteira — e a <b>instância</b> que foi alterada — para que
/// a operação possa ser desfeita com <see cref="WCoinService.RevertDebitAsync"/> se a unidade de
/// trabalho que a utiliza não chegar a ser salva.
/// </remarks>
/// <param name="Entry">A linha do extrato criada para o débito.</param>
/// <param name="Wallet">
/// A instância de carteira que <see cref="WCoinService.TryDebit"/> alterou. Guardá-la é o que
/// permite desfazer o débito sem depender de uma nova consulta, que poderia não devolver nada.
/// </param>
/// <param name="PreviousBalance">O saldo da carteira antes do débito.</param>
/// <param name="PreviousUpdatedAt">A data de atualização da carteira antes do débito.</param>
public sealed record WCoinDebit(
    WCoinTransaction Entry,
    WCoinWallet Wallet,
    long PreviousBalance,
    DateTime PreviousUpdatedAt);
