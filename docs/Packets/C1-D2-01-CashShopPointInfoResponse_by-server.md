# C1 D2 01 - CashShopPointInfoResponse (by server)

## Is sent when

The client requested the point info (C1 D2 01), which happens automatically after a successful shop open result (C1 D2 02) — or proactively when the player enters the world.

## Causes the following actions on the client side

The cash shop UI displays the balances (total cash, credit, prepaid, point and mileage).

## Structure

| Index | Length | Data Type | Value | Description |
|-------|--------|-----------|-------|-------------|
| 0 | 1 |   Byte   | 0xC1  | [Packet type](PacketTypes.md) |
| 1 | 1 |    Byte   |   45   | Packet header - length of the packet |
| 2 | 1 |    Byte   | 0xD2  | Packet header - packet type identifier |
| 3 | 1 |    Byte   | 0x01  | Packet header - sub packet type identifier |
| 4 | 1 | Byte |  | ViewType |
| 5 | 8 | Double |  | TotalCash |
| 13 | 8 | Double |  | CashCredit (WCoinC) |
| 21 | 8 | Double |  | CashPrepaid (WCoinP — the account WCoin wallet balance) |
| 29 | 8 | Double |  | TotalPoint |
| 37 | 8 | Double |  | TotalMileage |

## Remarks

The struct lives inside a #pragma pack(push, 1) region of the client (PMSG_CASHSHOP_CASHPOINT_ANS
in WSclient.h), so the doubles are written at unaligned byte offsets. Implemented by
`CashShopConnectionExtensions.SendCashShopPointInfoAsync` (hand-written, not XML-generated).
