// <copyright file="VaultAccess.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic;

using System;
using System.Linq;
using System.Threading.Tasks;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Centralizes access to the numbered vaults (báus) of an account:
/// how many vaults are allowed based on the VIP tier, and resolving/creating the
/// <see cref="ItemStorage"/> of the vault a player currently has selected.
/// Tier mapping: None=1, Bronze=2, Silver=3, Gold=4, Platinum=5.
/// </summary>
public static class VaultAccess
{
    /// <summary>The number of vaults an account without VIP may use.</summary>
    public const int VaultsWithoutVip = 1;

    /// <summary>The maximum number of vaults reachable by any tier.</summary>
    public const int MaximumVaultCount = 5;

    /// <summary>
    /// Gets the number of vaults the account may access, based on its VIP tier.
    /// </summary>
    /// <param name="context">The persistence context (used when the VIP cache needs to be hydrated).</param>
    /// <param name="accountId">The identifier of the account.</param>
    /// <returns>The accessible vault count (at least 1, at most <see cref="MaximumVaultCount"/>).</returns>
    public static async ValueTask<int> GetMaxVaultsAsync(IContext context, Guid accountId)
    {
        var status = await VipService.GetVipStatusAsync(context, accountId).ConfigureAwait(false);
        if (!status.IsActive)
        {
            return VaultsWithoutVip;
        }

        return status.PlanName?.ToLowerInvariant() switch
        {
            "bronze" => 2,
            "silver" => 3,
            "gold" => 4,
            "platinum" => 5,
            _ => VaultsWithoutVip,
        };
    }

    /// <summary>
    /// Clamps the requested vault number to the effectively granted access.
    /// Values below 1 are raised to 1; values above the tier limit fall back to it,
    /// so an expired VIP can never keep an extra vault open.
    /// </summary>
    /// <param name="requestedNumber">The requested vault number.</param>
    /// <param name="maxVaults">The effective maximum, see <see cref="GetMaxVaultsAsync"/>.</param>
    /// <returns>The vault number to use.</returns>
    public static int ClampVaultNumber(int requestedNumber, int maxVaults) =>
        Math.Clamp(requestedNumber, 1, Math.Max(VaultsWithoutVip, maxVaults));

    /// <summary>
    /// Resolves the <see cref="ItemStorage"/> of the vault which the player currently has selected
    /// (<see cref="Player.CurrentVaultNumber"/>), creating it lazily if it does not exist yet.
    /// The selected number is clamped to the VIP-tier-based access as anti-bypass measure.
    /// </summary>
    /// <param name="player">The player whose vault should be resolved.</param>
    /// <returns>The storage of the selected vault.</returns>
    public static async ValueTask<ItemStorage> GetOrCreateVaultStorageAsync(Player player)
    {
        var account = player.Account!;
        var maxVaults = await GetMaxVaultsAsync(player.PersistenceContext, account.Id).ConfigureAwait(false);
        player.CurrentVaultNumber = ClampVaultNumber(player.CurrentVaultNumber, maxVaults);

        var vault = account.Vaults?.FirstOrDefault(v => v.Number == player.CurrentVaultNumber);
        if (vault is null)
        {
            vault = player.PersistenceContext.CreateNew<AccountVault>();
            vault.Number = player.CurrentVaultNumber;
            vault.Storage = player.PersistenceContext.CreateNew<ItemStorage>();
            account.Vaults!.Add(vault);
        }

        vault.Storage ??= player.PersistenceContext.CreateNew<ItemStorage>();
        return vault.Storage;
    }

    /// <summary>
    /// Creates the runtime <see cref="Storage"/> wrapper for the given vault storage.
    /// The legacy extended-vault flag is honored for vault number 1 only, so existing
    /// extended vaults keep their items; additional vaults always use the standard size.
    /// </summary>
    /// <param name="player">The player who opens the vault (used for size calculation).</param>
    /// <param name="storage">The persistent storage of the vault.</param>
    /// <returns>The runtime storage wrapper.</returns>
    public static Storage CreateRuntimeStorage(Player player, ItemStorage storage)
    {
        var warehouseSize = player.CurrentVaultNumber == 1 && player.Account?.IsVaultExtended is true
            ? InventoryConstants.WarehouseSize * 2
            : InventoryConstants.WarehouseSize;
        return new Storage(warehouseSize, storage);
    }
}
