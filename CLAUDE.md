# Game Plus — guia do projeto

App de bandeja + overlay para Windows, **operado principalmente por controle (Xbox)**, pensado para o ROG Xbox Ally X
(Xbox fullscreen experience) e PCs gamer. Hoje controla o Lossless Scaling (LS); a intenção é acrescentar outras
funcionalidades (monitoramento, sistema, perfis, sobre), por isso o app **não** é "o app do Lossless".

Toda decisão de UI/UX parte de uma pergunta: **funciona bem só com o controle?** Mouse é suporte, não o foco.

## Stack

- .NET 10, WPF (overlay) + WinForms `NotifyIcon` (bandeja), `net10.0-windows`, `win-x64`, `Nullable` e `ImplicitUsings` ligados.
- Sem dependências NuGet. XInput e Win32 via P/Invoke.
- Design system: `C:\Users\paulo\source\repos\CentralDeJogoOverlayDesign\overlay-design.html` (+ `app-icon.svg`).
  **É a fonte da verdade visual.** Não invente cores, tamanhos ou componentes: se faltar algo, atualize o design system primeiro.

## Comandos

```bash
# Compilar (feche o app antes: taskkill //F //IM GamePlus.exe)
dotnet build src/GamePlus/GamePlus.csproj -c Release

# Publicar a versão portátil em publish/ (pasta autocontida, ~173 MB)
dotnet publish src/GamePlus -p:PublishProfile=Portable
```

Saída do build: `src/GamePlus/bin/Release/net10.0-windows/win-x64/GamePlus.exe`.
Abrir já com o overlay: `GamePlus.exe --overlay`.

## Fluxo de trabalho

- Ao **finalizar cada tarefa**: encerrar o `GamePlus.exe` em execução, compilar sem erros, **rodar o publish** e avisar no resumo final.
- **Commit só quando o usuário pedir.** Mensagens em inglês, no imperativo, com corpo curto explicando o porquê.
- Verifique mudanças visuais com screenshot do overlay (janela 920×580 centralizada), não só compilando.
- Se não conseguir testar algo (ex.: controle físico, Ally), diga isso explicitamente no resumo.
- Idioma: **interface, README e conversa em português**; código, comentários e commits em inglês.

## Estrutura

```
GamePlus.slnx
CLAUDE.md                     este guia
README.md                     documentação para o usuário final
publish/                      saída do publish (ignorada pelo git)
src/GamePlus/
  App.xaml(.cs)               ponto de entrada: bandeja, handlers globais de erro, orquestra serviços e overlay
  app.ico / app.manifest      ícone (16/32/48/256) e manifest
  Models/
    AppConfig.cs              config.json (combos, intervalos, caminho do LS...)
    MonitoringConfig.cs       Data\monitoring.json (métricas ligadas, GPU, posição 0-8, tamanho) + MetricKeys
    AppSettings.cs            Data\settings.json (tema, idioma, opacidade, escala, início, clique na bandeja, atalhos, pasta de capturas)
    LosslessScalingSettings.cs  modelo do Settings.xml do LS (XmlSerializer)
  Services/                   lógica sem UI
    GamepadService.cs         polling XInput, eventos de botão e combos
    XInputInterop.cs          P/Invoke do XInput, enum GamepadButton
    HotkeyInputSender.cs      SendInput, foreground, diagnóstico de janelas
    GameWindowFinder.cs       escolhe a janela do jogo para devolver o foco
    MonitoringConfigService.cs  lê/grava Data\monitoring.json
    AppSettingsService.cs     Data\settings.json + evento Changed (tudo que é global reage a ele)
    KeyboardShortcutService.cs  atalhos globais de teclado (RegisterHotKey, vários slots): abrir o overlay, capturar a tela
    ScreenshotService.cs      captura todos os monitores em PNG na pasta configurada
    Localization.cs           Loc: textos da UI por idioma (JSON), indexador com atualização ao vivo
    Monitoring/               monitoramento (Afterburner + RTSS), sem UI
      MahmReader.cs           sensores do Afterburner (memória compartilhada MAHM, somente leitura)
      RtssClient.cs           FPS do jogo + escrita do OSD no slot próprio do RTSS
      RamInfoProvider.cs      velocidade da RAM via tabelas SMBIOS (sem WMI/admin)
      MonitoringService.cs    loop de 500 ms: lê, monta o texto com tags do RTSS e escreve; expõe Status
    LosslessScalingProcessService.cs  achar/abrir/reiniciar o LS, detectar "escalando", link da Steam
    LosslessScalingSettingsService.cs Settings.xml + cópia local + sincronização
    AppConfigService.cs       lê/grava config.json ao lado do exe
    StartupRegistration.cs    "Iniciar com o Windows" (HKCU Run)
    Log.cs                    log em Data\app.log + watchdog
  Overlay/                    tudo que é tela
    Theme.xaml                estilos do design system (só DynamicResource para cores)
    Themes/Colors.Dark.xaml   pincéis do tema escuro; Colors.Light.xaml tem as MESMAS chaves
    ThemeManager.cs           troca o dicionário de cores em execução e aplica a opacidade do painel
    OverlayWindow.xaml(.cs)   janela, templates de linha, navegação por controle e mouse
    SettingRow.cs             modelo de linha (uma classe por tipo de controle)
  Languages/                  en.json, pt-BR.json (embutidos e copiados ao lado do exe; ver "Idiomas")
  Properties/PublishProfiles/Portable.pubxml
```

