# Histórico de Versões (Changelog)

Todas as mudanças principais do Custódia seguem versionamento semântico (`MAJOR.MINOR.PATCH`).

## [4.0.0] - 2026-11-27

### Novidades
- **Instalador leve para Linux (`Custodia-Installer-x86_64.run`):** interface gráfica leve (~30 KB, sem AppImage nem FUSE) onde você escolhe a versão e o formato (`.deb`, `.rpm` ou `.AppImage`). Ele baixa o pacote, valida a integridade, instala e se apaga sozinho ao terminar.
- **Pacote `.deb` nativo:** suporte oficial em `.deb` para Ubuntu, Debian, Pop!_OS e Linux Mint, além de `.rpm` e `.AppImage`.
- **Compartilhamento e aba "Compartilhados comigo":** navegue e baixe arquivos compartilhados com sua conta, gere ou remova links públicos (leitura ou edição) e convide pessoas por e-mail direto pelo app.
- **Meus Arquivos renovado:** visual mais limpo em linha única, miniaturas carregando rápido na grade, ícones próprios da TundraLab, suporte a Google Vids e opção **Salvar como...** para escolher a pasta ao baixar.
- **Fila de Transferências com filtros e retomada:** filtre por envios, downloads ou falhas, retome qualquer item com erro em um clique, retome envios/downloads grandes se a internet cair e limite a velocidade nas configurações se quiser.
- **Configurações com salvamento automático:** as opções passam a salvar na hora, sem botões manuais de recarregar ou salvar, e a aba **Sobre** agora tem atalho direto para o repositório no GitHub.
- **Correções de estabilidade e fluidez:** eliminados travamentos durante transferências e decodificação de miniaturas, corrigida a direção da animação de reload e removidos ajustes visuais pendentes na Lixeira e na fila.

## [3.0.5] - 2026-11-26

### Adicionado
- **Aba Favoritos no Hub de Arquivos:** agora você pode marcar arquivos e pastas com estrela em um clique, gerenciar favoritos em lote e acessá-los rapidamente na nova aba **Favoritos**, com sincronização direta com o Google Drive e suporte offline.
- **Envios, downloads e sincronizações simultâneos com controle de limite:** agora você pode enviar vários arquivos ou pastas inteiras de uma só vez, baixar múltiplos itens em paralelo e definir em **Configurações > Sincronização** se deseja limitar entre 1 e 6 transferências simultâneas ou operar sem limite.

### Alterado
- **Exclusão direta de pastas:** ao excluir uma pasta inteira no computador ou no Google Drive, a pasta passa a ser movida diretamente para a lixeira em bloco em vez de remover arquivo por arquivo.
- **Transferências otimizadas para arquivos maiores:** os blocos de envio e download agora escalam conforme o tamanho do arquivo para reduzir solicitações e melhorar a velocidade.
- **Indicador animado em Pastas de Sincronização:** a bolinha estática foi substituída por um emblema animado com pulso contínuo quando a pasta está sendo monitorada e animação de rotação durante a sincronização.
- **Etiqueta mais limpa na Fila de Transferências:** removido o selo antigo com pontos e texto duplicado, exibindo apenas o horário e um chip de direção e status no estilo Material Design 3.

### Corrigido
- **Pré-visualização de arquivos no Hub:** corrigido o botão **Visualizar** para imagens, PDFs, documentos, textos e vídeos, eliminando travamentos e conflitos com as miniaturas em grade.

## [3.0.4] - 2026-11-25

### Adicionado
- **Tema Claro e modo Automático (Sistema):** agora você pode escolher entre **Escuro**, **Claro** ou **Automático (Sistema)** em **Configurações > Aparência e Idioma**, com aplicação imediata em todo o aplicativo.
- **Contraste e ícones adaptados ao modo claro:** cores de destaque, status, cartões, barra lateral e logotipos da TundraLab se ajustam automaticamente para oferecer leitura confortável tanto no tema escuro quanto no claro.

## [3.0.3] - 2026-11-25

