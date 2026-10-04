// <copyright file="CashShopPackage.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Pacote da Cash Shop, conforme arquivo IBSPackage.txt.
/// Representa um item comprável que pode conter múltiplos produtos e variações de preço.
/// </summary>
[AggregateRoot]
public class CashShopPackage
{
    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the ProductDisplaySeq (FK para Category).
    /// </summary>
    public int ProductDisplaySeq { get; set; }

    /// <summary>
    /// Gets or sets the view order within the category.
    /// </summary>
    public int ViewOrder { get; set; }

    /// <summary>
    /// Gets or sets the PackageProductSeq (ID do pacote).
    /// </summary>
    /// <remarks>
    /// O valor é único dentro de uma categoria, mas pode ocorrer em categorias diferentes no IBS.
    /// A chave natural do pacote no catálogo é (ProductDisplaySeq, PackageProductSeq).
    /// </remarks>
    public int PackageProductSeq { get; set; }

    /// <summary>
    /// Gets or sets the package name.
    /// </summary>
    [Required]
    public string PackageProductName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the package type.
    /// </summary>
    public int PackageProductType { get; set; }

    /// <summary>
    /// Gets or sets the base price.
    /// </summary>
    public int Price { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the caution text.
    /// </summary>
    public string Caution { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sales flag.
    /// </summary>
    public int SalesFlag { get; set; }

    /// <summary>
    /// Gets or sets the gift flag.
    /// </summary>
    public int GiftFlag { get; set; }

    /// <summary>
    /// Gets or sets the start date.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Gets or sets the end date.
    /// </summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    /// Gets or sets the capsule flag.
    /// </summary>
    public int CapsuleFlag { get; set; }

    /// <summary>
    /// Gets or sets the capsule count.
    /// </summary>
    public int CapsuleCount { get; set; }

    /// <summary>
    /// Gets or sets the product cash name.
    /// </summary>
    public string ProductCashName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the price unit name.
    /// </summary>
    public string PriceUnitName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the delete flag.
    /// </summary>
    public int DeleteFlag { get; set; }

    /// <summary>
    /// Gets or sets the event flag.
    /// </summary>
    public int EventFlag { get; set; }

    /// <summary>
    /// Gets or sets the product amount.
    /// </summary>
    public int ProductAmount { get; set; }

    /// <summary>
    /// Gets or sets the product sequence list (stored as JSON array).
    /// </summary>
    /// <remarks>
    /// Exemplo: [528] ou [100,101,102]
    /// Armazenado como TEXT para PostgreSQL jsonb.
    /// </remarks>
    public string ProductSeqList { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the InGamePackageID.
    /// </summary>
    /// <remarks>
    /// Link futuro com ItemDefinition (FASE 7).
    /// </remarks>
    public string InGamePackageID { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the product cash seq.
    /// </summary>
    public int ProductCashSeq { get; set; }

    /// <summary>
    /// Gets or sets the price count.
    /// </summary>
    public int PriceCount { get; set; }

    /// <summary>
    /// Gets or sets the price sequence list (stored as JSON array).
    /// </summary>
    /// <remarks>
    /// Exemplo: [607,608,609,610] - variações de preço/duração.
    /// Armazenado como TEXT para PostgreSQL jsonb.
    /// </remarks>
    public string PriceSeqList { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to deduct mileage.
    /// </summary>
    public bool DeductMileageFlag { get; set; }

    /// <summary>
    /// Gets or sets the cash type.
    /// </summary>
    public int CashType { get; set; }

    /// <summary>
    /// Gets or sets the cash type flag.
    /// </summary>
    public int CashTypeFlag { get; set; }

    /// <summary>
    /// Gets or sets the category identifier (FK).
    /// </summary>
    public Guid CategoryId { get; set; }

    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    [Required]
    public virtual CashShopCategory? Category { get; set; }

    /// <summary>
    /// Gets or sets the package-product links (M:N relationship).
    /// </summary>
    [MemberOfAggregate]
    public virtual ICollection<CashShopPackageProduct> PackageProducts { get; protected set; } = null!;

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{this.PackageProductSeq} - {this.PackageProductName}";
    }
}
