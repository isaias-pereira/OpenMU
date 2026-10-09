// <copyright file="CashShopStorageService.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.CashShop;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Implementação de <see cref="ICashShopStorageService"/> sobre o <see cref="IContext"/> do jogador.
/// </summary>
/// <remarks>
/// A leitura usa a API pública <see cref="IContext.GetAsync{T}"/>, que é o mesmo caminho
/// usado pelo resto do <c>GameLogic</c> (por exemplo <c>VipService</c>): o
/// <c>GameLogic</c> não referencia o projeto de Entity Framework, então um repositório
/// específico não pode ser usado daqui.
/// <para>
/// O filtro por conta é feito em memória. Isso é seguro porque o storage é limitado a
/// <see cref="Capacity"/> itens por conta e o índice único <c>(AccountId, StorageIndex)</c>
/// impede linhas duplicadas.
/// </para>
/// </remarks>
public class CashShopStorageService : ICashShopStorageService
{
    /// <summary>
    /// Itens por página do storage. O cliente renderiza esta quantidade por página e o
    /// total de páginas é derivado dela (<c>D2 06</c>: <c>wTotalPage = ceil(total / PageSize)</c>).
    /// </summary>
    public const int PageSize = 30;

    private readonly Func<IContext> _contextFactory;

    /// <summary>
    /// Inicializa uma nova instância do <see cref="CashShopStorageService"/>.
    /// </summary>
    /// <param name="contextFactory">Fornece o contexto de persistência da conta.</param>
    public CashShopStorageService(Func<IContext> contextFactory)
    {
        this._contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    /// <inheritdoc />
    public int Capacity => PageSize;

    /// <inheritdoc />
    public async ValueTask<int> GetItemCountAsync(Guid accountId, bool includeGifts = false, CancellationToken cancellationToken = default)
    {
        var items = await this.GetItemsAsync(accountId, includeGifts, cancellationToken).ConfigureAwait(false);
        return items.Count;
    }

    /// <inheritdoc />
    public async ValueTask<int> GetGiftCountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var items = await this.GetItemsForTabAsync(accountId, includeGifts: false, giftsOnly: true, cancellationToken).ConfigureAwait(false);
        return items.Count;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CashShopStorageItem>> GetPageAsync(
        Guid accountId,
        int skip,
        int take,
        bool includeGifts = false,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take <= 0)
        {
            return Array.Empty<CashShopStorageItem>();
        }

        var items = await this.GetItemsAsync(accountId, includeGifts, cancellationToken).ConfigureAwait(false);
        return items.Skip(skip).Take(take).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CashShopStorageItem>> GetGiftPageAsync(
        Guid accountId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take <= 0)
        {
            return Array.Empty<CashShopStorageItem>();
        }

        var items = await this.GetItemsForTabAsync(accountId, includeGifts: false, giftsOnly: true, cancellationToken).ConfigureAwait(false);
        return items.Skip(skip).Take(take).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<int> GetNextStorageIndexAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var items = await this.GetItemsAsync(accountId, includeGifts: false, cancellationToken).ConfigureAwait(false);

        // Os índices são atribuídos sequencialmente e não são reutilizados enquanto a
        // linha existir, então o próximo índice livre é o menor índice não usado. Encontrar
        // o primeiro livre (em vez de "máximo + 1") mantém a alocação dentro de
        // [0, Capacity): se a linha de maior índice for removida, "máximo + 1" entregaria
        // um índice fora do alcance das páginas do cliente, enquanto o primeiro livre
        // reaproveita o buraco.
        var usedIndices = new HashSet<int>(items.Count);
        foreach (var item in items)
        {
            usedIndices.Add(item.StorageIndex);
        }

        for (var index = 0; index < this.Capacity; index++)
        {
            if (!usedIndices.Contains(index))
            {
                return index;
            }
        }

        // Storage cheio: não há índice livre. O chamador já verificou a capacidade antes,
        // mas um índice igual à capacidade é o sinal seguro de que a conta encheu entre a
        // checagem e aqui, e o servidor não deve inventar um índice fora de [0, Capacity).
        return this.Capacity;
    }