### Alterado
- **Abas redesenhadas no estilo Material Design 3:** as telas **Pastas de Sincronização**, **Fila de Transferências**, **Logs de Atividade** e **Sobre** ganharam visual mais limpo, cartões com menos bordas, botões contextuais que aparecem apenas quando necessários e navegação mais direta.
- **Textos e subtítulos mais claros:** descrições e legendas em todo o aplicativo foram simplificadas para uma linguagem natural e fácil de entender no dia a dia.
- **Fila de Transferências inteligente:** sempre que uma tarefa é concluída, ela sobe para o topo da lista e a tela rola automaticamente para o início para destacar o item finalizado.

### Corrigido
- **Rolagem completa na aba Sobre em tela cheia:** ajuste no espaçamento inferior para garantir que os botões finais de suporte e envio fiquem sempre totalmente visíveis ao rolar até o fim.

## [3.0.2] - 2026-11-24

### Alterado
- **Visual renovado em Meus Arquivos:** a grade agora separa **Pastas** (em cartões horizontais compactos) e **Arquivos** (em cartões limpos com miniaturas ou ícones suaves), exibe a barra de seleção apenas quando há itens marcados e mostra as caixas de seleção somente ao passar o mouse ou selecionar.
- **Cabeçalho e busca mais enxutos:** barra de caminho simplificada, busca integrada com botão rápido para limpar, filtro de abas mais claro (**Google Drive**, **Sincronizados** e **Lixeira**) e ações principais com destaque visual melhorado.
- **Seis cores de destaque:** além de Azul e Verde Esmeralda, agora você pode escolher entre **Roxo**, **Âmbar**, **Rosa** e **Ciano** em **Configurações**.
- **Ícones da barra lateral e bandeja refinados:** novos ícones vetoriais próprios da TundraLab com leitura mais nítida em todo o aplicativo e na bandeja do sistema.

### Corrigido
- **Pré-visualização mais confiável:** suporte aprimorado para arquivos de texto sem extensão e alternativa automática ao visualizar documentos grandes do Google Workspace.
- **Preservação de personalizações durante a sincronização inicial:** ajustes de apelido, cor e ícone em pastas recém-vinculadas não são mais sobrescritos enquanto a primeira sincronização acontece em segundo plano.

## [3.0.1] - 2026-11-24

### Adicionado
- **Seletor de cor de destaque:** agora você pode alternar o visual do Custódia entre **Azul** e **Verde Esmeralda** diretamente em **Configurações**.
- **Renomear e mover em Meus Arquivos:** novos atalhos na barra de ações e no menu de contexto para renomear itens ou movê-los para outra pasta, tanto no Google Drive quanto nas pastas locais.
- **Confirmação antes de ações destrutivas:** aviso de confirmação antes de remover uma pasta sincronizada ou limpar todo o histórico de logs de atividade.

### Melhorado
- **Configurações organizadas por categorias:** divisão em abas (**Geral**, **Contas**, **Sincronização** e **API Local**) com blocos mais enxutos e fáceis de navegar.
- **Painel de atualizações ajustado:** rolagem própria no quadro de novidades e quebra de linha nos avisos de atualização na aba **Sobre**.

## [3.0.0] - 2026-11-24

### Alterado
- **Visual totalmente renovado em Material Design 3:** toda a interface do Custódia agora conta com um tema escuro mais limpo, botões arredondados, barras de navegação em pílula e cartões sem bordas pesadas.
- **Telas mais organizadas no estilo Linux/GNOME:** as seções de **Configurações**, **Contas**, **Sobre** e **Logs** agora agrupam opções relacionadas em listas limpas e fáceis de ler.
- **Cores mais claras para status e alertas:** indicadores de envio, download, sucesso, aviso e erro foram padronizados para oferecer melhor contraste e leitura rápida.

### Adicionado
- **Privacidade, Termos de Uso e aceite inicial:** ao abrir o Custódia pela primeira vez, você pode revisar os Termos de Uso, a Política de Privacidade e escolher se deseja receber e-mails automáticos de boas-vindas e desconexão. Esses documentos também ficam disponíveis a qualquer momento na aba **Sobre**.

