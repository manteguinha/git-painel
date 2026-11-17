# Histórico de mudanças

A evolução do Git Painel, na ordem em que as funcionalidades foram criadas.

## 1. Primeira versão
- Janela nativa do Windows (WinForms) com tema Dracula, sem instalação: um único `.exe`.
- Abre uma pasta pai e encontra todos os repositórios Git dentro dela.
- Lista os arquivos alterados de cada repositório e mostra o diff ao clicar, no modo unificado ou lado a lado.
- Rolagem suave, barras de rolagem próprias, barra de título escura.

## 2. Organização por módulos
- Detecta subprojetos (pastas com `package.json`, `serverless.yml`, `requirements.txt` etc.) e agrupa os arquivos por módulo, por exemplo `apps/cadastro`.
- Estrela para marcar os "meus módulos" e filtro **Todos / Meus módulos**.
- Respeita o `.gitignore`.

## 3. Leitura mais confortável
- Quebra de linha no diff (Alt+Z).
- Atualização automática ao salvar arquivos, fazer commit ou trocar de branch.
- Recuo com linhas-guia, como uma árvore.

## 4. Commit pelo app
- Caixas para marcar arquivos ou módulos inteiros e painel de commit.
- Detecta o padrão de mensagens do repositório (Conventional Commits, com ou sem emoji) e sugere tipo e escopo.

## 5. Fluxo completo de Git
- Commit por trecho: marcar só alguns trechos (`@@`) de um arquivo.
- Desfazer o último commit ainda não enviado.
- Push e pull com confirmação, histórico de commits por repositório ou módulo.
- Cores de sintaxe no diff e realce das palavras que mudaram.
- Busca de arquivos (Ctrl+F).

## 6. Integrações e acabamento
- Menu do botão direito: abrir no Explorador, Bloco de Notas ou VS Code; copiar caminho.
- Seleção e cópia de código no diff (Ctrl+C, Ctrl+A, duplo clique seleciona palavra).
- Painel de commit compacto, com seletor de tipo e escopo.
- Botões **Enviar / Atualizar / Publicar** sempre visíveis quando há algo a sincronizar.
- Barra do app integrada à barra de título da janela.

## 7. Robustez
- Animações nos botões (spinner durante push, pull e commit; efeito de pressionar).
- Proteção contra clique duplo e contra duas operações ao mesmo tempo.
- Erros do Git explicados em português.
- Expandir e recolher tudo.
- Na primeira execução, pergunta a pasta de projetos.

## 8. Atualização automática (v1.1.0)
- O app verifica as releases do GitHub ao abrir e a cada 6 horas, em silêncio.
- Quando há versão nova, aparece uma pílula discreta "Nova versão X" na barra do topo; ao clicar, mostra as notas e atualiza sozinho (baixa, troca o exe e reinicia mantendo as preferências).
- A versão atual aparece ao lado do nome do app.

## 9. Branches divergentes (v1.2.0)
- Quando o seu branch e o remoto têm commits novos ao mesmo tempo, o **Atualizar** oferece juntar as versões com um merge (como o "Sync" do VS Code), avisando antes se vai haver conflito e em quais arquivos.
- O **Enviar** com o remoto à frente traz as novidades primeiro e depois envia, em vez de falhar.
- Merge com conflito: aviso na linha do repositório, opção de abrir no VS Code, **Concluir merge** (confere que não sobrou marcador de conflito) e **Cancelar o merge**.
- Commits avulsos ficam bloqueados durante um merge, para não estragá-lo.

## 10. Commits a enviar (v1.3.0)
- Seção **A enviar** em cada repositório, com os commits locais que o próximo push vai levar ao remoto (ou que ainda não estão em nenhum remoto, se o branch não foi publicado).
- Cada commit abre para mostrar os arquivos; clicar num arquivo mostra o diff daquele commit.
- Botão direito no commit: copiar hash ou mensagem, desfazer (o mais recente).
- Datas dos commits em português ("há 5 min", "ontem, 14:30").

## 11. Interface refinada e mais leve (v1.4.0)
- **Visão geral:** sem arquivo aberto, um cartão por repositório com branch, alterações, a enviar/receber e último commit; clicar leva ao repositório.
- **Lista:** ícone por tipo de arquivo, `+N −M` em cada arquivo, status "N" para arquivo novo (antes "U", que no Git significa conflito), hover suave e dicas em ícones, pílulas, estrelas e nomes cortados.
- **Teclado:** ↑ ↓ percorrem todas as linhas, ← → recolhem e expandem, Enter abre no VS Code, Espaço marca arquivo ou módulo; F1 mostra todos os atalhos.
- **Diff:**
  - navegação entre alterações (F7 / Shift+F7, Alt+↓ / Alt+↑) com "2 de 5" no cabeçalho;
  - busca no diff com contador;
  - régua lateral com as alterações e as ocorrências;
  - "N linhas ocultas" para ver mais contexto e opção de arquivo inteiro;
  - ignorar mudanças só de espaço;
  - realce de palavras por token, com vários trechos por linha;
  - indicador de carregamento;
  - diffs gigantes mostram 20 mil linhas, com "Mostrar tudo".
- **Acabamento:**
  - cabeçalhos alinhados;
  - painéis de commit e histórico que deslizam;
  - diálogos sem moldura nativa, com cantos arredondados;
  - menus arredondados (Windows 11);
  - foco visível ao navegar com Tab.
- **Desempenho:**
  - pincéis e medidas de texto em cache;
  - pintura e clique por busca binária;
  - o diff não é refeito quando nada mudou, mantendo a seleção e a posição;
  - o redesenho com quebra de linha espera o divisor parar;
  - atualização periódica a cada 5 minutos com o monitor de arquivos ativo (antes 30 s).
