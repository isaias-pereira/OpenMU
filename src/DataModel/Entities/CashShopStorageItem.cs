// <copyright file="CashShopStorageItem.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

using System;

/// <summary>
/// Um item comprado na Cash Shop que está guardado no storage da conta
/// (o "Cash Shop Item Storage" do cliente, aba <c>S</c> / <c>safekeeping</c>).
/// </summary>
/// <remarks>
/// <para>
/// O cliente <b>não</b> recebe o item do inventário: depois de uma compra bem-sucedida
/// (<c>ReceiveIGS_BuyItem</c>, caso 0) ele reenvia <c>D2 05</c> e espera ver o item em
/// <c>D2 0D</c>. Esta entidade é a linha que o servidor envia nessa listagem.
/// </para>
/// <para>
/// Os campos espelham 1:1 a struct <c>PMSG_CASHSHOP_STORAGELIST</c> do cliente
/// (<c>src\source\Network\Server\WSclient.h</c>), verificada em MuMain:
/// <c>lStorageIndex, lItemSeq, lStorageGroupCode, lProductSeq, lPriceSeq, dCashPoint, chItemType</c>.
/// </para>
/// <para>
/// <see cref="ProductSeq"/> e <see cref="PriceSeq"/> são obrigatórios porque o cliente
/// resolve nome, quantidade, período e o item code <b>no catálogo local</b>
/// (<c>GetProductInfoFromPriceSeq</c> em <c>NewUIInGameShop.cpp</c>), e não a partir do
/// servidor. Por isso eles precisam ser devolvidos exatamente como o cliente os espera.
/// </para>
/// <para>
/// O <see cref="Item"/> é <see cref="MemberOfAggregateAttribute">membro do agregado</see>
/// para ser gravado junto com esta linha, sem depender de um
/// <see cref="ItemStorage"/> de personagem — o storage da Cash Shop pertence à conta.
/// </para>
/// </remarks>
[AggregateRoot]
public class CashShopStorageItem
{
    /// <summary>
    /// Inicializa uma nova instância de <see cref="CashShopStorageItem"/>.
    /// </summary>
    public CashShopStorageItem()
    {
    }

    /// <summary>
    /// Inicializa uma nova instância de <see cref="CashShopStorageItem"/> com o Id informado.
    /// </summary>
    /// <param name="id">O identificador.</param>
    public CashShopStorageItem(Guid id)
    {
        this.Id = id;
    }

    /// <summary>
    /// Gets or sets o identificador.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets o Id da <see cref="Account"/> dona deste item.
    /// </summary>
    /// <remarks>
    /// É a mesma relação 1-para-muitos usada por <see cref="WCoinTransaction"/>:
    /// o storage é da conta, não do personagem, para que o item sobreviva à troca
    /// de personagem — igual à carteira de WCOIN que é debitada na mesma compra.
    /// </remarks>
    public Guid AccountId { get; set; }

    /// <summary>
    /// Gets or sets o índice da linha no storage (<c>lStorageIndex</c>).
    /// É o identificador que o cliente devolve em <c>D2 0A</c>/<c>D2 0B</c> para
    /// apanhar ou usar o item.
    /// </summary>
    public int StorageIndex { get; set; }

    /// <summary>
    /// Gets or sets a sequência do item dentro do storage (<c>lItemSeq</c>).
    /// </summary>
    public int ItemSeq { get; set; }

    /// <summary>
    /// Gets or sets o grupo do storage (<c>lStorageGroupCode</c>), copiado de
    /// <see cref="CashShopProduct.StorageGroup"/>.
    /// </summary>
    public int StorageGroup { get; set; }

    /// <summary>
    /// Gets or sets o <c>ProductSeq</c> do produto comprado (<c>lProductSeq</c>).
    /// </summary>
    public int ProductSeq { get; set; }

    /// <summary>
    /// Gets or sets o <c>PriceSeq</c> da variação comprada (<c>lPriceSeq</c>).
    /// Quando zero, o cliente resolve o produto apenas pelo <see cref="ProductSeq"/>.
    /// </summary>
    public int PriceSeq { get; set; }

    /// <summary>
    /// Gets or sets o valor pago (<c>dCashPoint</c>).
    /// </summary>
    public double CashPoint { get; set; }