## [2.9.2] - 2026-11-24

### Alterado
- **Personalização de pastas direto em Meus Arquivos:** clique com o botão direito em qualquer pasta (na lista ou na grade) e escolha **Personalizar pasta** para definir apelido, cor e ícone, mantendo suas preferências tanto nas pastas sincronizadas quanto nas pastas locais e da nuvem.
- **Destaque vermelho para ações de exclusão:** as opções **Mover para Lixeira**, **Excluir permanentemente** e **Esvaziar Lixeira** agora aparecem destacadas em vermelho no menu de contexto de **Meus Arquivos**.
- **E-mails automáticos sem precisar de permissão do Gmail:** os avisos oficiais de boas-vindas ao conectar uma conta e de confirmação ao desconectar agora são enviados diretamente pela TundraLab, sem exigir acesso ao seu Gmail.
- **Documentação e guia de segurança atualizados:** explicações mais claras sobre privacidade, permissões e funcionamento do aplicativo.

### Corrigido
- **Envio de sugestões mais limpo na aba Sobre:** a opção de anexar diagnóstico técnico e histórico de erros agora aparece somente quando você escolhe relatar um problema (**Bug / Erro**), ficando oculta no modo **Sugestão**.
- **Visual mais limpo no cartão de credenciais:** remoção de etiquetas redundantes na área de configuração de acesso em **Configurações**.

## [2.9.1] - 2026-11-24

### Alterado
- **Ícone da bandeja redesenhado:** o ícone na barra superior do sistema ficou mais nítido e fácil de identificar durante envios, downloads, exclusões, sincronizações e alertas.
- **Gerenciamento de contas e credenciais mais prático:** agora é possível arrastar e soltar o arquivo de credenciais direto na aba **Configurações**, detectar automaticamente arquivos baixados na pasta Downloads e gerenciar cada conta conectada com ações rápidas (**Alternar**, **Reconectar**, **Trocar JSON**, **Pasta** e **Revogar**).
- **Pastas preservadas ao desconectar:** ao remover uma conta, você pode escolher manter a lista de pastas configuradas para quando conectar novamente no futuro.
- **Suporte unificado para Bugs e Sugestões:** a aba **Sobre** agora adapta o formulário conforme o tipo de envio escolhido, com proteção automática de dados pessoais caso você opte por incluir um resumo de diagnóstico.
- **Menu de contexto completo em Meus Arquivos:** clicar com o botão direito em áreas vazias da lista ou grade também oferece atalhos para **Nova Pasta**, **Enviar Arquivo**, **Atualizar**, **Voltar** e seleção de itens.

### Corrigido
- **Status preso na bandeja após concluir tarefas:** correção para garantir que o menu da bandeja volte ao estado ocioso assim que todas as transferências terminarem.
- **Avisos por e-mail em contas secundárias:** melhoria na entrega de notificações de conexão e desconexão quando há mais de uma conta configurada.

## [2.9.0] - 2026-11-24

### Adicionado
- **Ícone dinâmico na bandeja do sistema:** acompanhamento visual em tempo real de envios, downloads, exclusões, sincronização e erros direto na barra superior do sistema.
- **Criação de pastas e envio rápido em lote:** novo botão **Nova Pasta** em **Meus Arquivos** e no **Navegador Remoto**, além da possibilidade de selecionar várias pastas do computador de uma só vez mantendo a estrutura original (inclusive pastas vazias).
- **Personalização de pastas:** defina apelido, cor e ícone personalizados em **Pastas de Sincronização**, refletidos automaticamente em **Meus Arquivos**.
- **Alertas visuais para falhas de transferência:** avisos claros na barra lateral, na **Fila de Transferências** e em **Meus Arquivos** caso algum envio ou download falhe.
- **Sons personalizáveis nas notificações:** escolha entre 6 toques simples (com botão para testar) ou deixe as notificações silenciosas.
- **Avisos opcionais por e-mail:** opção para receber confirmação automática ao conectar ou desconectar sua conta Google.
- **Previsão de atualizações e suporte integrado na aba Sobre:** exibição da frequência de checagem automática, aviso imediato quando não há internet e formulários para relatar erros ou enviar sugestões.
- **Notificação ao concluir a primeira sincronização:** aviso dedicado quando uma nova pasta adicionada termina de sincronizar pela primeira vez.

