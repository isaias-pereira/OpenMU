# Fase 10 — Cash Shop Gift Storage: Homologação Final

## Status

**FASE 10 — HOMOLOGAÇÃO TÉCNICA APROVADA, VALIDAÇÃO VISUAL NO MAIN.EXE PENDENTE.**

A Rodada 2C aplicou a migration exclusivamente no banco de homologação
(`openmu_cashshop_validation`), homologou o fluxo completo Gift com metadados no
PostgreSQL real, capturou os bytes reais do D2 0E pelo caminho de listagem de
produção e validou Gift → Claim sem segundo débito. **0 cenários com desvio.**

## Baseline

- Branch `checkpoint/cashshop-property-resolution-20261004`, HEAD `9bb6b8e3f`
  (= tag `cashshop-phase9-complete`); suíte inicial 640/640 GREEN.
- Rodada 2B implementou o código e os testes (653/653); Rodada 2C homologou.

## Migration (aplicada só no banco autorizado)

- `20261011053051_AddCashShopStorageGiftMetadata` — aditiva: `GiftSender varchar(10) NULL`
  e `GiftMessage varchar(200) NULL` em `data."CashShopStorageItem"`.
- **Conexão verificada antes de aplicar** (ETAPA crítica): a `ConnectionSettings.xml` aponta
  `Database=openmu`; o comando EF só foi executado com `DB_NAME=openmu_cashshop_validation`,
  confirmado com `SELECT current_database()` = `openmu_cashshop_validation` (localhost:5432).
  Sem o `DB_NAME`, o alvo seria o banco `openmu` — o banco principal permaneceu intacto
  (verificado: 50 migrations, 0 colunas Gift).
- Backup prévio: `pg_dump` 15 MB (`%TEMP%\openmu_cashshop_validation_pre_fase10_*.sql`).
- Pré/pós: 1 linha antes, 1 linha depois (preservada, metadados NULL);
  `__EFMigrationsHistory` 56 → 57; 5 índices e 2 FKs preservados.
- Down: 2× `DropColumn` dos mesmos campos; não executado.

## Segurança UTF-8 (Rodada 2C, ETAPA 2)

Casos críticos do `WriteFixedUtf8Field`, todos com decodificador UTF-8 **estrito**
(`UTF8Encoding(throwOnInvalidBytes: true)`) — 22/22 GREEN no projeto de pacotes:

| Caso | Entrada | Resultado |
|---|---|---|
| Limite exato | 198 ASCII + `é` (200 B) | 198 ASCII + NUL; `é` excluído inteiro; sem `C3` órfão |
| Emoji na fronteira | 197 ASCII + `😀` (201 B) | 197 ASCII + NUL; nenhum byte F0/9F/98/80 vaza |
| Remetente multibyte | `€€€a`/`€€€aa`/`ááááá`/`áááááá` | ≤10 bytes úteis, sem dividir caractere, NUL no campo |
| Vazio/NULL | `""` e `null` | campo 11/200 bytes todo zero |
| NUL embutido | `A\0B\tC` | conteúdo do fio termina no 1º NUL (determinístico) |
| Surrogate órfão | `A\uD83DB` | substituído por U+FFFD pelo encoder — saída sempre UTF-8 bem-formada |

**Persistência × exibição sem divergência silenciosa**: a normalização do
`CashShopGiftService` foi alterada na 2C para cortar a mensagem em **199 bytes UTF-8**
(em fronteira de caractere) — o MESMO limite do fio — e não mais em 200 caracteres.
O texto gravado é exatamente o que o destinatário verá (testado no banco real:
150×`á` (300 B) → persistido 99×`á`/198 B; mensagem vazia → NULL).

## Homologação no PostgreSQL real (`tools/cashshop-homologation -- gift-storage`)

Serviços de produção reais (`CashShopGiftService`, `CashShopStorageListAction`,
`CashShopStorageClaimService`, serializer D2 0E), catálogo real do banco, contas
sintéticas `cshomolo`/`cshomol2`. Evidências por cenário:

- **G — Gift com metadados**: WCoin 100000 → 99900 (débito único = preço 100);
  1 `WCoinTransaction` nova (27 → 28); storage do destinatário 0 → 1;
  linha `StorageIndex=0 ItemSeq=0 IsGift=True ItemType=P` com ItemId;
  `GiftSender='Cshomolo'` (personagem autenticado do remetente) e
  `GiftMessage='Olá, você ganhou um presente!'` persistidas com acentos.
