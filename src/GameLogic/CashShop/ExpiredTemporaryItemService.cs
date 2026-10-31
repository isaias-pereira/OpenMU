// <copyright file="ExpiredTemporaryItemService.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.CashShop;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Views.Inventory;

/// <summary>
/// Remove os itens temporários expirados do jogador (GATE G — limpeza de expiração).
/// </summary>
/// <remarks>
/// <para>
/// <b>Gatilhos sem custo de polling</b>: a varredura roda (1) na entrada do personagem no mundo
/// (reconciliação offline — cobre itens que expiraram enquanto a conta estava fora), (2) na
/// abertura do baú (reconciliação do warehouse) e (3) no ciclo de recuperação periódico do
/// <c>GameContext</c>, que já itera os jogadores conectados para regeneração. Em nenhum momento
/// há consulta SQL por tick: a verificação é uma comparação de propriedade em memória
/// (<see cref="ItemExpiryPolicy.IsExpired"/>); o banco só é tocado quando algo expirou de fato.
/// </para>
/// <para>
/// <b>Efeito</b>: o item é retirado do slot (equipado ou não) e removido da persistência no
/// MESMO save. Ao sair de um slot equipado, o mecanismo existente do
/// <see cref="InventoryStorage"/> dispara <c>EquippedItemsChanged</c>, que descarta os power-ups
/// do item (recálculo de atributos) e atualiza a aparência para os observadores; o pacote de
/// remoção no cliente sai pelo <c>IItemRemovedPlugIn</c> existente. Idempotente: uma segunda
/// varredura não encontra mais nada.
/// </para>
/// <para>
/// <b>Segurança</b>: cada item é removido da view e da persistência com falhas isoladas — uma
/// falha em um item não interrompe os demais; o save único acontece apenas se algo foi removido.
/// </para>
/// </remarks>
public static class ExpiredTemporaryItemService
{
    /// <summary>
    /// Remove todos os itens temporários expirados do inventário (incluídos os equipados) e do
    /// baú do jogador, com um único save ao final. Não faz nada quando não há itens expirados.
    /// </summary>
    /// <param name="player">O jogador cujos armazenamentos serão varridos.</param>
    /// <returns>A quantidade de itens removidos.</returns>
    public static async ValueTask<int> SweepExpiredItemsAsync(Player player)
    {
        var removed = 0;

        var inventory = player.Inventory;
        if (inventory is not null)
        {
            foreach (var item in inventory.Items.Where(IsInvalidOrExpired).ToList())
            {
                if (await RemoveItemAsync(player, item, inventory.RemoveItemAsync).ConfigureAwait(false))
                {
                    removed++;
                }
            }
        }

        // O baú só existe enquanto o NPC está aberto; a reconciliação dele acontece na abertura
        // (TalkNpcAction), mas a varredura cobre o caso de o baú já estar carregado aqui.
        var vault = player.Vault;
        if (vault is not null)
        {
            foreach (var item in vault.Items.Where(IsInvalidOrExpired).ToList())
            {
                if (await RemoveItemAsync(player, item, vault.RemoveItemAsync).ConfigureAwait(false))
                {
                    removed++;
                }
            }
        }

        if (removed > 0)
        {
            await player.PersistenceContext.SaveChangesAsync().ConfigureAwait(false);
            player.Logger.LogInformation(
                "Expired temporary item cleanup: {Count} item(s) removed from {Player}.",
                removed,
                player.Name);
        }

        return removed;
    }

    /// <summary>
    /// Predicado de remoção da varredura (fail-safe): o item é removido quando o prazo venceu
    /// (<see cref="ItemExpiryPolicy.IsExpired"/>) OU quando o metadado é inconsistente — ativação
    /// sem prazo (<c>ActivatedAtUtc</c> gravado e <c>ExpiresAtUtc</c> nulo). Um item assim nunca
    /// pode virar permanente silenciosamente (invariante 4 do contrato): ativação e prazo são
    /// sempre gravados juntos; se o prazo sumiu, o metadado está corrompido.
    /// </summary>
    /// <param name="item">O item candidato à remoção.</param>
    /// <returns><c>true</c> quando o item deve ser removido pela varredura.</returns>
    private static bool IsInvalidOrExpired(Item item) =>
        item.IsExpired() || (item.ActivatedAtUtc is not null && item.ExpiresAtUtc is null);

    private static async ValueTask<bool> RemoveItemAsync(Player player, Item item, Func<Item, ValueTask> removeFromStorage)
    {
        try
        {
            // A remoção do armazenamento dispara o desequipamento (se equipado): o mecanismo
            // existente do InventoryStorage recém descarta os power-ups e atualiza a aparência.
            await removeFromStorage(item).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            player.Logger.LogError(
                exception,
                "Expired temporary item cleanup: could not remove item {Item} from the storage of {Player}.",
                item,
                player.Name);
            return false;
        }

        try
        {
            await player.PersistenceContext.DeleteAsync(item).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            player.Logger.LogError(
                exception,
                "Expired temporary item cleanup: could not delete item {Item} from the persistence context of {Player}.",
                item,
                player.Name);
            return false;
        }

        try
        {
            await player.InvokeViewPlugInAsync<IItemRemovedPlugIn>(p => p.RemoveItemAsync(item.ItemSlot)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // O pacote ao cliente é o passo menos crítico: o estado do servidor já está correto
            // e a próxima listagem de inventário corrigirá a visão do cliente.
            player.Logger.LogWarning(
                exception,
                "Expired temporary item cleanup: could not notify the client of {Player} about the removed item {Item}.",
                player.Name,
                item);
        }

        return true;
    }
}