### Alterado
- **Renomeação de Meu Driver para Meus Arquivos:** novo nome aplicado em toda a interface, menu da bandeja, navegador remoto e documentação.

### Corrigido
- **Contagem completa na Fila de Transferências:** remoção do limite que interrompia a exibição em 200 itens, mostrando o histórico e os totais corretos.
- **Animação de sincronização parada ao concluir:** correção para impedir que o indicador continue girando depois que todas as pastas terminam de sincronizar.
- **Verificação de atualizações sem internet:** o botão de checagem agora encerra imediatamente e informa quando o computador está offline.

## [2.8.0] - 2026-11-20

### Alterado
- **Novo nome e identidade visual oficial (`Custódia by TundraLab`):** o projeto agora se chama **Custódia** (*"Seus arquivos. Sempre seguros."*), com nova logomarca, novo ícone na bandeja do sistema, novos pacotes `Custodia` (`.rpm` e `.AppImage`) e migração automática das configurações e contas já salvas no computador.
- **Layout da aba Sobre e painel de atualizações ajustado:** os cartões da aba **Sobre** agora aproveitam toda a largura da área principal da janela, o botão **Baixar e atualizar agora** fica posicionado logo acima das notas da versão para acesso imediato e o quadro de novidades conta com rolagem própria, permitindo atualizar o aplicativo confortavelmente sem precisar redimensionar a janela.

## [2.7.4] - 2026-11-20

### Corrigido
- **Inicialização com o sistema sem duplicidade nem fechamento:** ao ligar o computador e entrar na sessão do Linux, o aplicativo aguarda a barra superior do sistema terminar de carregar antes de registrar o ícone na bandeja, evitando o surgimento de dois ícones e o fechamento inesperado logo após o login.
- **Recuperação automática do ícone da bandeja:** caso o painel superior do sistema seja reiniciado ou apresente falha temporária durante o registro do ícone, o Custódia permanece aberto e recria o ícone automaticamente.
- **Limpeza de atalhos duplicados no menu:** instalações via pacote `.rpm` não geram mais um segundo atalho redundante na pasta do usuário.

## [2.7.3] - 2026-11-19

### Melhorado
- **Encerramento limpo ao desinstalar ou atualizar:** ao remover ou atualizar o Custódia pelo terminal (inclusive no formato AppImage rodando na bandeja), qualquer instância aberta é encerrada automaticamente e o ícone da bandeja desaparece na hora.
- **Desligamento gracioso por sinais do sistema:** o aplicativo agora responde corretamente a comandos de encerramento do sistema, fechando a bandeja, a sincronização e o servidor local com segurança.
- **Notas de versão mais legíveis na aba Sobre:** o painel de atualizações formata títulos e marcadores com clareza e sem símbolos de código na tela.

## [2.7.2] - 2026-11-19

### Adicionado
- **Animação de 5 segundos ao buscar atualizações:** ao clicar em **Verificar atualizações** na aba **Sobre**, o aplicativo exibe uma barra de progresso animada com etapas claras e contagem regressiva enquanto consulta as novidades.
- **Verificação automática mensal por padrão:** por padrão global, o Custódia passa a procurar novas versões automaticamente uma vez por mês (a cada 30 dias), com opção nas **Configurações** para escolher entre mensal, quinzenal, semanal ou a cada inicialização.

## [2.7.1] - 2026-11-19

