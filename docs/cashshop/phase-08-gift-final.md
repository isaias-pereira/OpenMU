# Fase 8 — Gift/Presente

**Data do registro:** 2026-10-07
**Escopo:** fechamento documental oficial da Fase 8 (Gift D2 04) e do baseline aprovado da resolução de propriedades IBS.
**Documentos irmãos:** `docs/plans/CashShop_Status_Pre_Fase_7.md` (estado das Fases 0–6), `docs/plans/CashShop_Fase_6_e_7_Validacao_PostgreSQL.md`, `docs/plans/CashShop_Homologacao_Banco_e_Seed.md`, requests em `docs/Packets/`.

## Status

```text
FASE 8 — CONCLUÍDA
```

## Baseline

```text
Branch: checkpoint/cashshop-property-resolution-20261004
HEAD:   7a1c03afb (test(cashshop): add gift flow homologation runner)

Commits:
f0ac9ddcf feat(cashshop): aggregate IBS properties for purchase resolution
8dfee0306 test(cashshop): validate GiftFlag 184 gating and gift delivery scenarios
7a1c03afb test(cashshop): add gift flow homologation runner
```

Tag do baseline: `cashshop-phase8-complete` (local, anotada).

## Testes

```text
CashShop: 113/113
Full suite: 627/627
Failed: 0
Skipped: 0
```

Executados no estado commitado (rodada final). A suíte completa anterior ao fechamento documental era 623 testes no HEAD limpo (`0118930f3`) com 7 falhas — todas as 7 eram testes de caracterização do novo comportamento de agregação, que passam no baseline aprovado.

## Catalog parity

```text
143 grupos
143 resolvidos
0 recusados
```

Validado por `CashShopCatalogParityTests.CatalogParity_AllBuyableGroupsResolve` contra a fixture `CashShopCatalogBuyableGroups.json` (snapshot dos 143 grupos compráveis do IBS 512.2012.084, gerado por `tools/generate-cashshop-fixture.ps1`).

## GiftFlag

```text
0   = não giftable
1   = não giftable
182 = não giftable
184 = giftable
```

Evidência de domínio confirmada no MuMain:

```cpp
GiftFlag == 184
```

habilita o envio como presente (`MsgBoxIGSBuySelectItem.cpp:67` e `MsgBoxIGSBuyPackageItem.cpp:68`: `if (pPackage->GiftFlag == 184) { m_BtnPresent.SetEnable(true); } else { m_BtnPresent.SetEnable(false); }`). A comparação é exata por valor de catálogo, **não** truthiness: 0 e 1 são valores reais do catálogo e não significam giftable. No catálogo real: 135 pacotes com 184 e 77 com 185. Compra de pacote não-giftable continua possível (o gate de GiftFlag restringe apenas o fluxo de gift, `requireGiftable`).

## Homologação PostgreSQL

Banco:

```text
openmu_cashshop_validation
```

Executada pelo runner `tools/cashshop-homologation` (modo `gift`), pelo mesmo caminho de produção de um cliente conectado: catálogo real do banco, resolver real, storage service real, débito WCoin real e unidade de trabalho EF real. Estado reiniciado por run; um player por cenário para que a checagem de saldo leia a wallet como está no banco.

```text
Gift válido:
PASS

Saldo inicial:
100000 WCoin

Preço:
100 WCoin

Saldo final:
99900 WCoin

Débitos:
1

Storage destinatário:
0 -> 1

ProductSeq:
58

PriceSeq:
95
```

Transação registrada: `CashShop Gift: Package 35, Price 95 (Wizard Ring) to Cshomol2` (pacote 35, GiftFlag 184, item 6676 = group 13, number 20 "Wizard's Ring", que existe na configuração com Durability 30).

Cenários negativos:

```text
saldo insuficiente:
PASS — sem débito (InsufficientBalance; saldo 99 inalterado, storage inalterado)

destinatário inválido:
PASS — sem débito e sem storage (SelectionUnavailable; nenhuma linha criada)

pacote não giftable:
PASS — recusado sem débito (o pacote real 34 é W Coin(C) 508, então a recusa vem do
gate de moeda, que roda antes do gate de GiftFlag; o gate de GiftFlag em si é
validado pelos testes unitários 0/1/182/184)

storage cheio:
PASS — NoDeliverySpace sem débito (aba de presentes preenchida com 30; a reserva é
por aba: somente linhas com IsGift=true competem pelo espaço de índices da aba 'G')

rollback:
coberto por testes de Save/Commit/Cancel (SaveThrows/SaveReturnsFalse/CommitFailure/
CancelledCommit/RollbackFailure) + transação única do EF: débito, reserva e entrega
num só SaveChangesAsync
```

