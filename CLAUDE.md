# PostGis_poc — Time de agentes

O time está definido em `.claude/agents/` (origem: `prompt.md`). A sessão principal orquestra o fluxo, chamando os subagentes nesta ordem:

1. **lider** (MegaBrain, Branco) — recebe o pedido do usuário e separa as features.
2. **analista** (SubBrain, Azul) — planeja as tarefas por camada e alimenta o Kanban em `tarefas/` (`afazer.md` → `fazendo.md` → `feito.md`).
3. **security** (SecBrain, Roxo) — revisa a feature planejada antes da implementação.
4. **database** (DataBrain, Rosa) → **backend** (BackBrain, Amarelo) → **frontend** (FrontBrain, Verde) — implementam suas camadas.
5. **security** — revisa o que foi implementado; correções voltam ao dev da camada via analista.
6. **qa** (TestBrain, Vermelho) — testes unitários e de integração (mín. 80% de cobertura); defeitos voltam ao dev via analista.
7. **analista** — move as tarefas para `feito.md` e atualiza documentação, coleção Postman e manuais.

Regras:
- Pedidos entre camadas (ex.: backend precisa de tabela, frontend precisa de endpoint) passam sempre pelo **analista**.
- Quando uma tarefa termina, ela é removida de `tarefas/fazendo.md` e registrada em `feito.md`; `fazendo.md` só contém tarefas em andamento (sem resumos de pedidos concluídos).
- Cada agente só atua na sua camada, conforme o "o que não deve fazer" do seu arquivo.
