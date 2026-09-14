# C1 D2 15 - CashShopBannerVersionResponse (by server)

## Is sent when

The player enters the game world (proactively, by the `CashShopVersionPlugIn`).

## Causes the following actions on the client side

`ReceiveIGS_UpdateBanner` records the banner version with `SetBannerVersion(zone, year, yearId)`. The client uses it to name the banner folder `data\InGameShopBanner\{zone:000}.{year:0000}.{yearId:000}`. With `KJH_MOD_SHOP_SCRIPT_DOWNLOAD` it does not unlock anything by itself — the shop unlock comes from D2 0C.

## Structure

| Index | Length | Data Type | Value | Description |
|-------|--------|-----------|-------|-------------|
| 0 | 1 |   Byte   | 0xC1  | [Packet type](PacketTypes.md) |
| 1 | 1 |    Byte   |   10   | Packet header - length of the packet |
| 2 | 1 |    Byte   | 0xD2  | Packet header - packet type identifier |
| 3 | 1 |    Byte   | 0x15  | Packet header - sub packet type identifier |
| 4 | 2 | ShortLittleEndian |  | BannerZone |
| 6 | 2 | ShortLittleEndian |  | Year |
| 8 | 2 | ShortLittleEndian |  | YearIdentify |

## Remarks

Implemented by `CashShopConnectionExtensions.SendCashShopBannerVersionAsync`. The client struct is
`PMSG_CASHSHOP_BANNER_UPDATE` (WSclient.h, pack(1)).