    /// <summary>
    /// Gets or sets o tipo do item no storage (<c>chItemType</c>).
    /// </summary>
    /// <remarks>
    /// O cliente só aceita <c>'P'</c>/<c>'p'</c> (produto) ou <c>'C'</c>/<c>'c'</c> (moeda):
    /// qualquer outro valor faz <c>AddStorageItem</c> retornar sem adicionar a linha.
    /// Ver <c>NewUIInGameShop.cpp</c>, <c>CNewUIInGameShop::AddStorageItem</c>.
    /// </remarks>
    public string ItemType { get; set; } = CashShopStorageItemType.Product;

    /// <summary>
    /// Gets or sets the item effectively delivered to the storage.
    /// </summary>
    [MemberOfAggregate]
    public virtual Item? Item { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this row is a <b>gift</b> received from another
    /// player, instead of an item the owning account bought for itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client shows two storage tabs and picks them with the <c>InventoryType</c> byte of the
    /// D2 05 request: <c>'S'</c> (safekeeping) for purchases and <c>'G'</c> (gift box) for received
    /// gifts. The server filters the rows by this flag, so the tab the client asks for and the
    /// rows it is given always agree.
    /// </para>
    /// <para>
    /// This flag is <b>not</b> <see cref="ItemType"/>: <c>chItemType</c> in the D2 0D response only
    /// accepts <c>'P'</c> (product) or <c>'C'</c> (currency), and <c>AddStorageItem</c> drops the
    /// line for any other value. A gift is still a product, so it keeps <c>'P'</c> here and is
    /// separated from a purchase by this flag instead.
    /// </para>
    /// </remarks>
    public bool IsGift { get; set; }

    /// <summary>
    /// Gets or sets a data/hora em que o item foi comprado.
    /// </summary>
    public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets o nome do <b>personagem</b> remetente de um presente
    /// (aba de presentes do cliente, campo <c>chSendUserName</c> do D2 0E).
    /// </summary>
    /// <remarks>
    /// É derivado sempre da sessão autenticada (<c>player.SelectedCharacter.Name</c>) —
    /// nunca de um dado enviado pelo cliente, que não carrega campo de remetente no D2 04.
    /// <c>null</c> em presentes antigos ou em compras: o cliente então exibe o remetente
    /// como vazio (<c>AddStorageItem</c> trata <c>NULL</c> como string vazia).
    /// </remarks>
    public string? GiftSender { get; set; }

    /// <summary>
    /// Gets or sets a mensagem enviada junto com um presente
    /// (campo <c>chMessage</c> do D2 0E).
    /// </summary>
    /// <remarks>
    /// No fio o campo comporta até 199 bytes UTF-8 úteis + NUL (o cliente converte
    /// até 200 bytes e exige um NUL interno para o <c>wcscpy</c>). A normalização do
    /// serviço grava no máximo 199 bytes codificados — o mesmo limite do fio — para que
    /// o texto persistido seja exatamente o que o destinatário verá; o limite desta
    /// coluna (200 caracteres) nunca é atingido, pois chars ≤ bytes em UTF-8.
    /// <c>null</c> para presentes antigos, compras e mensagens vazias.
    /// </remarks>
    public string? GiftMessage { get; set; }
}

/// <summary>
/// Os valores de <c>chItemType</c> que o cliente reconhece no storage da Cash Shop.
/// </summary>
/// <remarks>
/// Confirmado em MuMain, <c>CNewUIInGameShop::AddStorageItem</c>
/// (<c>src\source\GameShop\NewUIInGameShop.cpp</c>): a linha só é adicionada se
/// <c>chItemType</c> for <c>'C'/'c'</c> (moeda) ou <c>'P'/'p'</c> (produto);
/// nos demais casos a função retorna e o item simplesmente não aparece.
/// </remarks>
public static class CashShopStorageItemType
{
    /// <summary>
    /// Produto comprado com WCOIN (o caso implementado pela compra <c>D2 03</c>).
    /// </summary>
    public const string Product = "P";

    /// <summary>
    /// WCOIN creditado como item de storage. Não implementado nesta fase.
    /// </summary>
    public const string Currency = "C";
}
