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
    LosslessScalingSettings.cs  modelo do Settings.xml do LS (XmlSerializer)
  Services/                   lógica sem UI
    GamepadService.cs         polling XInput, eventos de botão e combos
    XInputInterop.cs          P/Invoke do XInput, enum GamepadButton
    HotkeyInputSender.cs      SendInput, foreground, diagnóstico de janelas
    GameWindowFinder.cs       escolhe a janela do jogo para devolver o foco
    LosslessScalingProcessService.cs  achar/abrir/reiniciar o LS, detectar "escalando", link da Steam
    LosslessScalingSettingsService.cs Settings.xml + cópia local + sincronização
    AppConfigService.cs       lê/grava config.json ao lado do exe
    StartupRegistration.cs    "Iniciar com o Windows" (HKCU Run)
    Log.cs                    log em Data\app.log + watchdog
  Overlay/                    tudo que é tela
    Theme.xaml                tokens (cores, fontes) e estilos do design system
    OverlayWindow.xaml(.cs)   janela, templates de linha, navegação por controle e mouse
    SettingRow.cs             modelo de linha (uma classe por tipo de controle)
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

**Para adicionar uma configuração** em uma categoria existente: crie a linha em `RebuildRows()`
(`OverlayWindow.xaml.cs`) e informe a `Section`/`Hint`/`EnabledWhen` se precisar. Não crie XAML novo para tipos que já existem.

**Para adicionar um tipo novo de controle**: nova subclasse de `SettingRow` + flag `IsXxx` + trecho no `RowTemplate`
+ estilo em `Theme.xaml`, seguindo o componente equivalente do design system.

Regras visuais:
- Cores/fontes só via recursos de `Theme.xaml` (`AccentBrush`, `FocusBrush`, `Text2Brush`...). Não escreva hex solto em telas novas,
  exceto valores já definidos no design system.
- Estado de foco = anel `FocusBrush` (via `DataTrigger` em `IsSelected` do `ListBoxItem`). Todo controle novo precisa ter foco visível.
- Linhas indisponíveis ficam com opacidade 0.42 e são puladas pela navegação (`EnabledWhen`).
- Toggle pequeno 52×30 (linhas), grande 68×38 (card mestre). Botões de dica (A/B/X/Y) usam `BtnA/B/X/YBrush`.
- Textos em português; rótulos curtos; dicas (`Hint`) só quando a opção não é autoexplicativa.

## Controle: regras que não podem quebrar

Mapeamento no overlay: D-Pad ↑↓ move a seleção, ←→ altera o valor, **A** confirma/aumenta, **B** fecha, **X** reinicia o LS,
**Y** salva, **LB/RB** troca de perfil. Combos globais (`config.json`): `ToggleCombo` = View+L3, `OverlayCombo` = View+Menu.

1. **Combos disparam uma vez por aperto** (latch até soltar). Segurar não pode repetir a ação.
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

## Portabilidade e dados

- **Tudo fica ao lado do `.exe`**: `config.json`, `Data\app.log`, `Data\LosslessScaling.Settings.xml` (cópia local das configs do LS).
  Nada em `%AppData%`/`%LocalAppData%` (exceto a leitura única do local antigo, para migração).
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

Monitoramento (Afterburner), Sistema, Perfis, Sobre, chip com o nome do jogo, seletor de posição 3×3, repetição ao segurar ←/→
e passos maiores com LT/RT. Ao implementar, siga a seção "Overlay" acima e atualize este guia e o README.
