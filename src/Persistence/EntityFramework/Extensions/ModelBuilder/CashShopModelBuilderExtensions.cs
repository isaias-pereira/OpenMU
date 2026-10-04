// <copyright file="CashShopModelBuilderExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework.Extensions.ModelBuilder;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MUnique.OpenMU.Persistence.EntityFramework.Model;

/// <summary>
/// Extensions for the <see cref="EntityTypeBuilder{CashShopCategory}"/>.
/// </summary>
internal static class CashShopCategoryExtensions
{
    /// <summary>
    /// Applies the settings for the <see cref="CashShopCategory"/> entity.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static void Apply(this EntityTypeBuilder<CashShopCategory> builder)
    {
        builder.Property(c => c.CategoryName).HasMaxLength(256);
        builder.Property(c => c.ProductDisplaySeq).IsRequired();

        builder.HasIndex(c => c.ProductDisplaySeq).IsUnique();

        // Self-referencing FK via generated ParentCategoryId (Guid → Id)
        builder.HasOne(c => c.RawParentCategory)
            .WithMany(c => c.RawSubCategories)
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Extensions for the <see cref="EntityTypeBuilder{CashShopPackage}"/>.
/// </summary>
internal static class CashShopPackageExtensions
{
    /// <summary>
    /// Applies the settings for the <see cref="CashShopPackage"/> entity.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static void Apply(this EntityTypeBuilder<CashShopPackage> builder)
    {
        builder.Property(p => p.PackageProductName).HasMaxLength(256);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.Caution).HasMaxLength(2000);
        builder.Property(p => p.ProductCashName).HasMaxLength(128);
        builder.Property(p => p.PriceUnitName).HasMaxLength(128);
        builder.Property(p => p.InGamePackageID).HasMaxLength(64);

        builder.HasIndex(p => new { p.ProductDisplaySeq, p.PackageProductSeq }).IsUnique();
        builder.HasIndex(p => p.PackageProductSeq);
        builder.HasIndex(p => p.CategoryId);

        builder.HasOne(p => p.RawCategory)
            .WithMany(c => c.RawPackages)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Extensions for the <see cref="EntityTypeBuilder{CashShopProduct}"/>.
/// </summary>
internal static class CashShopProductExtensions
{
    /// <summary>
    /// Applies the settings for the <see cref="CashShopProduct"/> entity.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static void Apply(this EntityTypeBuilder<CashShopProduct> builder)
    {
        builder.Property(p => p.ProductName).HasMaxLength(256);
        builder.Property(p => p.PropertyName).HasMaxLength(128);
        builder.Property(p => p.Value).HasMaxLength(128);
        builder.Property(p => p.UnitName).HasMaxLength(64);
        builder.Property(p => p.InGamePackageID).HasMaxLength(64);

        builder.HasIndex(p => new { p.ProductSeq, p.PriceSeq, p.PropertySeq }).IsUnique();
        builder.HasIndex(p => p.ProductSeq);
        builder.HasIndex(p => p.PriceSeq);
        builder.HasIndex(p => p.InGamePackageID);
    }
}

/// <summary>
/// Extensions for the <see cref="EntityTypeBuilder{CashShopPackageProduct}"/>.
/// </summary>
internal static class CashShopPackageProductExtensions
{
    /// <summary>
    /// Applies the settings for the <see cref="CashShopPackageProduct"/> entity.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static void Apply(this EntityTypeBuilder<CashShopPackageProduct> builder)
    {
        builder.HasKey(pp => new { pp.PackageId, pp.ProductId });

        builder.HasOne(pp => pp.RawPackage)
            .WithMany(p => p.RawPackageProducts)
            .HasForeignKey(pp => pp.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pp => pp.RawProduct)
            .WithMany(p => p.RawPackageProducts)
            .HasForeignKey(pp => pp.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(pp => pp.ProductId);
    }
}
