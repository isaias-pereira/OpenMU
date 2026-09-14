// <copyright file="CashShopVersionPlugIn.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.CashShop;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.GameLogic.Views.CashShop;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends the cash shop catalog versions (D2 0C script / D2 15 banner) and the initial point
/// info (D2 01) when the player enters the world.
/// </summary>
/// <remarks>
/// D2 0C is the only message which unlocks the shop UI client-side when the client was built
/// with KJH_MOD_SHOP_SCRIPT_DOWNLOAD (CInGameShopSystem::ReceiveIGS_UpdateScript calls
/// SetScriptVersion + ShopOpenUnLock). Without it the client keeps m_bIsShopOpenLock set and
/// pressing X shows "Cannot open MU Item Shop". The hotkey additionally requires that the
/// catalog folder data\InGameShopScript\{zone}.{year}.{yearId}\ exists on the client machine,
/// so the configured versions must match the installed catalog files.
/// </remarks>
[PlugIn]
[Display(Name = "Cash Shop Version Push", Description = "Envia as versões de script/banner (D2 0C/15) e o ponto inicial (D2 01) ao entrar no mundo, destravando a XShop.")]
[Guid("BC618EAE-3390-4C40-B09A-5610A7226A86")]
public class CashShopVersionPlugIn : IPlayerStateChangedPlugIn, ISupportCustomConfiguration<CashShopVersionPlugInConfiguration>, ISupportDefaultCustomConfiguration
{
    private readonly CashShopPointInfoAction _pointInfoAction = new();

    /// <inheritdoc />
    public CashShopVersionPlugInConfiguration? Configuration { get; set; }

    /// <inheritdoc />
    public object CreateDefaultConfig() => new CashShopVersionPlugInConfiguration();

    /// <inheritdoc />
    public async ValueTask PlayerStateChangedAsync(Player player, State previousState, State currentState)
    {
        if (previousState != PlayerState.CharacterSelection
            || currentState != PlayerState.EnteredWorld
            || player.SelectedCharacter is null)
        {
            return;
        }

        var config = this.Configuration ?? new CashShopVersionPlugInConfiguration();

        // The order mirrors what the client expects: script version unlocks the shop, the
        // banner version is just recorded, and the point info feeds the shop header. The
        // client also (re-)requests both on its own once the dialog opens.
        await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(
            p => p.ShowScriptVersionAsync(config.ScriptSaleZone, config.ScriptYear, config.ScriptYearIdentify)).ConfigureAwait(false);
        await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(
            p => p.ShowBannerVersionAsync(config.BannerZone, config.BannerYear, config.BannerYearIdentify)).ConfigureAwait(false);
        await this._pointInfoAction.SetPointInfoAsync(player).ConfigureAwait(false);
    }
}
