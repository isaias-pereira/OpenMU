# Contexto do Projeto OpenMU

## Objetivo

Manter e evoluir um servidor MU Online baseado em OpenMU, com backend em C#, PostgreSQL, Docker e extensões por plugins, além de um painel web/admin.

## Repositório principal

`https://github.com/isaias-pereira/OpenMU.git`

## Diretrizes de implementação

- Preferir reutilização de serviços, handlers, commands, repositories e padrões já presentes.
- Evitar duplicação de infraestrutura.
- Persistência importante deve ser feita no PostgreSQL seguindo o padrão já usado no projeto.
- Alterações no build devem preservar plugins.
- Alterações de frontend devem ser integráveis ao painel existente.
- Testar em ambiente semelhante ao Docker de execução sempre que a mudança tocar infraestrutura.

## Sistemas relevantes

### WCoin
Moeda planejada/implantada para recompensas e sistemas web/in-game. Persistência deve ser vinculada à conta e respeitar concorrência/transações.

### VIP
Sistema de benefícios associado à conta/personagem conforme a implementação existente. Antes de criar novos campos, procurar entidades/serviços já relacionados a status de conta, planos, benefícios ou moedas.

### Eventos
Há painel de eventos e lógica de horários do servidor. Eventos podem ser definidos por plugins. O painel deve refletir a fonte real de configuração/estado, evitando dados fictícios.

### Comandos
Há comandos existentes como `/addstr`, `/addagi` e aliases desejados `/f`, `/v`, `/e`, `/a`, `/c`. Alterações devem reaproveitar o mecanismo de comandos existente.

### Tickets
Sistema simples de tickets para jogadores e GameMaster, com integração ao site e notificação após fechamento. A autenticação existente não deve ser reescrita quando uma tarefa pedir apenas UI/fluxo.

### Cash Shop / XShop
A pesquisa do XShop deve primeiro mapear o cliente `Main.exe`, o protocolo `0xD2`, estruturas de pacotes, IDs, catálogo e moedas antes da implementação.

## Princípio de preservação

Este arquivo fornece contexto, mas nunca substitui a inspeção do código real. Em caso de conflito, o código e os testes do workspace são a fonte de verdade.
