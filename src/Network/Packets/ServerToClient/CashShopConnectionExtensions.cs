// <copyright file="CashShopConnectionExtensions.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Network.Packets.ServerToClient;

using System.Buffers.Binary;
using MUnique.OpenMU.Network;

/// <summary>
/// Server-to-client packets of the cash shop (packet type 0xD2).
/// </summary>
/// <remarks>
/// Note: these packets were not part of the upstream packet definition files, so they are
/// written here by hand. The layouts follow the client (MuMain, sven-n) struct definitions
/// in WSclient.h (all inside a #pragma pack(push, 1) region, so fields are byte-aligned):
/// <list type="table">
///   <item><term>D2 01</term><description>45 bytes: [4]=byte ViewType; [5..45)=5 doubles LE (TotalCash, CashCredit, CashPrepaid, TotalPoint, TotalMileage). The whole cashshop struct block is inside #pragma pack(push,1) in the client (WSclient.h), so the doubles are NOT 8-byte aligned.</description></item>
///   <item><term>D2 02</term><description>5 bytes: [4]=byte ShopOpenResult (0 = failure; the client auto-requests 01 and 05 after a non-zero result).</description></item>
///   <item><term>D2 06</term><description>12 bytes: off 4..12 = 4 WORDs (TotalItemCount, CurrentItemCount, PageIndex, TotalPage).</description></item>
///   <item><term>D2 03</term><description>9 bytes: off 4 = BYTE byResultCode; off 5..9 = long lItemLeftCount. The client reads only the result code (ReceiveIGS_BuyItem).</description></item>
///   <item><term>D2 0D</term><description>33 bytes: off 4..24 = 5 longs (lStorageIndex, lItemSeq, lStorageGroupCode, lProductSeq, lPriceSeq); off 24..32 = double dCashPoint; off 32 = char chItemType. One packet per item of the requested page.</description></item>
///   <item><term>D2 0C</term><description>10 bytes: off 4..10 = 3 WORDs (SaleZone, Year, YearIdentify). Unlocks the shop UI (ShopOpenUnLock) when KJH_MOD_SHOP_SCRIPT_DOWNLOAD is defined.</description></item>
///   <item><term>D2 13</term><description>6 bytes: off 4 = WORD EventItemListCount.</description></item>
///   <item><term>D2 15</term><description>10 bytes: same layout as 0C (banner version).</description></item>
/// </list>
/// The client builds the script folder name with L"%03d.%04d.%03d" (ListManager.cpp), so the
/// version values must be chosen so that the resulting folder matches the installed catalog
/// (e.g. zone=512, year=2012, yearIdentify=76 -> "data\InGameShopScript\512.2012.076\").
/// </remarks>
public static class CashShopConnectionExtensions
{
    private const byte PacketCode = 0xD2;