Regra de camadas: `Services` não conhece WPF/`Overlay`; `Overlay` usa `Services` e `Models`; `App` liga tudo.
Ao surgir um novo domínio (ex.: monitoramento), crie `Services/<Dominio>...Service.cs` e, se tiver tela própria, uma
categoria no overlay — não misture com as classes do Lossless.

## Overlay: como é montado

O overlay é uma lista de **linhas** (`SettingRow`) dentro de um `ListBox`. Cada linha é um `DataTemplate`
escolhido pelo tipo (`RowTemplate`, `MasterTemplate`, `ItemTemplate` em `OverlayWindow.xaml`).

Tipos de linha (`Overlay/SettingRow.cs`):

| Classe | Controle | Uso |
|---|---|---|
| `ActionRow` | card mestre com toggle grande | ligar/desligar; estado por `MasterState` |
| `EnumSettingRow` | segmentado (≤3 opções) ou stepper `‹ valor ›` | valores de enum |
| `BoolSettingRow` | toggle | liga/desliga |
| `IntSettingRow` / `DoubleSettingRow` | stepper ou slider (`slider: true`) | números com min/max/passo |
| `PathSettingRow` | stepper (`‹ valor ›`) | pasta: direita/A chama uma ação (`Increment`, ex. abrir diálogo), esquerda restaura o padrão (`Decrement`) |

**Para adicionar uma configuração** em uma categoria existente: crie a linha em `RebuildRows()`
(`OverlayWindow.xaml.cs`) e informe a `Section`/`Hint`/`EnabledWhen` se precisar. Não crie XAML novo para tipos que já existem.

**Para adicionar um tipo novo de controle**: nova subclasse de `SettingRow` + flag `IsXxx` + trecho no `RowTemplate`
+ estilo em `Theme.xaml`, seguindo o componente equivalente do design system.

Regras visuais:
- Cores só via pincéis dos dicionários de tema (`AccentBrush`, `FocusBrush`, `Text2Brush`...) com **`DynamicResource`** (com `StaticResource` a
  cor não acompanha a troca de tema). Não escreva hex solto em telas novas. Cor nova = chave nova **nos dois** `Colors.*.xaml`.
- Estado de foco = anel `FocusBrush` (via `DataTrigger` em `IsSelected` do `ListBoxItem`). Todo controle novo precisa ter foco visível.
- Linhas indisponíveis ficam com opacidade 0.42 e são puladas pela navegação (`EnabledWhen`).
- Toggle pequeno 52×30 (linhas), grande 68×38 (card mestre). Botões de dica (A/B/X/Y) usam `BtnA/B/X/YBrush`.
- Nenhum texto fixo na UI: tudo por `Loc.T("chave")` (código) ou `{Binding [chave], Source={x:Static svc:Loc.Instance}}` (XAML), com a chave nos dois JSON. Rótulos curtos (o inglês costuma ser mais longo: confira o segmentado); dicas (`Hint`) só quando a opção não é autoexplicativa.

