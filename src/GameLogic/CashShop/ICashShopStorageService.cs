// <copyright file="ICashShopStorageService.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.CashShop;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Acesso ao storage de itens da Cash Shop da conta (o "Cash Shop Item Storage" do cliente).
/// </summary>
public interface ICashShopStorageService
{
    /// <summary>
    /// Quantos itens o storage da conta aceita no máximo.
    /// </summary>
    /// <remarks>
    /// O valor é a capacidade de uma página do storage do cliente. O cliente não impõe
    /// limite próprio: é o servidor que decide, e o número de páginas é derivado
    /// (<c>D2 06</c>: <c>wTotalPage = ceil(total / pageSize)</c>). O valor 30 foi escolhido
    /// por ser o número de linhas que a lista do storage exibe sem rolagem.
    /// </remarks>
    int Capacity { get; }

    /// <summary>
    /// Conta os itens que a conta já tem no storage.
    /// </summary>
    /// <param name="accountId">O id da conta.</param>
    /// <param name="includeGifts">
    /// Quando <c>true</c>, conta as compras e os presentes recebidos; quando <c>false</c>, conta
    /// apenas as compras (a aba 'S').
    /// </param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A quantidade de itens no storage.</returns>
    ValueTask<int> GetItemCountAsync(Guid accountId, bool includeGifts = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Conta os itens no storage lendo pelo contexto informado (Rodada 2V).
    /// </summary>
    /// <remarks>
    /// O contexto do jogador (caching) já tem as entidades da sessão rastreadas, evitando a
    /// materialização dos agregados row → Item → Definition que o contexto "fresco" da factory
    /// refaz a cada leitura (89 s medidos na Rodada 2U).
    /// </remarks>
    /// <param name="context">O contexto de leitura (normalmente o do jogador).</param>
    /// <param name="accountId">O id da conta.</param>
    /// <param name="includeGifts">Quando <c>true</c>, inclui os presentes.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A quantidade de itens no storage.</returns>
    ValueTask<int> GetItemCountAsync(IContext context, Guid accountId, bool includeGifts = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lê uma página de itens do storage, ordenados por <see cref="CashShopStorageItem.StorageIndex"/>.
    /// </summary>
    /// <param name="accountId">O id da conta.</param>
    /// <param name="skip">Quantos itens pular.</param>
    /// <param name="take">Quantos itens ler (o tamanho da página).</param>
    /// <param name="includeGifts">
    /// Quando <c>true</c>, devolve as compras e os presentes recebidos; quando <c>false</c>,
    /// devolve apenas as compras (a aba 'S'). A aba 'G' do cliente é lida separadamente por
    /// <see cref="GetGiftPageAsync"/>.
    /// </param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Os itens da página, em ordem de índice.</returns>
    ValueTask<IReadOnlyList<CashShopStorageItem>> GetPageAsync(
        Guid accountId,
        int skip,
        int take,
        bool includeGifts = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lê uma página de itens do storage pelo contexto informado (Rodada 2V).
    /// </summary>
    /// <param name="context">O contexto de leitura (normalmente o do jogador).</param>
    /// <param name="accountId">O id da conta.</param>
    /// <param name="skip">Quantos itens pular.</param>
    /// <param name="take">Quantos itens ler (o tamanho da página).</param>
    /// <param name="includeGifts">Quando <c>true</c>, inclui os presentes.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Os itens da página, em ordem de índice.</returns>
    ValueTask<IReadOnlyList<CashShopStorageItem>> GetPageAsync(
        IContext context,
        Guid accountId,
        int skip,
        int take,
        bool includeGifts = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lê uma página dos presentes recebidos pela conta (a aba 'G' do cliente).
    /// </summary>
    /// <param name="accountId">O id da conta.</param>
    /// <param name="skip">Quantos itens pular.</param>
    /// <param name="take">Quantos itens ler (o tamanho da página).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Os presentes da conta, em ordem de índice.</returns>
    ValueTask<IReadOnlyList<CashShopStorageItem>> GetGiftPageAsync(
        Guid accountId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lê uma página dos presentes recebidos lendo pelo contexto informado (Rodada 2AD).
    /// </summary>
    /// <remarks>
    /// Mesma justificativa dos overloads da Rodada 2V: o contexto "fresco" da factory
    /// materializa os agregados row → Item → Definition a cada leitura (60–71 s medidos
    /// na aba G na Rodada 2AE).
    /// </remarks>
    /// <param name="context">O contexto de leitura (normalmente o do jogador).</param>
    /// <param name="accountId">O id da conta.</param>
    /// <param name="skip">Quantos itens pular.</param>
    /// <param name="take">Quantos itens ler (o tamanho da página).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Os presentes da conta, em ordem de índice.</returns>
    ValueTask<IReadOnlyList<CashShopStorageItem>> GetGiftPageAsync(
        IContext context,
        Guid accountId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Conta os presentes recebidos pela conta.
    /// </summary>
    /// <param name="accountId">O id da conta.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A quantidade de presentes recebidos.</returns>
    ValueTask<int> GetGiftCountAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Conta os presentes recebidos lendo pelo contexto informado (Rodada 2AD).
    /// </summary>
    /// <param name="context">O contexto de leitura (normalmente o do jogador).</param>
    /// <param name="accountId">O id da conta.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A quantidade de presentes recebidos.</returns>
    ValueTask<int> GetGiftCountAsync(IContext context, Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devolve o próximo <see cref="CashShopStorageItem.StorageIndex"/> livre da conta.
    /// </summary>
    /// <param name="accountId">O id da conta.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>
    /// O índice livre, sempre dentro de <c>[0, <see cref="Capacity"/>)</c>; quando não há índice
    /// livre, devolve <see cref="Capacity"/> como sinal de storage cheio.
    /// </returns>
    /// <remarks>
    /// Esta leitura é feita por um contexto próprio, separado do contexto que grava a
    /// entrega. Ela sozinha <b>não</b> torna a alocação atômica: entre a leitura e o commit,
    /// outro processo pode ocupar o mesmo índice. Use
    /// <see cref="ReserveNextStorageIndexAsync"/> para reservar o índice na mesma unidade de
    /// trabalho da gravação, que é o caminho usado pela compra.
    /// </remarks>
    ValueTask<int> GetNextStorageIndexAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserva o próximo índice livre do storage <b>na mesma unidade de trabalho</b> que grava a
    /// entrega, para que a alocação e a gravação sejam resolvidas em um único commit.
    /// </summary>
    /// <param name="context">O contexto que gravará a entrega.</param>
    /// <param name="accountId">O id da conta.</param>
    /// <param name="forGift">
    /// Reserva um slot na aba de presentes em vez da aba de compras. As duas abas tem numeracao
    /// independente, porque o indice e a posicao dentro da aba que o cliente esta vendo, enquanto
    /// a constraint unica (AccountId, StorageIndex) e global: se compartilhassem a numeracao, um
    /// presente ocuparia o slot de uma compra e a entrega seria rejeitada.
    /// </param>
    /// <returns>
    /// O índice reservado, ou <c>-1</c> se o storage da conta já estiver cheio.
    /// </returns>
    /// <remarks>
    /// A linha reservada é criada (não apenas calculada) em <paramref name="context"/>, de modo
    /// que ela participe da mesma transação do débito. Se o commit falhar, o chamador desfaz a
    /// reserva junto com o resto da unidade de trabalho.
    /// <para>
    /// <b>Escopo.</b> Isso fecha a janela de corrida <i>dentro</i> da unidade de trabalho: a
    /// capacidade e a índice são lidos do mesmo contexto que grava, e a constraint única
    /// <c>(AccountId, StorageIndex)</c> continua sendo a rede de segurança final contra outro
    /// processo. A serialização entre processos não é coberta aqui — ver as observações de
    /// <see cref="PlayerActions.CashShop.CashShopBuyService"/>.
    /// </para>
    /// </remarks>
    ValueTask<CashShopStorageReservation> ReserveNextStorageIndexAsync(
        IContext context,
        Guid accountId,
        bool forGift = false,
        CancellationToken cancellationToken = default);
}


/// <summary>
/// O slot reservado no storage da conta, junto da linha criada que o materializa.
/// </summary>
/// <param name="StorageItem">
/// A linha de storage criada por <see cref="ICashShopStorageService.ReserveNextStorageIndexAsync"/>.
/// Ela já está anexada ao contexto do chamador e ainda não foi salva: quem reservou precisa
/// preenchê-la e commitar, ou descartá-la. É <c>null</c> apenas em <see cref="Empty"/>.
/// </param>
/// <param name="StorageIndex">
/// O <see cref="CashShopStorageItem.StorageIndex"/> reservado, ou <c>-1</c> em <see cref="Empty"/>.
/// </param>
public sealed record CashShopStorageReservation(CashShopStorageItem? StorageItem, int StorageIndex)
{
    /// <summary>
    /// Reserva vazia: o storage da conta não tem índice livre. Nada foi criado no contexto.
    /// </summary>
    public static CashShopStorageReservation Empty { get; } = new(null, -1);
}
