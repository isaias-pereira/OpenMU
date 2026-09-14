# C1 D2 02 - CashShopOpenResultResponse (by server)

## Is sent when

The client requested to open the cash shop (C1 D2 02 with IsClosed = false).

## Causes the following actions on the client side

If the result is non-zero, the client shows the shop dialog and automatically requests the point info (C1 D2 01) and the storage list (C1 D2 05, page 1).

## Structure

| Index | Length | Data Type | Value | Description |
|-------|--------|-----------|-------|-------------|
| 0 | 1 |   Byte   | 0xC1  | [Packet type](PacketTypes.md) |
| 1 | 1 |    Byte   |   5   | Packet header - length of the packet |
| 2 | 1 |    Byte   | 0xD2  | Packet header - packet type identifier |
| 3 | 1 |    Byte   | 0x02  | Packet header - sub packet type identifier |
| 4 | 1 | Byte |  | ShopOpenResult (0 = failure, the client keeps the dialog closed) |

## Remarks

Implemented by `CashShopConnectionExtensions.SendCashShopOpenResultAsync`. The server answers
success when the player is in a safezone (`Player.IsAtSafezone()`), mirroring the client-side
gate of `CNewUIInGameShop::IsInGameShopOpen` ("You can only open MU Item Shop in a town or safe
zone").