### Melhorado
- **Resumo das novidades antes de atualizar:** as notas da nova versão passam a aparecer diretamente na aba **Sobre** antes de iniciar o download.
- **Identificação da versão na bandeja:** o número da versão instalada agora é exibido ao passar o cursor sobre o ícone da bandeja do sistema.
- **Pasta limpa após instalar ou atualizar pelo terminal:** os instaladores `.rpm` e `.AppImage` baixados durante uma instalação ou atualização são armazenados temporariamente e removidos ao final do processo, preservando a opção de apenas baixar os pacotes quando desejado.

## [2.7.0] - 2026-11-18

### Adicionado
- **Janela única garantida:** tentar abrir o Custódia novamente enquanto ele já está ativo (inclusive minimizado na bandeja) traz a janela existente para frente em vez de abrir uma segunda cópia.
- **Atualizador integrado no aplicativo:** checagem manual e automática de novas versões, aviso discreto no topo da janela, notificação no sistema, barra de progresso no download, conferência de autenticidade e instalação assistida para pacotes `.rpm` e `.AppImage`.
- **Opções extras no instalador de terminal:** detecção automática da versão mais recente disponível e novo comando de atualização direta para instalações existentes.

### Alterado
- Exibição automática da versão em uso em todo o aplicativo sem depender de texto fixo.
- Remoção de menções antigas a pacote `.deb` na documentação do repositório.

## [2.6.0] - 2026-11-18

### Adicionado
- **Seleção múltipla em Meu Driver:** agora você pode selecionar vários arquivos de uma vez nos modos Lista e Grade usando caixas de seleção, `Ctrl + Clique`, `Shift + Clique` ou `Ctrl + A`, além de realizar ações em lote (baixar, mover para a Lixeira, restaurar, excluir permanentemente e enviar múltiplos arquivos).
- **Animações e cores por tipo de transferência:** envios em azul, downloads em verde e exclusões em vermelho na barra lateral e na Fila de Transferências, com exibição clara mesmo em arquivos rápidos e painel de detalhes ao clicar em cada item.
- **Notificações nativas com ação rápida:** avisos de sincronização integrados ao sistema com botão **Abrir pasta** direto no gerenciador de arquivos e filtro automático para evitar alertas repetidos quando nada foi alterado.
- **Compatibilidade ampliada com ambientes Linux:** funcionamento ajustado para KDE Plasma 5 e 6 (diálogos do sistema, atalhos, Dolphin e Lixeira), além de GNOME e COSMIC (Pop!_OS).
- **Instalador interativo no terminal:** novo assistente oficial para instalar, atualizar ou remover o aplicativo em pacotes `.rpm` e `.AppImage`, com verificação de autenticidade.
- **Fluxo dedicado para novas contas:** ao adicionar outra conta Google, o aplicativo solicita primeiro o arquivo de credenciais dessa conta antes de abrir a autorização no navegador.
- **Prévia visual e guia de configuração:** capturas de tela e passo a passo ilustrado para configurar o acesso ao Google Drive adicionados à documentação.

### Segurança
- **Proteção contra caminhos inválidos:** validação rigorosa no download, na leitura de subpastas e no envio de arquivos para impedir acessos fora das pastas sincronizadas.
- **Proteção reforçada na comunicação local:** verificação de origem nas requisições recebidas localmente e bloqueio automático de credenciais criadas para sites em vez de aplicativos de computador.

### Corrigido
- **Restauração confiável pela bandeja:** clicar no ícone da bandeja do sistema agora volta a exibir a janela minimizada corretamente no GNOME, Pop!_OS e KDE Plasma.
- **Atalhos e inicialização automática no AppImage:** geração correta do atalho no menu de aplicativos e na inicialização com o sistema ao usar a versão AppImage.
- **Limpeza visual das telas:** remoção de subtítulos repetitivos nos cabeçalhos de página.

## [2.5.0] - 2026-11-17