Integridade pós-homologação verificada no banco: débito exatamente o preço, sem item duplicado (índice único `(AccountId, IsGift, StorageIndex)`), sem registro órfão (a FK do Item usa Cascade — apagar a linha de storage apaga o item junto; 0 órfãos do item 13/20; o inventário do remetente não cresceu em 2 runs).

## Resolver

O resolver passou a agregar propriedades complementares do IBS em vez de tratar cada linha como variante concorrente:

```text
CatalogParity = 143/143
```

Propriedades relevantes:

```text
Duration:
{2,10,12,28,58}

Quantity:
{7,8,9,11,30,31}
```

PropertySeq complementares não devem ser usadas como identidade da oferta: o cliente percorre todas as linhas de uma sequência de preço em `GetPriceSeqNext` e lê o valor do atributo que pede (`InGameShopSystem.cpp:588-606`), então linhas que divergem em `PropertyName`, `Value`, `UnitName`, `UnitType`, `MustFlag` ou `ViewOrder` são atributos complementares de uma mesma oferta. Conflito real existe somente quando duas propriedades reconhecidas como duração (ou quantidade) produzem valores efetivos divergentes, quando `ProductSeq` diverge, ou quando os campos de consistência (`StorageGroup`, `ShareFlag`, `PropertyType`, `DeleteFlag`, `ProductType`) divergem. `UnitType` não é moeda (386 = segundos, 174 = minutos, 172 = horas, 680 = EA); a moeda vem de `CashType`/`PriceSeq`. A agregação é estática (`AggregateForParity`) para o teste de paridade reproduzir as mesmas regras sem mocks.

Testes obsoletos reescritos semanticamente (premissa do antigo invalidada pela evidência 143/143; nada deletado, sem `[Ignore]`/`[Explicit]`):

- `DivergingMustFlag_IsAcceptedAsComplementary` (era `DivergingMustFlag_IsRejected`);
- `DivergingUnitType_IsAcceptedAsComplementary` (era `DivergingCurrency_IsRejected`);
- `DivergingUnitName_IsAcceptedAsComplementary` (era `DivergingDuration_IsRejected`);
- `RealPackage35PropertySet_ResolvesAndDelivers` (era `RealPackage35PropertySet_IsRejectedAsConflicting`).

Proteções que continuam green: preço divergente, duas durações reais divergentes, duas quantidades reais divergentes, ProductSeq incompatível, item não disponível, `QuantityAboveStackCapacity` e `QuantityAboveOneOfNonStackable` — recusa antes de qualquer débito.

## Quantity

Fluxo da quantidade:

```text
CashShopSelectionResult.Quantity
        ↓
CashShopBuyService
        ↓
Item.Durability
```

para itens stackable (`IsStackable` = sem `ItemSlot` e `Durability > 1`; a `Durability` da definição é a capacidade máxima do stack). Quantidade acima da capacidade do stack, ou acima de 1 em item não-stackable, é recusada antes de qualquer estado ser montado. Nenhuma migration de Quantity foi criada.

## Duration

```text
DurationSeconds:
resolvida/preservada

ExpiresAt:
NÃO implementado

migration:
NÃO

timer/job:
NÃO

remoção automática:
NÃO
```

Expiração fica fora da Fase 8 (projeto separado).

## Item 6702

Pendência:

```text
ItemCode: 6702
Group: 13
Number: 46

ItemDefinition Season 6:
ausente (confirmado em 3 fontes: inicialização SeasonSix — Devil Square = 14/19
"Devil's Invitation"; banco openmu — 0 linhas; banco openmu_cashshop_validation — 0 linhas)

IBS Quantity:
1 (PropertySeq 7, "1", EA)

Comportamento:
CannotBeBought / item not available in game configuration

Débito:
nenhum
```

Classificado como:

```text
PENDÊNCIA NÃO BLOQUEANTE:
compatibilidade IBS x configuração Season 6
```

O fluxo trata com segurança: compra/presente de 6702 é recusado sem débito e sem crash. NÃO adicionar ItemDefinition nesta fase — será tratado futuramente como compatibilidade `IBS x ItemDefinition Season 6`.

## Integridade

```text
D2 inalterado
MuMain inalterado
Main.exe inalterado
IBS inalterado
PostgreSQL sem migration nova
Docker não alterado pela implementação
expiração não implementada
```

## Fontes e limites deste registro

- Código e histórico Git do checkout atual (baseline `7a1c03afb`).
- Resultados reais do runner (`dotnet test` com TRX, 627/627) e do runner de homologação (`tools/cashshop-homologation -- gift` contra `openmu_cashshop_validation`), ambos executados durante o fechamento.
- Evidências de cliente (`MsgBoxIGSBuySelectItem.cpp`, `InGameShopSystem.cpp`) e do catálogo IBS real levantadas nas fases anteriores.
- As pendências listadas (Item 6702, expiração, Fase 9) estão fora do escopo deste fechamento.
