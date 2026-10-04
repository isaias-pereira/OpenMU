// <copyright file="CashShopStorageServiceTests.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CashShop;

using System;
using System.Linq;
using System.Threading.Tasks;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.CashShop;
using MUnique.OpenMU.Persistence.InMemory;
using NUnit.Framework;

/// <summary>
/// Testes da alocação de <see cref="CashShopStorageItem.StorageIndex"/> no storage da conta.
/// </summary>
/// <remarks>
/// A cobertura importa porque o índice é o que o cliente devolve em <c>D2 0A</c>/<c>D2 0B</c>
/// para apanhar o item, e porque a constraint única <c>(AccountId, StorageIndex)</c> faz um
/// índice mal alocado derrubar a compra inteira.
/// <para>
/// <b>Limitação conhecida:</b> o <see cref="InMemoryContext"/> não impõe constraints únicas, então
/// estes testes verificam a <i>atribuição</i> do índice, não a reação do banco a uma violação.
/// A reação do PostgreSQL é coberta indiretamente por
/// <c>CashShopBuyServiceTests.ProcessPurchaseAsync_StorageIndexConstraintViolation_DoesNotChargeThePlayer</c>,
/// que injeta a falha de constraint no commit.
/// </para>
/// </remarks>
[TestFixture]
public class CashShopStorageServiceTests
{
    private const int StorageCapacity = 30;

    private InMemoryPersistenceContextProvider _contextProvider = null!;
    private CashShopStorageService _service = null!;
    private Guid _accountId;

    /// <summary>
    /// The id of an account that is not <see cref="_accountId"/>, used to prove that the
    /// allocation is scoped per account.
    /// </summary>
    private Guid _otherAccountId;

    [SetUp]
    public void SetUp()
    {
        this._contextProvider = new InMemoryPersistenceContextProvider();
        this._service = new CashShopStorageService(() => this._contextProvider.CreateNewContext());
        this._accountId = Guid.NewGuid();
        this._otherAccountId = Guid.NewGuid();
    }

    /// <summary>
    /// O primeiro slot de uma conta vazia é o índice 0, que é o que o cliente espera para a
    /// primeira linha do storage.
    /// </summary>
    [Test]
    public async Task GetNextStorageIndexAsync_EmptyStorage_ReturnsZero()
    {
        Assert.That(await this._service.GetNextStorageIndexAsync(this._accountId), Is.EqualTo(0));
    }

    /// <summary>
    /// Uma conta com um buraco no meio deve reusar o buraco. "Maior índice + 1" devolveria um
    /// índice além do último, espalhando as linhas e fazendo o storage parecer cheio antes da
    /// hora para um cliente que o percorre por índice.
    /// </summary>
    [Test]
    public async Task GetNextStorageIndexAsync_WithHole_ReusesTheLowestFreeIndex()
    {
        await this.SeedAsync(this._accountId, 0, 2);

        Assert.That(await this._service.GetNextStorageIndexAsync(this._accountId), Is.EqualTo(1));
    }

    /// <summary>
    /// Linhas fora de ordem não podem desalinhar a alocação: o índice é a posição, não a ordem
    /// de inserção.
    /// </summary>
    [Test]
    public async Task GetNextStorageIndexAsync_OutOfOrderRows_StillReturnsLowestFreeIndex()
    {
        await this.SeedAsync(this._accountId, 2, 1);

        Assert.That(await this._service.GetNextStorageIndexAsync(this._accountId), Is.EqualTo(0));
    }

    /// <summary>
    /// A alocação é por conta: o storage cheio de uma conta não pode empurrar a próxima linha de
    /// outra conta para fora da capacidade.
    /// </summary>
    [Test]
    public async Task GetNextStorageIndexAsync_FullStorageOfAnotherAccount_StillReturnsFreeIndex()
    {
        await this.SeedAsync(this._otherAccountId, Enumerable.Range(0, StorageCapacity).ToArray());

        Assert.That(await this._service.GetNextStorageIndexAsync(this._accountId), Is.EqualTo(0));
    }

