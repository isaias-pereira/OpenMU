# Fase 9 — Cash Shop Storage: Homologação Final

## Status

**FASE 9 — CONCLUÍDA.** O fluxo de storage do Cash Shop (compra → reserva → listagem → resgate)
foi homologado de ponta a ponta contra o banco PostgreSQL real
(`openmu_cashshop_validation`), com 8 cenários automatizados no runner
`tools/cashshop-homologation` — **0 desvios**. A homologação revelou e corrigiu **2 bugs
reais de produção** invisíveis à suíte InMemory (detalhes em "Bugs de produção
descobertos e corrigidos").

## Baseline

- Branch: `checkpoint/cashshop-property-resolution-20261004`.
- Fase 8 (Gift) concluída, homologada e tagueada (`cashshop-phase8-complete`,
  commit `9d6041763`).
- Núcleo da Fase 9 commitado antes desta rodada: `bd0ce53d9` (quantity no gift),
  `af99453a3` (claim service), `c3f7b53f5` (testes de claim/list).
- Suíte completa pré-homologação: 640/640 GREEN.
- Banco de homologação: `openmu_cashshop_validation` (schemas `config`/`data`), contas
  `cshomolo`/`cshomol2`, personagens `Cshomolo`/`Cshomol2`, carteiras resetadas para
  100000 WCoin a cada run.

## Protocolo (resumo confirmado)

| Sub-código | Sentido | Conteúdo | Estado |
|---|---|---|---|
| D2 05 | cliente → servidor | Requisição das abas de storage | Implementado |
| D2 06 | servidor → cliente | Abas de storage (contadores) | Implementado |
| D2 0D | servidor → cliente | Uma página de linhas do storage | Implementado |
| D2 0B | cliente → servidor | `STORAGE_ITEM_USE_REQ` (15 bytes: StorageSeq @4, StorageItemSeq @8, ItemCode u16 @12, StorageType @14) | Implementado |
| D2 0B resposta | servidor → cliente | 5 bytes `byResult` (0 = ok + o cliente reenvia D2 05; 21 = inventário cheio; 22 = não pode usar; 1 = storage inexistente; -1/-2 em complemento de dois = 255/254) | Implementado |
| D2 0A | cliente → servidor | Throw (jogar item do storage) | **Código morto no cliente — não implementado (pendência)** |
| D2 0E | servidor → cliente | Linha da caixa de presentes com remetente/mensagem (244 bytes: base do D2 0D + `chSendUserName[11]` @33 + `chMessage[200]` @44, UTF-8 com NUL interno) | **Fase 10 (Rodada 2B): implementado. Correção factual de direção: o cliente trata 0x0E no dispatcher de recebimento (`WSclient.cpp:14480`, `ReceiveIGS_StorageGiftItemList`) e nunca envia 0x0E — a direção "cliente → servidor" registrada acima estava errada** |

Nomes enganosos dos structs OpenMU: `BaseItemCode`/`MainItemCode` carregam na verdade
`StorageSeq`/`StorageItemSeq`. O wire format não foi alterado (compatibilidade); a
semântica real está documentada no handler e no serviço.

## Claim (D2 0B → `CashShopStorageClaimService`)

- Resolução da linha por `(AccountId, ItemSeq, StorageIndex, ItemType='P')` contra as
  linhas persistidas da conta autenticada. O `ItemCode` enviado pelo cliente **nunca** é
  autoridade (apenas logado para diagnóstico).
- `ItemSeq` é **global único por conta nas duas abas** (alocado em
  `CashShopStorageService.ReserveNextStorageIndexAsync`: `max(ItemSeq)+1` das linhas da
  conta), o que resolve a ambiguidade do D2 0B não indicar a aba.
- Validações **antes** de qualquer remoção: linha existe e é da conta; item existe;
  definição existe (forma aceita da incompatibilidade 6702 → código 22); espaço no
  inventário (→ código 21).
- Entrega: o **mesmo item persistido** (mesmo `ItemId`) vai para o inventário via
  `player.Inventory.AddItemAsync`; a linha é consumida; um único `SaveChangesAsync`
  (transação EF) grava a movimentação e a remoção.
- Débito WCoin no claim: **zero** (a cobrança ocorreu na compra/gift que criou a linha;
  delta verificado = 0 em todos os cenários).
- Rollback: `RemoveItemAsync` + restauração do slot + `Detach` da linha e do item;
  rollback incompleto aposenta a sessão (`UnsafeContextRetirement`), como nos fluxos de
  compra e gift.

## Segurança

- **IDOR**: o claim de outra conta é recusado com `StorageNotFound` (1), sem revelar a
  existência da linha alheia (cenário G — PASS).
- Conta sempre do jogador autenticado; nenhum identificador do cliente é usado como
  autoridade de conta.
- Duplo claim: a segunda chamada encontra a linha já consumida no mesmo commit da
  primeira entrega → `StorageNotFound` (cenário E — PASS).

## Atomicidade

- Item movido + linha removida em **um** `SaveChangesAsync` (unit of work do EF).
- Inventário cheio: recusa **antes** do staging; linha e item permanecem (cenário D —
  PASS, código 21, wallet e transações inalterados).
- Falha de persistência: rollback completo da unidade de trabalho (coberto por testes
  automatizados; rollback de Save/Commit não é simulado diretamente no banco — registrado
  como limitação de cobertura de homologação).
- A referência `row.Item` é limpa antes da remoção da linha: o `DeleteAsync` do provider
  EF remove também os membros `[MemberOfAggregate]` da linha — que é o próprio item
  entregue (ver "Bugs de produção"). O cascade **no banco** é Item → linha (deletar o
  item remove a linha; deletar a linha nunca toca o item), verificado empiricamente.

## Concorrência

- Claims da mesma conta serializados por `AsyncLock` por conta (serviço singleton,
  processo único) — mesmo escopo dos fluxos de compra e gift.
- Linha consumida no mesmo commit da entrega: uma linha rende no máximo um item entregue
  (cenário E — PASS).

## Quantity

- A quantidade do catálogo é aplicada na entrega (compra e gift) — Fase 9 mantém o
  comportamento da Fase 8 (`bd0ce53d9`).
- Durabilidade preservada na entrega (cenário C — Splinter of Armor 13/32 com
  Durability 15 → entregue com 15, PASS).
- Nota documentada: nenhuma oferta real do catálogo aponta `Quantity > 1` para um item
  stackable existente (incompatibilidade do item 6702); o cenário C valida a
  preservação de duração/quantidade com linha descartável.

## Gift → Claim

- Gift (Fase 8) cria a linha `isGift=True` no storage do destinatário, debitando o
  remetente; o **claim** é do destinatário e não debita ninguém (cenário B — PASS:
  wallet do remetente inalterada durante o claim, item entregue ao destinatário).
- Presente recebido enquanto o destinatário já está logado (linha commitada por outro
  contexto) é resgatável: a resolução do item cai para um contexto curto de leitura
  (ver "Bugs de produção").

## Bugs de produção descobertos e corrigidos

Ambos invisíveis à suíte InMemory (o provider InMemory não modela roteamento de carga
nem cascade de agregado); revelados apenas pela homologação com EF/PostgreSQL real.
Correção aplicada com re-execução completa da homologação (RED → GREEN no runner).

1. **Item de linha carregada do banco não resolvia no contexto do jogador** —
   `CacheAwareRepositoryProvider` roteia `Item` para o `ConfigurationTypeRepository<Item>`
   (cache de configuração: somente estoques de lojas de mercador). Itens de conta nunca
   estão nesse cache: `row.Item` vinha `null` no contexto do jogador para toda linha
   commitada por outra sessão → todo claim de presente era recusado com 22.
   **Fix**: `CashShopStorageClaimService` carrega a linha num contexto curto completo
   (onde item e definição são lidos do banco) e assume a posse do item no contexto do
   jogador via `Attach` (o contexto de conta ignora tipos de configuração, então a
   posse não arrasta grafo de configuração). A identidade persistente do item é
   preservada.
2. **`DeleteAsync` da linha apagava o item entregue do banco** —
   `EntityFrameworkContextBase.DeleteAsync` remove também os membros
   `[MemberOfAggregate]` da linha; o `Item` da linha de storage é membro do agregado →
   o claim apagava o item que acabara de entregar (perda de dados, o item não chegava
   nem ao inventário nem permanecia no storage).
   **Fix**: a referência `storageItem.Item` é limpa antes do `DeleteAsync`, de modo que
   apenas a linha seja removida; o item — já dono do slot do inventário — é gravado pelo
   mesmo commit.

Arquivos de produção alterados: `src/GameLogic/PlayerActions/CashShop/CashShopStorageClaimService.cs`,
`src/Startup/Program.cs` (DI com a fábrica de contexto), e atualização dos construtores
nos testes (`CashShopStorageClaimTests`, `UnsafeContextRetirementTests`).

## Homologação (runner `tools/cashshop-homologation -- claim`)

Formato de saída por cenário: `[X] Nome ................ PASS/FAIL`. Run final
(**0 desvios**, exit 0):

```
[A] Purchase -> Claim ........ PASS  (compra pacote 35 → claim; item 13/20 entregue com o
                                     MESMO ItemId; linha consumida; wallet delta 0; tx delta 0)
[B] Gift -> Claim ............ PASS  (gift de cshomolo → claim de cshomol2; wallet do remetente
                                     inalterada no claim; mesmo ItemId entregue)
[12] ItemSeq global .......... PASS  (linha na aba de compra seq 0 + linha na aba de gift seq 1;
                                     ambos os claims Success; storage 2 → 0; 13/20 e 13/80 entregues)
[C] Quantity ................. PASS  (Splinter 13/32, Durability 15 → entregue com 15)
[D] Inventory Full ........... PASS  (inventário cheio legitimamente → código 21; linha e item
                                     preservados; wallet/transações inalterados)
[E] Double Claim ............. PASS  (1º Success; 2º StorageNotFound; storage 0)
[F] Missing Definition ....... PASS  (item sem definição → código 22; linha e item preservados)
[G] IDOR ..................... PASS  (claim de outra conta → StorageNotFound; dono intacto)
=== Homologacao do claim: 0 cenario(s) com desvio ===
```

Evidências PostgreSQL (verificação direta por `psql`):

- Linhas consumidas removidas de `data."CashShopStorageItem"`.
- Itens entregues presentes em `data."Item"` com `ItemStorageId` do inventário do
  personagem e o **mesmo `ItemId`** referenciado pela linha antes do claim.
- Carteiras (`WCoin`) sem débito no claim (delta 0 em A, B, 12, C, D, E, F, G).

## WCoin (delta do claim)

- **0** em todos os cenários. Débitos observados apenas na compra (-100) e no gift
  (-100/-150), como esperado.

## Testes

- Filtro CashShop (`MUnique.OpenMU.Tests.CashShop`): **126/126 GREEN**.
- Suíte completa: ver relatório final (executada após os fixes; 0 falhas).
- Limitação: os dois bugs de produção desta fase não são reproduzíveis no provider
  InMemory; o veículo RED→GREEN foi o runner de homologação (automatizado), e a suíte
  unitária continua cobrindo as regras de negócio (validações, códigos, rollback lógico).

## Pendências (fora do escopo da Fase 9)

- **D2 0E** (gift com remetente/mensagem, 244 bytes) — não implementado.
- **GiftSender/GiftMessage** — persistência dos nomes/mensagens do D2 0E.
- **Duration/ExpiresAt** — expiração de itens do storage.
- **Item 6702** — ofertas com quantidade para item não stackável; forma aceita recusada
  com código 22 (não corrigir o item em si).
- **D2 0A (Throw)** — código morto no cliente; não implementar.

## Integridade do repositório

- Sem `push`; commits locais pequenos e atômicos; tag local anotada
  `cashshop-phase9-complete` (sem sobrescrever tags existentes).
- Limpeza do banco de homologação: apenas dados descartáveis criados pelo runner
  (itens descartáveis removidos com suas linhas via cascade); os 2 Wizard's Rings
  pré-existentes (13/20, de 2026-10-02) e as transações históricas foram preservados.
