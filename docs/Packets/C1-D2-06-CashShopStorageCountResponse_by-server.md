# C1 D2 06 - CashShopStorageCountResponse (by server)

## Is sent when

The client requested a storage page (C1 D2 05) — which it does automatically after a successful shop open result — or after the server moved an item into the storage.

## Causes the following actions on the client side

`CNewUIGameShop::InitStorage` initializes the storage page list with the given counts. Without this message the storage UI of the shop stays uninitialized.

## Structure

| Index | Length | Data Type | Value | Description |
|-------|--------|-----------|-------|-------------|
| 0 | 1 |   Byte   | 0xC1  | [Packet type](PacketTypes.md) |
| 1 | 1 |    Byte   |   12   | Packet header - length of the packet |
| 2 | 1 |    Byte   | 0xD2  | Packet header - packet type identifier |
| 3 | 1 |    Byte   | 0x06  | Packet header - sub packet type identifier |
| 4 | 2 | ShortLittleEndian |  | TotalItemCount |
| 6 | 2 | ShortLittleEndian |  | CurrentItemCount |
| 8 | 2 | ShortLittleEndian |  | PageIndex |
| 10 | 2 | ShortLittleEndian |  | TotalPage |

## Remarks

Implemented by `CashShopConnectionExtensions.SendCashShopStorageCountAsync`. The real storage is not
implemented yet (FASE 9), so the server answers an empty storage (0 items, 0 pages).