    /// <summary>
    /// Sem índice livre, o serviço devolve a própria capacidade como sinal de "cheio" em vez de
    /// inventar um índice que o cliente não endereça.
    /// </summary>
    [Test]
    public async Task GetNextStorageIndexAsync_FullStorage_ReturnsCapacityAsFullSignal()
    {
        await this.SeedAsync(this._accountId, Enumerable.Range(0, StorageCapacity).ToArray());

        Assert.That(await this._service.GetNextStorageIndexAsync(this._accountId), Is.EqualTo(StorageCapacity));
    }

    /// <summary>
    /// A reserva cria a linha no contexto que vai gravar a entrega, e é isso que a coloca na
    /// mesma transação do débito. Sem isso, o índice seria apenas um número calculado.
    /// </summary>
    [Test]
    public async Task ReserveNextStorageIndexAsync_CreatesTheRowInTheCallersContext()
    {
        using var context = this._contextProvider.CreateNewContext();

        var reservation = await this._service.ReserveNextStorageIndexAsync(context, this._accountId);

        Assert.Multiple(() =>
        {
            Assert.That(reservation.StorageItem, Is.Not.Null);
            Assert.That(reservation.StorageIndex, Is.EqualTo(0));
            Assert.That(reservation.StorageItem!.AccountId, Is.EqualTo(this._accountId));
            Assert.That(reservation.StorageItem.StorageIndex, Is.EqualTo(0));
        });

        // Created through the caller's context, so it is visible there before any save.
        var tracked = await context.GetAsync<CashShopStorageItem>();
        Assert.That(tracked.Count(), Is.EqualTo(1), "The reserved row must live in the caller's unit of work.");
    }

    /// <summary>
    /// Uma segunda reserva no mesmo contexto não pode reusar o slot da primeira: é exatamente a
    /// corrida que a reserva no contexto do jogador existe para fechar, já que ela enxerga as
    /// linhas que esse contexto ainda não commitou.
    [Test]
    public async Task ReserveNextStorageIndexAsync_TwiceInSameContext_DoesNotReuseTheSlot()
    {
        using var context = this._contextProvider.CreateNewContext();

        var first = await this._service.ReserveNextStorageIndexAsync(context, this._accountId);
        var second = await this._service.ReserveNextStorageIndexAsync(context, this._accountId);

        Assert.Multiple(() =>
        {
            Assert.That(first.StorageIndex, Is.EqualTo(0));
            Assert.That(second.StorageIndex, Is.EqualTo(1), "A slot still held by this context must not be handed out twice.");
        });
    }

    /// <summary>
    /// Storage cheio não pode criar nada: uma reserva vazia deixa o contexto intacto, para que o
    /// chamador simplesmente recuse a compra.
    /// </summary>
    [Test]
    public async Task ReserveNextStorageIndexAsync_FullStorage_CreatesNothing()
    {
        using var context = this._contextProvider.CreateNewContext();
        await this.SeedAsync(this._accountId, Enumerable.Range(0, StorageCapacity).ToArray());

        var reservation = await this._service.ReserveNextStorageIndexAsync(context, this._accountId);

        Assert.Multiple(() =>
        {
            Assert.That(reservation.StorageItem, Is.Null);
            Assert.That(reservation.StorageIndex, Is.EqualTo(-1));
        });

        var tracked = await context.GetAsync<CashShopStorageItem>();
        Assert.That(tracked.Count(), Is.EqualTo(StorageCapacity), "A full storage must not add a row to the context.");
    }

    /// <summary>
    /// Linhas já persistidas no repositório são levadas em conta pela reserva, para que o índice
    /// liberado por uma remoção possa ser reusado.
    /// </summary>
    [Test]
    public async Task ReserveNextStorageIndexAsync_UsesPersistedRows()
    {
        await this.SeedAsync(this._accountId, 0, 1, 2);
        using var context = this._contextProvider.CreateNewContext();

        var reservation = await this._service.ReserveNextStorageIndexAsync(context, this._accountId);

        Assert.That(reservation.StorageIndex, Is.EqualTo(3));
    }