## Controle: regras que não podem quebrar

Mapeamento no overlay: D-Pad ou analógico esquerdo (virtual D-Pad em `GamepadService`, com histerese e um eixo por vez) ↑↓ move a seleção com volta circular e repetição ao segurar, ←→ altera o valor (em campos com mais de 2 valores, segurar só pré-visualiza: `Preview`/`Commit` na linha, aplicado ao soltar; A e teclado aplicam na hora), **A** confirma/aumenta, **B** fecha, **X** reinicia o LS,
**Y** salva, **LB/RB** troca de categoria, **LT/RT** troca de perfil do LS (os gatilhos viram botões `LeftTrigger`/`RightTrigger` em `GamepadService`). Combos globais (`config.json`): `ToggleCombo` = Menu+L3, `OverlayCombo` = View+Menu, `ScreenshotCombo` = View+R3 — cada um usa um botão de cada lado (polegar esquerdo + direito), View+L3 foi trocado por Menu+L3 por serem os dois do mesmo lado (mesmo polegar) e difíceis de segurar juntos.

1. **Combos disparam uma vez por aperto** (latch até soltar). Segurar não pode repetir a ação. A detecção tolera até 90 ms sem ver o combo (`ComboGraceMs` em `GamepadService`), para uma leitura perdida (Bluetooth, jitter de polling) não resetar a contagem de um combo que está sendo segurado corretamente; cada início/disparo/soltura-antes-de-disparar de um combo vai para `Data\app.log`.
2. **Fechar o overlay só depois de soltar os botões** (`CloseAfterReleaseAsync`): o jogo recupera o foco na hora e um botão ainda
   pressionado chegaria a ele como aperto novo. Qualquer novo fluxo que feche o overlay deve usar esse método.
3. **Devolver o foco à janela do jogo** capturada ao abrir (`GameWindowFinder.Find()`), ignorando Explorer, bandeja, este app e o LS.
4. **Não travar o foco**: a re-captura de foco em `Deactivated` é limitada a 2 s / 3 tentativas. Nunca capturar foco sem limite.
5. Todo fluxo tem que ser possível só com o controle (sem depender de clique ou teclado).

## Lossless Scaling: particularidades