    /// <summary>
    /// Sends the cash shop point info (C1 D2 01) to the client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="viewType">The view type (0 = normal).</param>
    /// <param name="totalCash">The total cash.</param>
    /// <param name="cashCredit">The cash credit (WCoinC).</param>
    /// <param name="cashPrepaid">The cash prepaid (WCoinP) — shown at the shop top bar.</param>
    /// <param name="totalPoint">The total point.</param>
    /// <param name="totalMileage">The total mileage.</param>
    public static async ValueTask SendCashShopPointInfoAsync(this IConnection? connection, byte viewType, double totalCash, double cashCredit, double cashPrepaid, double totalPoint, double totalMileage)
    {
        if (connection is null)
        {
            return;
        }

        int WritePacket()
        {
            const byte length = 45;
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = length;
            span[2] = PacketCode;
            span[3] = 0x01;
            span[4] = viewType;
            WriteDouble(span[5..13], totalCash);
            WriteDouble(span[13..21], cashCredit);
            WriteDouble(span[21..29], cashPrepaid);
            WriteDouble(span[29..37], totalPoint);
            WriteDouble(span[37..45], totalMileage);
            return length;
        }

        await connection.SendAsync(WritePacket).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the cash shop open state result (C1 D2 02) to the client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="success">If set to <c>true</c>, the shop may open; the client then requests
    /// the point info (D2 01) and the storage list (D2 05) on its own.</param>
    public static async ValueTask SendCashShopOpenResultAsync(this IConnection? connection, bool success)
    {
        if (connection is null)
        {
            return;
        }

        int WritePacket()
        {
            const byte length = 5;
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = length;
            span[2] = PacketCode;
            span[3] = 0x02;
            span[4] = success ? (byte)1 : (byte)0;
            return length;
        }

        await connection.SendAsync(WritePacket).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the cash shop storage item count (C1 D2 06) to the client, which initializes the
    /// storage page list. A page list with zero pages is a valid empty storage.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="totalItemCount">The total item count.</param>
    /// <param name="currentPageItemCount">The item count of the current page.</param>
    /// <param name="pageIndex">The zero-based index of the current page.</param>
    /// <param name="totalPage">The total page count.</param>
    public static async ValueTask SendCashShopStorageCountAsync(this IConnection? connection, ushort totalItemCount, ushort currentPageItemCount, ushort pageIndex, ushort totalPage)
    {
        if (connection is null)
        {
            return;
        }

        int WritePacket()
        {
            const byte length = 12;
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = length;
            span[2] = PacketCode;
            span[3] = 0x06;
            WriteWord(span[4..6], totalItemCount);
            WriteWord(span[6..8], currentPageItemCount);
            WriteWord(span[8..10], pageIndex);
            WriteWord(span[10..12], totalPage);
            return length;
        }

        await connection.SendAsync(WritePacket).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends one cash shop storage item (C1 D2 0D) to the client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="storageIndex">The index of the line in the storage (lStorageIndex).</param>
    /// <param name="itemSeq">The item sequence within the storage (lItemSeq).</param>
    /// <param name="storageGroupCode">The storage group code (lStorageGroupCode).</param>
    /// <param name="productSeq">The product sequence (lProductSeq).</param>
    /// <param name="priceSeq">The price sequence (lPriceSeq); zero resolves by product only.</param>
    /// <param name="cashPoint">The paid cash points (dCashPoint).</param>
    /// <param name="itemType">The item type (chItemType): 'P' for a product, 'C' for currency.</param>
    /// <remarks>
    /// The client sends D2 0D once per item of the requested page and stops reading after
    /// <c>wCurrentItemCount</c> lines (D2 06), see <c>CNewUIInGameShop::AddStorageItem</c>.
    /// The 33-byte layout mirrors <c>PMSG_CASHSHOP_STORAGELIST</c> in the client
    /// (<c>src\source\Network\Server\WSclient.h</c>), inside the <c>#pragma pack(push, 1)</c>
    /// region, so the double is not 8-byte aligned.
    /// <para>
    /// <paramref name="productSeq"/> and <paramref name="priceSeq"/> are mandatory: the client
    /// resolves name, quantity, period and item code from its <b>local</b> catalog through
    /// <c>GetProductInfoFromPriceSeq</c>. <paramref name="itemType"/> must be 'P' or 'C',
    /// otherwise the client silently drops the line.
    /// </para>
    /// </remarks>
    public static async ValueTask SendCashShopStorageItemAsync(
        this IConnection? connection,
        int storageIndex,
        int itemSeq,
        int storageGroupCode,
        int productSeq,
        int priceSeq,
        double cashPoint,
        char itemType)
    {
        if (connection is null)
        {
            return;
        }

        int WritePacket()
        {
            const byte length = 33;
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = length;
            span[2] = PacketCode;
            span[3] = 0x0D;
            BinaryPrimitives.WriteInt32LittleEndian(span[4..8], storageIndex);
            BinaryPrimitives.WriteInt32LittleEndian(span[8..12], itemSeq);
            BinaryPrimitives.WriteInt32LittleEndian(span[12..16], storageGroupCode);
            BinaryPrimitives.WriteInt32LittleEndian(span[16..20], productSeq);
            BinaryPrimitives.WriteInt32LittleEndian(span[20..24], priceSeq);
            WriteDouble(span[24..32], cashPoint);
            span[32] = (byte)itemType;
            return length;
        }

        await connection.SendAsync(WritePacket).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the cash shop script version (C1 D2 0C) to the client. With the client-side
    /// KJH_MOD_SHOP_SCRIPT_DOWNLOAD define, this is the only message which unlocks the shop
    /// (SetScriptVersion + ShopOpenUnLock). The client then loads
    /// data\InGameShopScript\{Zone:000}.{Year:0000}.{YearIdentify:000}\ locally.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="saleZone">The sale zone (e.g. 512).</param>
    /// <param name="year">The catalog year (e.g. 2012).</param>
    /// <param name="yearIdentify">The catalog year identifier (e.g. 76).</param>
    public static async ValueTask SendCashShopScriptVersionAsync(this IConnection? connection, ushort saleZone, ushort year, ushort yearIdentify)
    {
        if (connection is null)
        {
            return;
        }

        int WritePacket()
        {
            const byte length = 10;
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = length;
            span[2] = PacketCode;
            span[3] = 0x0C;
            WriteWord(span[4..6], saleZone);
            WriteWord(span[6..8], year);
            WriteWord(span[8..10], yearIdentify);
            return length;
        }

        await connection.SendAsync(WritePacket).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the cash shop event item count (C1 D2 13) to the client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="itemCount">The number of event items. 0 shows an empty event list.</param>
    public static async ValueTask SendCashShopEventItemCountAsync(this IConnection? connection, ushort itemCount)
    {
        if (connection is null)
        {
            return;
        }

        int WritePacket()
        {
            const byte length = 6;
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = length;
            span[2] = PacketCode;
            span[3] = 0x13;
            WriteWord(span[4..6], itemCount);
            return length;
        }

        await connection.SendAsync(WritePacket).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the cash shop item purchase result (C1 D2 03) to the client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="resultCode">The result code; zero indicates success.</param>
    /// <param name="itemLeftCount">The remaining item count.</param>
    public static async ValueTask SendCashShopBuyResultAsync(this IConnection? connection, byte resultCode, int itemLeftCount)
    {
        if (connection is null)
        {
            return;
        }

        int WritePacket()
        {
            const byte length = 9;
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = length;
            span[2] = PacketCode;
            span[3] = 0x03;
            span[4] = resultCode;
            BinaryPrimitives.WriteInt32LittleEndian(span[5..9], itemLeftCount);
            return length;
        }

        await connection.SendAsync(WritePacket).ConfigureAwait(false);
    }


    /// <summary>
    /// Sends the cash shop banner version (C1 D2 15) to the client. Layout is identical to the
    /// script version (0C); with KJH_MOD_SHOP_SCRIPT_DOWNLOAD it just records the version.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="bannerZone">The banner zone (e.g. 583).</param>
    /// <param name="year">The banner year (e.g. 2011).</param>
    /// <param name="yearIdentify">The banner year identifier (e.g. 1).</param>
    public static async ValueTask SendCashShopBannerVersionAsync(this IConnection? connection, ushort bannerZone, ushort year, ushort yearIdentify)
    {
        if (connection is null)
        {
            return;
        }

        int WritePacket()
        {
            const byte length = 10;
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = length;
            span[2] = PacketCode;
            span[3] = 0x15;
            WriteWord(span[4..6], bannerZone);
            WriteWord(span[6..8], year);
            WriteWord(span[8..10], yearIdentify);
            return length;
        }

        await connection.SendAsync(WritePacket).ConfigureAwait(false);
    }

    private static void WriteWord(Span<byte> target, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(target, value);
    }

    private static void WriteDouble(Span<byte> target, double value)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(target, value);
    }
}
