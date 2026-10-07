# CashShopCatalogBuyableGroups.json

Snapshot normalizado dos **grupos de produto do catálogo IBS considerados compráveis por W Coin(P)**,
usado pelo teste `CatalogParity_AllBuyableGroupsResolve`.

Este arquivo **não é cópia dos IBS**. Contém apenas os campos necessários para reproduzir, offline e
sem caminho de máquina, a seleção que o `CashShopSelectionResolver` faz hoje.

## Origem

| Item | Valor |
|---|---|
| Fonte | IBS `512.2012.084` |
| Arquivo de produtos | `IBSProduct.txt` |
| SHA-256 do produto | `04AFDB66239B44CA2153FCB40BBF291F0F435751A8050C8228D6F2EF2985FC72` |
| Arquivo de pacotes | `IBSPackage.txt` |
| SHA-256 de pacotes | `39CE724CC9ACF4AB9037FE53BAFF85AFE267E8A0169ECF095A94DD879319B74C` |
| Gerador | `tools/generate-cashshop-fixture.ps1` |
| Gerado em | 2026-10-04 |
| Grupos esperados | **143** |
| Pacotes esperados | **113** |

## Algoritmo

1. Lê `IBSProduct.txt` e `IBSPackage.txt` (CP949, separador `@`).
2. Filtra pacotes por todos os predicados que o resolver aplica:

   | Campo | Valor | Significado |
   |---|---|---|
   | `CashType` | 509 | W Coin(P) — a carteira que este servidor cobra |
   | `DeleteFlag` | 181 | pacote ativo |
   | `SalesFlag` | 182 | à venda |
   | `PackageProductType` | 170 | pacote normal (não sorteio) |
   | `EventFlag` | 200 | item normal |
   | `CashTypeFlag` | 669 | compra online |
   | `DeductMileageFlag` | 0 | não é compra por milagem |

3. Para cada pacote, forma o produto cartesiano `ProductSeqList × PriceSeqList`.
4. **Grupo** = `(ProductSeq, PriceSeq, InGamePackageID)`. Várias linhas de `PropertySeq` diferentes
   compartilham a mesma chave de grupo e são **acumuladas**, não sobrescritas.
5. Emite pacotes e grupos ordenados por chave, linhas por `PropertySeq`.

Determinístico: rodar o gerador duas vezes produz o mesmo SHA-256.

## Estrutura

```jsonc
{
  "metadata": { /* proveniência, hashes, algoritmo, filtros, contagens esperadas */ },
  "packages": [ { packageProductSeq, productDisplaySeq, cashType, deleteFlag, salesFlag,
                  giftFlag, eventFlag, cashTypeFlag, packageProductType, deductMileageFlag,
                  startDate, endDate, price, inGamePackageID,
                  productSeqList: [...], priceSeqList: [...] } ],
  "groups":   [ { productSeq, priceSeq, inGamePackageID, price, productType,
                  storageGroup, deleteFlag, productName,
                  rows: [ { propertySeq, propertyType, value, unitName, unitType,
                            mustFlag, shareFlag, viewOrder, propertyName } ] } ]
}
```

## Observações

- As linhas de cada grupo reproduzem fielmente o IBS, incluindo `PropertyName` em coreano,
  porque a chave lógica atual (`BuildLogicalProductKey`) usa `PropertyName`, `Value`, `UnitName`,
  `UnitType` e `MustFlag` — são justamente esses campos que diferem entre linhas complementares e
  que hoje fazem a compra ser recusada.
- `productName` está incluído porque é idêntico dentro de cada grupo e participates da chave atual.
- **Nenhum campo foi removido da persistência** por decisão deste snapshot: ele é um recorte para
  teste, não um novo contrato de domínio.
- O teste **não** lê os IBS e **não** exige variável de ambiente. Para regenerar, aponte o gerador
  para o diretório dos IBS: `& tools/generate-cashshop-fixture.ps1 -Source '<dir>'`.