    /// <summary>
    /// A contagem e a pagina refletem o storage da conta, ordenadas por indice, e ignoram as
    /// linhas de outras contas.
    /// </summary>
    [Test]
    public async Task GetPageAsync_ReturnsTheAccountRowsOrderedByIndex()
    {
        await this.SeedAsync(this._accountId, 2, 0, 1);
        await this.SeedAsync(this._otherAccountId, 5);

        var page = await this._service.GetPageAsync(this._accountId, 0, 30);
        var count = await this._service.GetItemCountAsync(this._accountId);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(3));
            Assert.That(page.Select(item => item.StorageIndex), Is.EqualTo(new[] { 0, 1, 2 }));
        });
    }

    /// <summary>
    /// Uma pagina que comeca alem do fim devolve vazio, e nao uma excecao - o cliente pede a
    /// ultima pagina mesmo quando o storage encolheu desde a ultima listagem.
    /// </summary>
    [Test]
    public async Task GetPageAsync_OutOfRange_ReturnsEmpty()
    {
        await this.SeedAsync(this._accountId, 0);

        Assert.That(await this._service.GetPageAsync(this._accountId, 10, 30), Is.Empty);
    }

    /// <summary>
    /// Cria linhas de storage ja persistidas, para simular o estado que o servico encontra ao
    /// comprar.
    private async Task SeedAsync(Guid accountId, params (int Index, bool IsGift)[] rows)
    {
        using var context = this._contextProvider.CreateNewContext();
        foreach (var row in rows)
        {
            var storageItem = context.CreateNew<CashShopStorageItem>();
            storageItem.AccountId = accountId;
            storageItem.StorageIndex = row.Index;
            storageItem.IsGift = row.IsGift;
        }

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Cria linhas de storage de compras (IsGift = false) para os testes de listagem.
    /// </summary>
    private Task SeedAsync(Guid accountId, params int[] storageIndices) =>
        this.SeedAsync(accountId, storageIndices.Select(index => (index, false)).ToArray());

    /// <summary>
    /// As duas abas sao separadas: a conta que comprou um item e recebeu um presente tem uma
    /// linha em cada aba, e cada listagem devolve apenas a sua.
    /// </summary>
    [Test]
    public async Task GiftAndPurchaseRows_AreListedInSeparateTabs()
    {
        var accountId = Guid.NewGuid();
        await this.SeedAsync(accountId, new[] { (Index: 0, IsGift: false), (Index: 0, IsGift: true) });

        var purchases = await this._service.GetPageAsync(accountId, 0, 30).ConfigureAwait(false);
        var gifts = await this._service.GetGiftPageAsync(accountId, 0, 30).ConfigureAwait(false);
        var purchaseCount = await this._service.GetItemCountAsync(accountId).ConfigureAwait(false);
        var giftCount = await this._service.GetGiftCountAsync(accountId).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(purchaseCount, Is.EqualTo(1), "The S tab counts only purchases.");
            Assert.That(giftCount, Is.EqualTo(1), "The G tab counts only gifts.");
            Assert.That(purchases, Has.Count.EqualTo(1));
            Assert.That(purchases[0].IsGift, Is.False);
            Assert.That(gifts, Has.Count.EqualTo(1));
            Assert.That(gifts[0].IsGift, Is.True);
        });
    }

    /// <summary>
    /// As abas tem numeracao independente. A constraint uniques (AccountId, StorageIndex) e
    /// global, entao as duas linhas realmente ocupam slots distintos: foi o que impediu um
    /// presente de colidir com a compra de mesma posicao.
    /// </summary>
    [Test]
    public async Task ReserveNextStorageIndexAsync_TwiceInSameContext_UsesOneSlotPerTab()
    {
        using var context = this._contextProvider.CreateNewContext();
        var accountId = Guid.NewGuid();

        var purchase = await this._service.ReserveNextStorageIndexAsync(context, accountId, forGift: false).ConfigureAwait(false);
        var gift = await this._service.ReserveNextStorageIndexAsync(context, accountId, forGift: true).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(purchase.StorageIndex, Is.EqualTo(0));
            Assert.That(gift.StorageIndex, Is.EqualTo(0), "Each tab numbers from zero independently.");
            Assert.That(purchase.StorageItem!.IsGift, Is.False);
            Assert.That(gift.StorageItem!.IsGift, Is.True);
        });
    }
}
