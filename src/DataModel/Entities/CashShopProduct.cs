// <copyright file="CashShopProduct.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Produto da Cash Shop, conforme arquivo IBSProduct.txt.
/// Representa uma variação de um item (ex: duração, quantidade, propriedades).
/// </summary>
[AggregateRoot]
public class CashShopProduct
{
    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the ProductSeq.
    /// </summary>
    /// <remarks>
    /// NÃO é único - múltiplas linhas compartilham o mesmo ProductSeq (variações).
    /// </remarks>
    public int ProductSeq { get; set; }

    /// <summary>
    /// Gets or sets the product name.
    /// </summary>
    [Required]
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the property name.
    /// </summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the unit name.
    /// </summary>
    public string UnitName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the price.
    /// </summary>
    public int Price { get; set; }

    /// <summary>
    /// Gets or sets the PriceSeq da linha de produto.
    /// </summary>
    /// <remarks>
    /// O IBS reutiliza PriceSeq para diferentes PropertySeq do mesmo ProductSeq.
    /// A chave natural confirmada do produto é (ProductSeq, PriceSeq, PropertySeq).
    /// </remarks>
    public int PriceSeq { get; set; }

    /// <summary>
    /// Gets or sets the property type.
    /// </summary>
    public int PropertyType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a required property.
    /// </summary>
    public bool MustFlag { get; set; }

    /// <summary>
    /// Gets or sets the view order.
    /// </summary>
    public int ViewOrder { get; set; }

    /// <summary>
    /// Gets or sets the delete flag.
    /// </summary>
    public int DeleteFlag { get; set; }

    /// <summary>
    /// Gets or sets the storage group.
    /// </summary>
    public int StorageGroup { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is shareable.
    /// </summary>
    public bool ShareFlag { get; set; }

    /// <summary>
    /// Gets or sets the InGamePackageID.
    /// </summary>
    /// <remarks>
    /// Link futuro com ItemDefinition (FASE 7).
    /// </remarks>
    public string InGamePackageID { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the property seq.
    /// </summary>
    public int PropertySeq { get; set; }

    /// <summary>
    /// Gets or sets the product type.
    /// </summary>
    public int ProductType { get; set; }

    /// <summary>
    /// Gets or sets the unit type.
    /// </summary>
    public int UnitType { get; set; }

    /// <summary>
    /// Gets or sets the package-product links (M:N relationship).
    /// </summary>
    [MemberOfAggregate]
    public virtual ICollection<CashShopPackageProduct> PackageProducts { get; protected set; } = null!;

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{this.PriceSeq} - {this.ProductName}";
    }
}