- `Settings.xml` (`%LocalAppData%\Lossless Scaling\`) é validado por `XmlSerializer` com enums estritos:
  **só grave valores que existem nos enums reais** (lista em `RebuildRows`); um valor inválido impede o LS de abrir.
  `Save` cria `Settings.xml.bak`.
- O LS só lê o `Settings.xml` ao iniciar: salvar reinicia o LS (`RestartIfRunning`, com `-StartMinimized`) se `AutoRestartOnSave`.
- O ligar/desligar simula a hotkey configurada no próprio LS (`SendInput`); o app lê a hotkey do `Settings.xml` a cada uso.
- **"Escalando" ≠ "processo aberto"**: o LS aberto fica ocioso. Escalando, ele tem uma janela visível de classe `LosslessScaling`
  (`LosslessScalingProcessService.IsScaling`). Essa janela **some quando o jogo perde o foco** (inclusive quando o overlay abre), embora a
  geração continue. Por isso o overlay lê o estado **antes** de tomar o foco (`InitialScaling`) e mantém esse valor enquanto aberto.
- Ligar tem contagem de 3 s (cancelável com A) e, se o LS estiver fechado, abre o LS primeiro; desligar é imediato e não fecha o overlay.
- Sem o LS instalado, o card vira alerta com botão para a página da Steam (`steam://store/993090`, com fallback para a web).

## Monitoramento (Afterburner + RTSS)

Categoria "Monitoramento": `MonitoringService` (criado em `App`, roda em loop próprio, então o OSD funciona com o overlay fechado)
lê os sensores do Afterburner e escreve o texto num slot de OSD do RTSS. Tudo sem administrador.

- **Memórias compartilhadas**: `MAHMSharedMemory` (só leitura; assinatura 0x4D41484D) e `RTSSSharedMemoryV2` (leitura/escrita;
  assinatura 0x52545353, versão 2.21 aqui). Nomes dos sensores vêm do Afterburner (`GPU2 temperature`...); `FLT_MAX` = sem dado.
  Assinatura/versão desconhecida = "indisponível", nunca exceção.
- **Slot do OSD**: o slot 0 é do Afterburner; usamos o primeiro livre a partir do 1 com dono `GamePlus` e liberamos ao desligar/sair.
  Respeitar `dwBusy` ao escrever e incrementar `dwOSDFrame`. Gráficos de FPS/frametime são objetos embutidos (`<OBJ=offset>`).
- **Posição e tamanho** vão no texto por tags do RTSS (`<Pn><L0>` e `<S=percentual>`; `<S=0>` esconde o texto); validar visualmente ao mudar.
- **Estado no overlay**: `MonitoringStatus` (Afterburner/RTSS conectados, GPUs, métricas disponíveis, jogo). Linhas sem sensor ficam
  esmaecidas com o motivo (`HintWhen`). Com Afterburner/RTSS fechados, ligar o cartão os inicia (`AfterburnerLauncher`, feedback "Abrindo o MSI Afterburner…" como no LS) e só então liga a exibição; sem o Afterburner instalado o cartão vira alerta sem interruptor.
- FPS é o do jogo (RTSS), ignorando `GamePlus.exe` e `LosslessScaling.exe`.
- **Nomes de sensor dependem do PC** (aprendido no Ally): o Afterburner só numera os sensores de GPU (`GPU1 temperature`,
  `GPU2 usage`...) quando há mais de uma GPU; com uma só (Ally) os nomes são `GPU temperature`, `GPU usage`... Por isso `Gs()` cai
  para o nome sem número. Mesmo assim, `core clock`, `memory usage` e `power` não apareciam no Ally por outras diferenças de nome,
  então `MahmReader` também indexa os sensores de GPU pelo **id de origem estável** (entrada @1320: temperatura 0, uso 48, clock 32,
  VRAM 49, consumo 97, tensão 64) com o índice de GPU (@1316) e cria o apelido `GPU{n} {sensor}`. Não confie só em nomes de sensor.
- **Diagnóstico**: na primeira leitura o `MahmReader` grava em `Data\app.log` a lista completa (`MAHM sensors: nome|unidade|gpu=|id=|valor`).
  Peça esse log ao investigar "métrica não aparece" em outro hardware.
- **Hardware AMD não tem tensão de GPU** (o Afterburner não expõe): a linha fica esmaecida com "Indisponível neste hardware". É esperado.
- Unidades: RAM e VRAM em **MB** (o sensor `RAM usage` já vem em MB); velocidade da RAM em MT/s via SMBIOS (`RamInfoProvider`).
- **Largura dos gráficos** (`GraphWidth`, 8-48 caracteres, padrão 32) vai na largura negativa do objeto de gráfico do RTSS.
- **Cores por grupo** (GPU, CPU, RAM, FPS, Frametime; `Colors[grupo] = {Label, Value}` em `monitoring.json`, "RRGGBB") vão como `<C=RRGGBB>`; a tela escolhe o grupo e edita o rótulo/valor dele. Presets nomeados (`ColorPresets`) mais
  sliders R/G/B para qualquer cor; um valor que não seja hex de 6 dígitos volta ao padrão ao carregar.
## Configurações do app, temas, idiomas e escala

Categoria "Configurações" (`BuildSettingsRows`), gravada em `Data\settings.json` por `AppSettingsService`. Cada mudança chama `Save()`, que dispara `Changed`:
`App` reage (tema, opacidade, idioma, comportamento da bandeja) e o `OverlayWindow` também (escala, refazer as linhas se o idioma mudou). Linhas novas de config global seguem esse fluxo.

- **Tema** (`dark`/`light`): `ThemeManager.Apply` troca o `Colors.*.xaml` mesclado em `Application.Resources`; a opacidade do painel (50-100%, padrão 95) só mexe no alfa de `PanelBrush`, definido no próprio `Application.Resources` para sobrepor o do tema. Fora dos temas: ícone, botões A/B/X/Y de texto fixo, glow verde.
- **Idiomas**: `Loc` (`Services/Localization.cs`). Arquivos planos `{ "_name": "...", "_code": "...", "chave": "texto" }`. `en` e `pt-BR` vão embutidos no exe *e* são copiados para `Languages\` ao lado dele; qualquer `Languages\*.json` ali entra sozinho na lista (é assim que se adiciona um idioma, sem recompilar). Arquivo parcial vale: chave ausente cai para o inglês e depois para a própria chave. `Language` nulo segue o idioma do Windows. Trocar de idioma refaz as linhas (`RebuildRowsKeepingSelection`) e o menu da bandeja. Rótulos que dependem de dado em execução (nomes de posição, presets) usam chaves (`pos.N`, `preset.<chave>`).
- **Escala** (`UiScale`, 75-150%): `LayoutTransform` no `Root` (920x580 de projeto) e janela com `SizeToContent`; `ApplyScale` limita ao tamanho da área de trabalho e recentraliza.
- **Itens do menu lateral** (`HiddenFeatures`): a barra e a lista de "Itens do menu lateral" nascem do registro `_features` (`BuildFeatures` em `OverlayWindow`). Funcionalidade nova = valor em `Category` + uma entrada no registro (nome, ícone, `Active` para a bolinha, `Deactivate`) + construtor de linhas; barra, opção de ocultar e a regra "pelo menos um visível" vêm de graça. Ocultar chama `Deactivate` (LS: manda a hotkey se estiver escalando; Monitoramento: `Enabled=false`) e reexibir chama de novo, então o item sempre volta desligado. Configurações não é ocultável.
- **Início**: padrão é abrir a janela ao iniciar (`StartInTray` = só bandeja; `--overlay` sempre abre). **Bandeja**: um clique abre (`TrayClickOpens`); desligado, só duplo clique.
- **Atalho de teclado** (`KeyboardShortcut`, padrão `Ctrl+Alt+G`, vazio = desligado): `KeyboardShortcutService` registra um hotkey global numa janela só de mensagens (não depende de foco) e `App` liga o disparo a `ToggleOverlay`, como o combo do controle. `Apply` roda no início e a cada `Changed`. A linha das Configurações é uma lista de atalhos prontos (assim dá para escolher só com o controle); qualquer outra combinação (`Ctrl|Alt|Shift|Win` + nome de tecla do `System.Windows.Forms.Keys`) pode ser escrita à mão em `settings.json`. Exige ao menos um modificador. Se outro programa já usa a combinação, `Failed(id)` fica true e a linha mostra o aviso. `KeyboardShortcutService` tem um slot por atalho (`SlotOverlay`, `SlotScreenshot`); `App.ApplyShortcuts()` reaplica os dois a cada `Changed`.
- **Captura de tela** (`ScreenshotFolder` vazio = `%userprofile%\Pictures\Screenshots\GamePlus`; `ScreenshotShortcut`, padrão `Ctrl+Alt+PrintScreen`): `ScreenshotService.Capture()` acha a janela em primeiro plano (`HotkeyInputSender.GetForegroundWindow()`) e captura só o monitor dela (`Screen.FromHandle`, com `Screen.PrimaryScreen` como reserva) num `Bitmap`, salvo como `GamePlus_aaaa-MM-dd_HH-mm-ss.png`; disparado pelo atalho de teclado e pelo combo do controle (`GamepadService.ScreenshotComboTriggered`, `ScreenshotCombo` em `config.json`, padrão View+R3). A pasta padrão é uma exceção deliberada à regra de portabilidade abaixo: fica no `Pictures\Screenshots` do Windows (mesma convenção de outras ferramentas de captura), não ao lado do exe. A linha "Pasta para salvar" (`PathSettingRow`) abre um `FolderBrowserDialog` dono da própria janela do overlay (`Win32Window` + `WindowInteropHelper`, necessário porque o overlay é `Topmost`); direita/A escolhe uma pasta, esquerda volta ao padrão. Clicar no balão de "captura salva" na bandeja abre a pasta com o arquivo selecionado (`App.OpenScreenshotFolder`, ligado só àquele balão via `_screenshotBalloonPath`).

## Instância única

`App.OnStartup` cria o evento nomeado `Local\GamePlus.Show`. Quem o cria é a instância principal (escuta o sinal numa thread e chama `ShowOverlay`); uma segunda execução do exe só sinaliza e encerra, abrindo a janela como "Abrir configurações".

## Portabilidade e dados

- **Tudo fica ao lado do `.exe`**: `config.json`, `Data\app.log`, `Data\LosslessScaling.Settings.xml` (cópia local das configs do LS).
  Nada em `%AppData%`/`%LocalAppData%` (exceto a leitura única do local antigo, para migração). **Exceção deliberada**: capturas de
  tela vão por padrão em `%userprofile%\Pictures\Screenshots\GamePlus` (não portátil), porque é onde o usuário espera achar
  imagens, igual outras ferramentas de captura do Windows — configurável para qualquer pasta em Configurações.
- Sincronização: vence o arquivo modificado por último entre o `Settings.xml` do LS e a cópia local (`SyncWithLocalCopy` no início).
  Copiar a pasta para outro PC aplica as mesmas configs. Novas funcionalidades que tenham configurações devem seguir o mesmo modelo
  (arquivo dentro de `Data\`, copiável com a pasta).
- Tolerar pasta somente leitura: capture `IOException`/`UnauthorizedAccessException` e rode com padrões.
- **Publicar como pasta autocontida** (`PublishSingleFile=false`). O arquivo único falha no ROG Xbox Ally X (o overlay nunca aparece), com
  ou sem compressão. **Não** ligue `InvariantGlobalization` (quebra bindings do WPF).
- O ícone vem de `app.ico` (exe, bandeja e cabeçalho do overlay usam a mesma arte do design system).

## Erros e diagnóstico

- Handlers globais em `App.OnStartup` (WinForms, Dispatcher, AppDomain, Task) gravam em `Data\app.log` e não derrubam o app.
- Use `Log.Info` em pontos de decisão de fluxos difíceis de depurar em outra máquina e `Log.Watchdog` para distinguir travamento de crash.
- Logging nunca pode lançar exceção. Erros visíveis ao usuário vão como balão da bandeja ou na barra de status do overlay.

## Padrões de código

- Serviços simples, sem injeção de dependência; instâncias criadas em `App` e passadas por construtor.
- P/Invoke fica junto de quem usa (privado), ou em `XInputInterop`/`HotkeyInputSender` quando compartilhado.
- Trabalho bloqueante (enumerar processos/janelas, disco) fora da thread de UI (`Task.Run`); atualização de UI na thread do Dispatcher.
- Estados de tela por enum explícito (ex.: `MasterState`) em vez de vários booleanos soltos.
- Comentários explicam o **porquê** (limitação do LS, do Windows, do Ally), não o quê.
- Não renomeie nada que se refira ao Lossless em si (`LosslessScaling.exe`, classe de janela `LosslessScaling`, `Settings.xml`).

## Armadilhas já encontradas

- Encerre o `GamePlus.exe` antes de compilar/publicar, senão o build falha por arquivo em uso.
- Ambiente é Git Bash sem Python; edite com as ferramentas de edição ou PowerShell. Scripts `.ps1` com acentos precisam de cuidado de
  codificação (prefira `Edit`).
- Recursos WPF: `FontFamily` da janela deve ser literal (`StaticResource` do próprio dicionário mesclado não resolve).
- `Timer` é ambíguo entre WinForms/Threading e `Application` entre WPF/WinForms: use nomes qualificados.
- A janela do overlay é transparente e sem borda: capturas de tela mostram o jogo por trás; isso é esperado.
- Abrir o overlay pelo menu da bandeja tira o foco do jogo antes da leitura do estado: o card pode mostrar "Desligado" com a geração ativa
  (limitação conhecida, pendente).

## Roadmap (ainda não implementado — definido no design system)

Sistema, Perfis, Sobre, chip com o nome do jogo e passos maiores. Ao implementar, siga a seção "Overlay" acima e atualize este guia e o README.
