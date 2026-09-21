// <copyright file="CashShopPackageProduct.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Junction table para relacionamento M:N entre Package e Product.
/// Mantém a ordem de exibição dos produtos dentro do pacote.
/// </summary>
public class CashShopPackageProduct
{
    /// <summary>
    /// Gets or sets the package identifier (FK).
    /// </summary>
    public Guid PackageId { get; set; }

    /// <summary>
    /// Gets or sets the product identifier (FK).
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Gets or sets the display order of this product within the package.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets the package.
    /// </summary>
    [Required]
    public virtual CashShopPackage? Package { get; set; }

    /// <summary>
    /// Gets or sets the product.
    /// </summary>
    [Required]
    public virtual CashShopProduct? Product { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Package {this.PackageId} - Product {this.ProductId} (Order: {this.Order})";
    }
}
