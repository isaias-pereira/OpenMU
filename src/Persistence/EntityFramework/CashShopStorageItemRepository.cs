// <copyright file="CashShopStorageItemRepository.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework;

using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Persistence.EntityFramework.Model;

/// <summary>
/// Repository for the cash shop storage items with a claim-targeted dependent data load.
/// </summary>
/// <remarks>
/// <para>
/// The generic recursive <c>LoadDependentDataAsync</c> of the base class materializes the whole
/// reachable configuration graph of the row's item definition on <b>every</b> load through a full
/// data context — including the documented claim fallback, where it was measured at ~17 s per claim
/// on a local ephemeral database (RC2/GATE A RED, Wizard Ring definition 13/20) and 40–46 s per
/// claim on the RC1 homologation container (rodadas 2S/GATE 7: claims a9a21003, 90798174, e6386271
/// and eefe90cc). The claim needs exactly three things from the fallback context: the row itself
/// (scalar columns arrive with the point query), the row's delivered <see cref="CashShopStorageItem.Item"/>,
/// and the item's definition — whose <b>scalar</b> properties (name, width/height for the inventory
/// space check) arrive with the definition entity itself.
/// </para>
/// <para>
/// <b>Account contexts are kept untouched.</b> They cut <see cref="ItemDefinition"/> from their
/// model, so this override loads nothing there: the row's item navigation stays unresolved exactly
/// as before, and the claim keeps taking its documented fallback path (a definition-less item
/// loaded into the player context would otherwise be refused by the claim).
/// </para>
/// </remarks>
internal class CashShopStorageItemRepository : CachingGenericRepository<CashShopStorageItem>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopStorageItemRepository"/> class.
    /// </summary>
    /// <param name="repositoryProvider">The repository provider.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public CashShopStorageItemRepository(IContextAwareRepositoryProvider repositoryProvider, ILoggerFactory loggerFactory)
        : base(repositoryProvider, loggerFactory)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask LoadDependentDataAsync(object obj, DbContext currentContext, CancellationToken cancellationToken)
    {
        if (obj is not CashShopStorageItem row)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Only a full data context can resolve the item's definition. Account contexts ignore
        // ItemDefinition; loading the item there would hand the claim a definition-less item which
        // it would refuse — so on those contexts nothing is loaded and the behavior is identical
        // to the previous generic implementation observed in production (the navigation stays
        // unresolved and the claim takes the fallback path).
        if (currentContext.Model.FindEntityType(typeof(ItemDefinition)) is null)
        {
            return;
        }

        // The claim-targeted load: the row's item, and the item's definition. Definition scalars
        // (Name, Width, Height) come with the definition entity — no deeper navigation is loaded,
        // so the recursive configuration graph materialization is gone.
        var itemNavigation = currentContext.Entry(row).Metadata.FindNavigation(nameof(CashShopStorageItem.RawItem));
        if (itemNavigation is not null)
        {
            await currentContext.Entry(row).Navigation(itemNavigation).LoadAsync(cancellationToken).ConfigureAwait(false);
            if (row.Item is { } item)
            {
                var definitionNavigation = currentContext.Entry(item).Metadata.FindNavigation(nameof(Model.Item.RawDefinition));
                if (definitionNavigation is not null)
                {
                    await currentContext.Entry(item).Navigation(definitionNavigation).LoadAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
