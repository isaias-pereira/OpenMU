# C1 D2 13 - CashShopEventItemCountResponse (by server)

## Is sent when

The client requested the event item list of a category (C1 D2 13).

## Causes the following actions on the client side

`CInGameShopSystem::InitEventPackage` prepares the event package list with the given count. The client would then request/receive the item details (D2 14) — a count of 0 leaves the event list empty.

## Structure

| Index | Length | Data Type | Value | Description |
|-------|--------|-----------|-------|-------------|
| 0 | 1 |   Byte   | 0xC1  | [Packet type](PacketTypes.md) |
| 1 | 1 |    Byte   |   6   | Packet header - length of the packet |
| 2 | 1 |    Byte   | 0xD2  | Packet header - packet type identifier |
| 3 | 1 |    Byte   | 0x13  | Packet header - sub packet type identifier |
| 4 | 2 | ShortLittleEndian |  | EventItemListCount |

## Remarks

Implemented by `CashShopConnectionExtensions.SendCashShopEventItemCountAsync`. The event shop is
not implemented yet (FASE 10), so the server answers with 0 items.
