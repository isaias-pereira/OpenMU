# C1 D2 0C - CashShopScriptVersionResponse (by server)

## Is sent when

The player enters the game world (proactively, by the `CashShopVersionPlugIn`), before the client would ever be able to open the cash shop.

## Causes the following actions on the client side

With the client built with `KJH_MOD_SHOP_SCRIPT_DOWNLOAD`, `ReceiveIGS_UpdateScript` calls `CInGameShopSystem::SetScriptVersion(zone, year, yearId)` and `ShopOpenUnLock()`. This is the only message which unlocks the XShop UI. The version values must match a folder installed in the client under `data\InGameShopScript\`, named `{zone:000}.{year:0000}.{yearId:000}` (format string from `ListManager.cpp`), e.g. `512.2012.076`.

## Structure

| Index | Length | Data Type | Value | Description |
|-------|--------|-----------|-------|-------------|
| 0 | 1 |   Byte   | 0xC1  | [Packet type](PacketTypes.md) |
| 1 | 1 |    Byte   |   10   | Packet header - length of the packet |
| 2 | 1 |    Byte   | 0xD2  | Packet header - packet type identifier |
| 3 | 1 |    Byte   | 0x0C  | Packet header - sub packet type identifier |
| 4 | 2 | ShortLittleEndian |  | SaleZone |
| 6 | 2 | ShortLittleEndian |  | Year |
| 8 | 2 | ShortLittleEndian |  | YearIdentify |

## Remarks

Implemented by `CashShopConnectionExtensions.SendCashShopScriptVersionAsync`. The client struct is
`PMSG_CASHSHOP_VERSION_UPDATE` (WSclient.h, pack(1)).