### Adicionado
- **Automação local integrada:** servidor local restrito ao próprio computador para consultas de status e integrações de uso pessoal.
- **Pré-visualização e miniaturas de arquivos:** cache local em disco e visualização integrada para imagens, áudio, vídeo, PDFs e documentos de escritório.
- **Otimização automática de memória:** limpeza periódica e manual do cache de mídia fora da tela para manter o consumo de memória baixo durante longos períodos na bandeja.
- **Suporte a múltiplas contas Google:** base preparada para conectar e alternar rapidamente entre diferentes contas na barra lateral.

### Melhorado
- Listagem mais rápida de pastas remotas e atualização imediata do espaço disponível após downloads ou exclusões.

## [2.4.0] - 2026-11-05

### Adicionado
- **Tela Fila de Transferências:** acompanhamento com barras de progresso em tempo real, cancelamento de tarefas em andamento e botão para limpar itens concluídos.
- **Detalhamento do armazenamento na nuvem:** exibição separada do espaço ocupado por Drive, Gmail / Fotos e Lixeira, com atalho rápido para gerenciar espaço.
- **Troca de idioma instantânea:** alternância entre Português (Brasil) e Inglês em tempo real sem precisar reiniciar o aplicativo.
- **Controle de escala da interface:** ajuste de zoom entre 100% e 300% nas configurações ou pelos atalhos `Ctrl +`, `Ctrl -` e `Ctrl 0`.

## [2.2.0] - 2026-10-18

### Adicionado
- **Navegador Remoto do Google Drive:** exploração das pastas na nuvem antes mesmo de escolher quais sincronizar ou baixar sob demanda.
- **Integração completa com a Lixeira:** visualização de itens apagados no Google Drive, permitindo restaurar arquivos ou excluí-los definitivamente direto pelo aplicativo.
- **Busca e alternância de visualização:** pesquisa rápida por nome ou caminho e troca entre visualização em Grade e Lista.

### Melhorado
- Conversão e exportação amigável de arquivos Google Docs, Planilhas e Apresentações durante o download.

## [2.0.0] - 2026-10-02

### Alterado
- Reorganização estrutural do aplicativo para separar motor de sincronização, serviço local e interface desktop.
- Nova interface desktop nativa para Linux com tema escuro unificado e suporte a sessões gráficas modernas e tradicionais.

### Adicionado
- **Ícone persistente na bandeja do sistema:** permite fechar a janela mantendo a sincronização ativa em segundo plano.
- **Inicialização automática com o sistema:** opção para abrir o aplicativo junto com a sessão do usuário no Linux.
- **Tela Sobre:** exibição de versão, desenvolvedor e atalho para o repositório oficial.

## [1.5.0] - 2026-09-12

### Adicionado
- **Dois modos de sincronização por pasta:** escolha entre **Bidirecional** (mudanças dos dois lados) ou **Somente envio / Backup** (do computador para a nuvem).
- **Regras de exclusão de arquivos:** suporte a filtros para ignorar arquivos temporários, caches e padrões definidos pelo usuário.
- **Verificação de integridade e conflitos:** comparação automática do conteúdo antes de sobrescrever alterações locais ou na nuvem.

### Corrigido
- Prevenção de reenvios desnecessários logo após concluir o download de um arquivo atualizado.

## [1.2.0] - 2026-08-22

### Adicionado
- **Monitoramento contínuo de pastas:** detecção automática de arquivos criados, modificados ou movidos no computador, agrupando edições rápidas em sequência.
- **Histórico local persistente:** registro seguro das contas, pastas mapeadas e estado de cada arquivo entre reinicializações.
- **Tela Logs de Atividade:** consulta detalhada do que foi sincronizado, com filtros por tipo de evento e pesquisa por texto.

## [1.0.0] - 2026-08-01

### Adicionado
- **Lançamento inicial do Custódia para Linux:** primeiro aplicativo desktop para sincronizar pastas locais com o Google Drive.
- **Autenticação segura com credenciais próprias:** conexão direta com a conta Google do usuário e armazenamento protegido das chaves de acesso no computador.
- **Mapeamento inicial de pastas:** vinculação de diretórios locais do Linux a pastas do Google Drive.