    /// <inheritdoc />
    public async ValueTask<CashShopStorageReservation> ReserveNextStorageIndexAsync(
        IContext context,
        Guid accountId,
        bool forGift = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Lê os índices já conhecidos no MESMO contexto que vai gravar. Isso é o ponto central da
        // reserva: as linhas que este contexto ainda não commitou (por exemplo, uma compra
        // anterior da mesma conta ainda em voo) já aparecem aqui, então duas compras
        // sequenciais no mesmo jogador não podem receber o mesmo índice.
        var items = await context.GetAsync<CashShopStorageItem>(cancellationToken).ConfigureAwait(false);
        var usedIndices = new HashSet<int>();
        foreach (var item in items)
        {
            // Só as linhas da mesma aba competem pelo mesmo índice: o índice é a posição dentro
            // da aba que o cliente está vendo, e a constraint única (AccountId, StorageIndex) é
            // global. Por isso as duas abas NÃO podem compartilhar a numeração, senão um presente
            // ocuparia o slot de uma compra e a entrega seria rejeitada pela constraint.
            if (item.AccountId == accountId && item.IsGift == forGift)
            {
                usedIndices.Add(item.StorageIndex);
            }
        }

        for (var index = 0; index < this.Capacity; index++)
        {
            if (usedIndices.Contains(index))
            {
                continue;
            }

            // A reserva é materializada como uma linha real, anexada ao contexto do chamador,
            // para que a gravação da entrega e a da reserva sejam a mesma transação.
            var storageItem = context.CreateNew<CashShopStorageItem>();
            storageItem.AccountId = accountId;
            storageItem.StorageIndex = index;
            storageItem.IsGift = forGift;

            // O ItemSeq é uma sequência única POR CONTA, cobrindo as duas abas: é o segundo
            // identificador que o cliente devolve no D2 0B e, por ser global à conta, o par
            // (StorageIndex, ItemSeq) nunca colide entre as abas — a requisição de retirada
            // (D2 0B) não carrega a aba, então a identificação precisa ser única sem ela.
            var accountItemSeqs = items.Where(i => i.AccountId == accountId).Select(i => i.ItemSeq).ToList();
            storageItem.ItemSeq = accountItemSeqs.Count == 0 ? 0 : accountItemSeqs.Max() + 1;
            return new CashShopStorageReservation(storageItem, index);
        }

        // Sem índice livre: devolve uma reserva vazia, sem criar nada no contexto. O chamador
        // trata o -1 como "storage cheio"; um StorageItem nulo aqui significaria que ele
        // precisa checar null E o índice, o que é fácil de errar.
        return CashShopStorageReservation.Empty;
    }

    private async ValueTask<IReadOnlyList<CashShopStorageItem>> GetItemsAsync(Guid accountId, bool includeGifts, CancellationToken cancellationToken)
    {
        return await this.GetItemsForTabAsync(accountId, includeGifts, giftsOnly: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lê as linhas de storage de uma conta, filtrando pela aba que o cliente pediu.
    /// </summary>
    /// <param name="accountId">The account id.</param>
    /// <param name="includeGifts">Inclui os presentes junto das compras (aba 'S' com ambas).</param>
    /// <param name="giftsOnly">Lê somente os presentes recebidos (aba 'G').</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async ValueTask<IReadOnlyList<CashShopStorageItem>> GetItemsForTabAsync(Guid accountId, bool includeGifts, bool giftsOnly, CancellationToken cancellationToken)
    {
        // O contexto é descartado explicitamente: _contextFactory cria um contexto novo a cada
        // chamada (ver o registro em Startup/Program.cs), e sem o using cada leitura de storage
        // vazaria um contexto — e, no Entity Framework, a conexão do pool junto.
        using var context = this._contextFactory();
        var all = await context.GetAsync<CashShopStorageItem>(cancellationToken).ConfigureAwait(false);
        return all
            .Where(item => item.AccountId == accountId)
            .Where(item => giftsOnly ? item.IsGift : includeGifts || !item.IsGift)
            .OrderBy(item => item.StorageIndex)
            .ToList();
    }
}