- **H — Captura binária D2 0E** (caminho real D2 05 → `CashShopStorageListAction` → view →
  serializer, sobre conexão de homologação SpyConnection — não é captura de rede):
  1× D2 06 (12 B) + 1× D2 0E (**244 B**), 0× D2 0D; header `C1 F4 D2 0E`;
  sender 8 bytes úteis `Cshomolo`; mensagem 31 bytes úteis com acentos;
  offsets LE conferidos contra a linha persistida; último byte dos campos = NUL.
- **I — Legado + Purchase**: linha legada (metadados NULL) sai por D2 0E com campos
  11/200 bytes **zerados** (nenhum remetente inventado); aba 'S' emitiu 0× D2 0E e
  1× D2 06; aba 'G' não emite D2 0D.
- **J — Gift → Claim**: mesmo `ItemId` antes/depois
  (`fe29a101-…-149b`), storage consumido, item no inventário com Durability 30 preservada,
  WCoin do destinatário inalterado (99850 → 99850), delta `WCoinTransaction` = 0.
- **K — Mensagens**: longa (300 B) persistida já truncada em fronteira UTF-8 (198 B);
  vazia → NULL; ambos com entrega normal e débito único.
- **L — Account scope**: linha sintética de outra conta (`CashPoint` marcador 123456)
  não aparece na listagem do destinatário (1 linha D2 0E apenas, do próprio).
- **Cenários negativos originais** (runner `-- gift`, Fase 9): saldo insuficiente,
  destinatário inválido, pacote não-giftable e storage cheio — sem débito e sem
  storage indevido (reexecutados nesta rodada; ver relatório).
- **Falha de persistência injetada no banco real: NÃO EXECUTADO** (injeção não segura
  contra banco de homologação); coberta por testes unitários com `FailingSaveContext`
  (`ProcessGiftAsync_SaveFailure_LeavesNoPartialGiftMetadataOrDebit` — GREEN).

## Contrato D2 0E (implementado e homologado)

```text
244 bytes; C1 244 D2 0E;
@4/8/12/16/20 int32 LE (StorageIndex, ItemSeq, StorageGroup, ProductSeq, PriceSeq);
@24 double LE (CashPoint); @32 chItemType ('P'/'C');
@33 chSendUserName[11] UTF-8 (≤10 bytes úteis + NUL + padding zero);
@44 chMessage[200]   UTF-8 (≤199 bytes úteis + NUL + padding zero);
direção: SERVIDOR → CLIENTE (dispatcher do cliente WSclient.cpp:14480;
correção factual aplicada no phase-09-storage-final.md; doc da Fase 9 não reescrito em histórico).
```

## Testes finais

| Suíte | Resultado |
|---|---|
| `MUnique.OpenMU.Tests` (CashShop filter) | 140/140 GREEN (139 da 2B + 1 novo alinhamento byte-limit) |
| `MUnique.OpenMU.Network.Packets.Tests` | 494/494 GREEN (485 + 9 execuções dos casos críticos UTF-8) |
| `MUnique.OpenMU.Persistence.Initialization.Tests` | 25 PASS (23 + 2 novos) / 2 skips preexistentes / 0 failed |
| `MUnique.OpenMU.Tests` completa | ver relatório da rodada (gate final) |

## Limitações e pendências

1. **VALIDAÇÃO VISUAL NO MAIN.EXE: PENDENTE** — não existe ambiente de cliente
   configurado para homologação nesta rodada; a captura binária foi em-processo
   (SpyConnection), não em socket real. Roteiro manual:
   1. conectar o Main.exe ao servidor apontado para `openmu_cashshop_validation`;
   2. com `cshomolo`, enviar um presente (aba de presente, GiftFlag 184) para
      `Cshomol2` com a mensagem `Olá, você ganhou um presente!`;
   3. logar com `cshomol2`/`Cshomol2`, abrir o X-Shop → aba 'G': conferir
      "é um presente de Cshomolo" e a mensagem acentuada;
   4. verificar um presente antigo (sem metadados): remetente/mensagem em branco,
      sem crash, retirada funcionando;
   5. retirar o item (D2 0B) e conferir o inventário;
   6. conferir a aba 'S' com uma compra (só D2 0D, sem remetente/mensagem).
2. `PacketTwister` não afeta o layout (transparente na conexão).
3. Surrogate órfão vira U+FFFD por política do encoder .NET (documentada).
