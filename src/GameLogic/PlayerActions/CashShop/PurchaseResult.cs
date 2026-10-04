// <copyright file="PurchaseResult.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

/// <summary>
/// Result of a cash shop purchase attempt, plus the D2 03 result codes the client understands.
/// </summary>
/// <remarks>
/// Este arquivo era <c>ICashShopPurchaseService</c>, o contrato do serviço de scaffold que
/// resolvia o produto apenas por <c>PriceSeq</c> e debitava o jogador sem entregar nada.
/// Esse serviço foi removido: <see cref="ICashShopBuyService"/> o substitui, resolvendo o produto
/// pelas chaves naturais que o próprio pacote declara. Os tipos de resultado foram preservados
/// porque são usados por <see cref="ICashShopBuyService"/>, <see cref="CashShopBuyAction"/> e pela
/// view que envia a resposta ao cliente.
/// </remarks>
public record PurchaseResult
{
    /// <summary>
    /// Gets a value indicating whether the purchase succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the protocol result code. The numeric values are those handled by the client.
    /// </summary>
    public PurchaseErrorCode ErrorCode { get; init; }

    /// <summary>
    /// Gets the error message for logging/debugging.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the item that was delivered (if any).
    /// </summary>
    public string? DeliveredItem { get; init; }

    /// <summary>
    /// Creates a successful purchase result.
    /// </summary>
    public static PurchaseResult Successful(string deliveredItem) => new()
    {
        Success = true,
        DeliveredItem = deliveredItem,
    };

    /// <summary>
    /// Creates a failed purchase result.
    /// </summary>
    public static PurchaseResult Failed(PurchaseErrorCode errorCode, string errorMessage) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage,
    };
}

/// <summary>
/// Error codes for cash shop purchase failures (C1 D2 03 answer, D2 03 S2C).
/// </summary>
/// <remarks>
/// Os valores são determinados por <c>ReceiveIGS_BuyItem</c> em
/// <c>Network/Server/WSclient.cpp</c> e não devem ser alterados sem revalidar o cliente.
/// Mapeamento confirmado no cliente:
/// <list type="bullet">
/// <item><description>0 = compra concluída (<c>PurchaseCompleted</c>).</description></item>
/// <item><description>1 = WCoin/ponto insuficiente (<c>YouDoNotHaveEnoughWCoinOrPoints</c>).</description></item>
/// <item><description>2 = espaço insuficiente no storage (<c>YouDoNotHaveEnoughSpaceInStorage</c>).</description></item>
/// <item><description>3 = item esgotado (<c>ThisItemHasSoldOut</c>).</description></item>
/// <item><description>4 = item não está disponível no momento (<c>ThisItemIsNotCurrentlyAvailable</c>).</description></item>
/// <item><description>5 = item não está mais disponível (<c>ThisItemIsNoLongerAvailable</c>).</description></item>
/// <item><description>6 = item não pode ser comprado (<c>ThisItemCannotBeBought</c>).</description></item>
/// <item><description>7 = item de evento não pode ser comprado (<c>EventItemsCannotBeBought</c>).</description></item>
/// <item><description>8 = limite de compras de item de evento excedido.</description></item>
/// <item><description>9 = tipo de moeda incorreto (<c>YouHaveSelectedAnIncorrectWCoinTypePleaseSelectAgain</c>).</description></item>
/// <item><description>-1 = erro de banco de dados (<c>ADatabaseErrorHasOccurred</c>).</description></item>
/// <item><description>-2 = falha de acesso ao banco de dados (<c>DatabaseAccessFailed</c>).</description></item>
/// </list>
/// </remarks>
public enum PurchaseErrorCode
{
    /// <summary>
    /// Compra concluída com sucesso (não é um erro).
    /// </summary>
    None = 0,

    /// <summary>
    /// Saldo de WCoinP/WCoin insuficiente.
    /// </summary>
    InsufficientBalance = 1,

    /// <summary>
    /// O cliente não recebeu o item porque o storage de entrega está cheio.
    /// </summary>
    NoDeliverySpace = 2,

    /// <summary>
    /// O item esgotou.
    /// </summary>
    SoldOut = 3,

    /// <summary>
    /// O pacote/produto não está disponível no momento (fora de vigência, apagado ou não vendido).
    /// </summary>
    PackageUnavailable = 4,

    /// <summary>
    /// A seleção (pacote, variação ou item code) não existe mais no catálogo.
    /// Também usado para inventário cheio, pois o cliente trata ambos como falha de entrega.
    /// </summary>
    SelectionUnavailable = 5,

    /// <summary>
    /// A seleção não pode ser comprada com segurança (item inexistente na configuração, mileage, etc).
    /// </summary>
    CannotBeBought = 6,

    /// <summary>
    /// O item é um item de evento e não pode ser comprado.
    /// </summary>
    EventItemCannotBeBought = 7,

    /// <summary>
    /// O limite de compras de itens de evento foi excedido.
    /// </summary>
    EventItemPurchaseLimitExceeded = 8,

    /// <summary>
    /// O tipo de moeda enviado pelo cliente não corresponde ao do pacote.
    /// </summary>
    InvalidCoinType = 9,

    /// <summary>
    /// Ocorreu um erro de banco de dados (<c>ADatabaseErrorHasOccurred</c>).
    /// </summary>
    TransactionFailed = -1,

    /// <summary>
    /// Alias de <see cref="TransactionFailed"/>; o cliente trata como erro de banco de dados.
    /// </summary>
    Unknown = -1,

    /// <summary>
    /// Falha de acesso ao banco de dados (<c>DatabaseAccessFailed</c>).
    /// </summary>
    DatabaseAccessFailed = -2,

    /// <summary>
    /// A unidade de trabalho foi desfeita, mas a reversão ficou incompleta e o contexto do
    /// jogador pode continuar carregando estado pendente.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Este código <b>não</b> é enviado ao cliente: não existe um valor correspondente no
    /// <c>ReceiveIGS_BuyItem</c> do cliente, e inventar um faria o cliente interpretá-lo como
    /// outro erro. Ele serve para o servidor distinguir "nada foi gravado, contexto limpo" de
    /// "pode haver débito ou entrega parcial pendente", que é a diferença entre uma falha
    /// recuperável e uma que exige intervenção.
    /// </para>
    /// <para>
    /// Quando o servidor devolve um resultado de compra/presente, ele converte este código no
    /// <see cref="TransactionFailed"/> que o cliente entende, porque o cliente já trata -1 como
    /// erro de banco. A distinção fica no log e nesta API.
    /// </para>
    /// </remarks>
    RollbackIncomplete = -100,
}
