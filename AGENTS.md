# OpenMU — Regras de Orquestração para OpenClaude

## 1. Contexto do projeto

Este workspace é um servidor MU Online baseado em OpenMU.

Repositório de referência informado pelo proprietário:
`https://github.com/isaias-pereira/OpenMU.git`

Tecnologias e contexto já existentes:
- C# / OpenMU no backend do jogo.
- PostgreSQL como banco de dados.
- Docker/Compose para execução e build da infraestrutura.
- Plugins próprios do servidor devem continuar compatíveis com o build.
- Painel web/admin separado do núcleo do servidor.
- Sistemas em desenvolvimento incluem WCoin, VIP, eventos, comandos customizados, tickets e Cash Shop/XShop.
- Investigação do XShop/Cash Shop inclui cliente `Main.exe`, protocolo `0xD2`, catálogo, moedas e integração cliente-servidor.

## 2. Regra principal

O agente principal deve atuar como ORQUESTRADOR. Para tarefas que atravessam mais de um domínio, deve dividir o trabalho entre subagentes especializados quando a ferramenta de agentes estiver disponível.

Não inventar arquivos, classes, protocolos, tabelas, campos ou endpoints. Primeiro localizar e confirmar no código real.

## 3. Fluxo obrigatório para mudanças relevantes

1. Explorar o repositório e localizar a implementação atual.
2. Identificar dependências e impacto.
3. Definir a menor mudança segura.
4. Delegar investigação/implementação especializada quando aplicável.
5. Implementar preservando padrões existentes.
6. Compilar o escopo alterado.
7. Executar testes existentes e testes direcionados.
8. Fazer revisão de regressões.
9. Relatar arquivos alterados, testes executados e limitações.

## 4. Delegação por domínio

- Arquitetura, localização de código e dependências → `openmu-architect` / `openmu-codebase-explorer`
- C# e lógica do servidor → `openmu-backend`
- PostgreSQL, queries e persistência → `openmu-postgres`
- Plugins e compatibilidade de extensão → `openmu-plugin`
- XShop/Cash Shop/0xD2/Main.exe → `openmu-cashshop`
- VIP, WCoin, eventos e regras de gameplay → `openmu-game-systems`
- Site, painel GM, integração web → `openmu-web`
- Docker, Compose, build e deploy → `openmu-docker`
- Autenticação, autorização, segurança e APIs → `openmu-security`
- Compilação, testes e validação → `openmu-test-validator`
- Revisão final de qualidade → `openmu-code-reviewer`

## 5. Orquestração recomendada

### Feature pequena
explorer → especialista → validator

### Feature média
architect → especialista(s) → validator → reviewer

### Feature grande
architect → explorer → especialistas em paralelo quando independentes → validator → reviewer

O agente principal deve consolidar as respostas dos subagentes antes de encerrar a tarefa.

## 6. Banco de dados

PostgreSQL é parte oficial da arquitetura deste servidor. Alterações persistentes devem verificar:
- tabelas existentes;
- migrations/DDL adotado pelo projeto;
- chaves, índices e constraints;
- transações e concorrência;
- compatibilidade com dados já existentes;
- impacto no Docker e no ambiente de produção.

Nunca apagar ou recriar dados de produção como tentativa de correção sem pedido explícito.

## 7. Plugins

Plugins existentes são uma restrição de compatibilidade. Antes de alterar build, Dockerfile ou Compose:
- descobrir como os plugins são carregados;
- preservar o mecanismo de extensão atual;
- confirmar se o artefato final contém os plugins necessários;
- não assumir que `docker build` padrão inclui automaticamente extensões do projeto.

## 8. Cash Shop / XShop

A investigação do Cash Shop é incremental. Toda descoberta sobre `0xD2`, IDs, subcódigos, tamanhos, campos, catálogo, moedas e fluxo do `Main.exe` deve ser classificada como:
- CONFIRMADO no código/trace/arquivo;
- PROVÁVEL, mas ainda não validado;
- DESCONHECIDO.

Só implementar protocolo com evidência suficiente. Não fabricar bytes de pacote.

## 9. Segurança

Nunca colocar segredos, tokens, senhas ou chaves de API em arquivos versionados. Configurações de OpenClaude que contenham credenciais devem ficar fora do repositório ou em arquivos locais ignorados pelo Git.

## 10. Git

Não fazer `reset --hard`, `clean -fd`, rebase destrutivo ou apagar arquivos não relacionados. Antes de alterações grandes, verificar `git status` e preservar trabalho do usuário.

## 11. Definição de pronto

Uma tarefa só é considerada concluída quando:
- o código compila no escopo aplicável;
- os testes disponíveis foram executados;
- os caminhos de erro relevantes foram avaliados;
- PostgreSQL/Docker/plugins foram considerados quando afetados;
- a mudança está descrita objetivamente;
- não existem alegações de teste que não tenham sido realmente executados.
