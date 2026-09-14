// <copyright file="CashShopVersionPlugInConfiguration.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.CashShop;

/// <summary>
/// Configuration for the <see cref="CashShopVersionPlugIn"/>.
/// </summary>
/// <remarks>
/// The client builds the catalog folder names with L"%03d.%04d.%03d" from these values, so the
/// combination must match the folders installed under data\InGameShopScript\ and
/// data\InGameShopBanner\ of the game client (default: 512.2012.076 / 583.2011.001, which are
/// the catalogs shipped with our client).
/// </remarks>
public class CashShopVersionPlugInConfiguration
{
    /// <summary>
    /// Gets or sets the sale zone of the item shop script catalog (D2 0C).
    /// </summary>
    [Display(Name = "Script Zone")]
    public ushort ScriptSaleZone { get; set; } = 512;

    /// <summary>
    /// Gets or sets the year of the item shop script catalog (D2 0C).
    /// </summary>
    [Display(Name = "Script Year")]
    public ushort ScriptYear { get; set; } = 2012;

    /// <summary>
    /// Gets or sets the year identifier of the item shop script catalog (D2 0C).
    /// </summary>
    [Display(Name = "Script Year Identifier")]
    public ushort ScriptYearIdentify { get; set; } = 76;

    /// <summary>
    /// Gets or sets the banner zone of the item shop banner catalog (D2 15).
    /// </summary>
    [Display(Name = "Banner Zone")]
    public ushort BannerZone { get; set; } = 583;

    /// <summary>
    /// Gets or sets the year of the item shop banner catalog (D2 15).
    /// </summary>
    [Display(Name = "Banner Year")]
    public ushort BannerYear { get; set; } = 2011;

    /// <summary>
    /// Gets or sets the year identifier of the item shop banner catalog (D2 15).
    /// </summary>
    [Display(Name = "Banner Year Identifier")]
    public ushort BannerYearIdentify { get; set; } = 1;
}
