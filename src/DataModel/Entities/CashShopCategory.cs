// <copyright file="CashShopCategory.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Categoria da Cash Shop, conforme arquivo IBSCategory.txt.
/// Estrutura hierárquica: categorias raiz (IsRoot=true) e subcategorias (com ParentCategory).
/// </summary>
[AggregateRoot]
public class CashShopCategory
{
    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the ProductDisplaySeq (ID do protocolo IBS).
    /// </summary>
    /// <remarks>
    /// Este é o identificador usado no protocolo do cliente e no arquivo IBSCategory.txt.
    /// Deve ser UNIQUE na tabela.
    /// </remarks>
    public int ProductDisplaySeq { get; set; }

    /// <summary>
    /// Gets or sets the category name.
    /// </summary>
    [Required]
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event flag.
    /// </summary>
    public int EventFlag { get; set; }

    /// <summary>
    /// Gets or sets the open flag.
    /// </summary>
    public int OpenFlag { get; set; }

    /// <summary>
    /// Gets or sets the parent ProductDisplaySeq (self-reference).
    /// </summary>
    /// <remarks>
    /// Para categorias raiz, este valor aponta para si mesmo (ex: 10→10).
    /// Para subcategorias, aponta para a categoria pai.
    /// </remarks>
    public int ParentProductDisplaySeq { get; set; }

    /// <summary>
    /// Gets or sets the display order.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a root category.
    /// </summary>
    /// <remarks>
    /// true = categoria raiz (Root=1 no IBS), false = subcategoria (Root=0).
    /// </remarks>
    public bool IsRoot { get; set; }

    /// <summary>
    /// Gets or sets the parent category (self-referencing FK).
    /// </summary>
    public virtual CashShopCategory? ParentCategory { get; set; }

    /// <summary>
    /// Gets or sets the subcategories.
    /// </summary>
    [MemberOfAggregate]
    public virtual ICollection<CashShopCategory> SubCategories { get; protected set; } = null!;

    /// <summary>
    /// Gets or sets the packages in this category.
    /// </summary>
    [MemberOfAggregate]
    public virtual ICollection<CashShopPackage> Packages { get; protected set; } = null!;

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{this.ProductDisplaySeq} - {this.CategoryName}";
    }
}
