// <copyright file="WCoinWallet.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Carteira de WCOIN de uma conta.
/// </summary>
/// <remarks>
/// Não é referenciada como <see cref="MemberOfAggregateAttribute"/> da <see cref="Account"/>
/// de propósito: assim não é preciso alterar o arquivo Account.cs (que é do projeto
/// upstream) para acrescentar essa funcionalidade — apenas este arquivo novo, mais o
/// WCoinTransaction.cs e as duas linhas de registro no EntityDataContext (ver notas
/// de integração). O <see cref="Id"/> é propositalmente o mesmo Id da Account: dá uma
/// relação 1-para-1 sem precisar de uma coluna de chave estrangeira a mais, e a busca
/// por carteira de uma conta vira só "GetByIdAsync&lt;WCoinWallet&gt;(account.Id)".
/// </remarks>
[AggregateRoot]
public class WCoinWallet
{
    /// <summary>
    /// Inicializa uma nova instância de <see cref="WCoinWallet"/>.
    /// </summary>
    public WCoinWallet()
    {
    }

    /// <summary>
    /// Inicializa uma nova instância de <see cref="WCoinWallet"/> com o Id informado.
    /// </summary>
    /// <param name="id">O id, que deve ser o mesmo Id da <see cref="Account"/> dona da carteira.</param>
    public WCoinWallet(Guid id)
    {
        this.Id = id;
    }

    /// <summary>
    /// Gets or sets the identifier. Deve ser sempre o mesmo Id da <see cref="Account"/>
    /// à qual esta carteira pertence (relação 1-para-1 por chave compartilhada).
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the saldo atual, em unidades inteiras de WCOIN (sem casas decimais).
    /// </summary>
    /// <remarks>
    /// Nunca use float/double aqui — erro de arredondamento em dinheiro/moeda vira saldo
    /// errado com o tempo. Se um dia precisar de frações, troque para um tipo decimal
    /// (nunca float/double) e ajuste a migration.
    /// </remarks>
    public long Balance { get; set; }

    /// <summary>
    /// Gets or sets a data/hora da última alteração de saldo.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
