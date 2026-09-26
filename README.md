# Game Plus

App de bandeja para Windows que permite **ligar/desligar o Lossless Scaling** e
**editar suas configurações de geração de quadros** usando só o controle do Xbox,
sem precisar de teclado/mouse — pensado para quem joga em modo Xbox Game Bar
fullscreen experience.

## Por que não é um "widget" da Xbox Game Bar?

A Xbox Game Bar Widget SDK oficial é baseada em UWP e, para abrir de dentro da
overlay nativa (botão Xbox do controle → lista de widgets), historicamente
depende de empacotamento MSIX e, no caso comum, de sideload via **Modo de
Desenvolvedor do Windows** — além de um sandbox UWP mais restrito para coisas
como escrever no `Settings.xml` de outro app ou simular teclas globais.

Optamos, junto com você, por um **app overlay próprio** (WPF/.NET, Win32
comum): mesma experiência de uso (abre por cima do jogo, 100% navegável pelo
D-Pad/controle), mas sem sandbox, sem precisar de Modo de Desenvolvedor, e bem
mais simples de manter. Se no futuro fizer sentido migrar para um widget UWP
real, a lógica de controle/settings deste projeto pode ser reaproveitada.

## Como funciona

- **Ligar/desligar (View + L3 no controle, por padrão):** o app lê o hotkey
  configurado dentro do próprio Lossless Scaling (`Settings.xml → Hotkey` /
  `HotkeyModifierKeys`, hoje `F12`) e simula exatamente essa tecla via
  `SendInput`. É o mesmo mecanismo que o Lossless Scaling já espera, então
  funciona não importa qual janela esteja em foco.
- **Abrir/fechar o overlay de configurações (View + Menu, por padrão):** mostra
  uma janela borderless, sempre no topo, com os principais parâmetros de
  geração de quadros e escalonamento do perfil ativo, navegável só com o
  D-Pad/A/B/X/Y/LB/RB.
- **Salvar (Y):** grava as mudanças de volta no `Settings.xml` do Lossless
  Scaling. Se o Lossless Scaling estiver rodando, por padrão o companion o
  reinicia automaticamente para garantir que as novas configurações sejam
  aplicadas (isso causa uma interrupção breve da geração de quadros — pode ser
  desativado em `config.json`, ficando só a opção de reiniciar manualmente com
  **X**).

## Controles no overlay

| Botão | Ação |
|---|---|
| D-Pad ou analógico esquerdo Cima/Baixo | Mover seleção entre os campos (segurar repete; passa do último para o primeiro e vice-versa) |
| D-Pad ou analógico Esquerda | Diminuir/ciclar valor (segurar percorre os valores e aplica ao soltar, em campos com mais de 2 opções) |
| D-Pad ou analógico Direita / A | Aumentar/ciclar valor (A aplica na hora) |
| LB / RB | Trocar de categoria (Lossless Scaling / Monitoramento / Configurações) |
| LT / RT | Trocar de perfil do Lossless Scaling (quando há mais de um) |
| Y | Salvar no Settings.xml |
| X | Reiniciar o Lossless Scaling manualmente |
| B / View+Menu de novo | Fechar o overlay |

Os combos de abrir o overlay e ligar/desligar foram escolhidos justamente para
**não colidir** com A/B/X/Y/LB/RB/D-Pad, que são usados na navegação do
overlay.

## Monitoramento (MSI Afterburner + RTSS)

A categoria **Monitoramento** mostra sobre o jogo temperatura, uso, frequência,
consumo e tensão de CPU/GPU, RAM e VRAM, FPS atual, média (AVG) e frametime,
com ou sem gráfico. Você escolhe a GPU, o canto da tela (grade 3x3), o tamanho
do texto, a largura dos gráficos e as cores de cada grupo (GPU, CPU, RAM, FPS, frametime), rótulo e valores separados (cores prontas ou qualquer cor
por sliders R/G/B), tudo pelo controle.
(Sensores que o hardware não tem, como a tensão de GPUs AMD, aparecem esmaecidos.)

- Requer o **MSI Afterburner** e o **RTSS** (o RTSS vem com o Afterburner). Se estiverem fechados, ligar o monitoramento abre os dois.
  O Game Plus lê os sensores do Afterburner e desenha o texto pelo RTSS, sem
  precisar de administrador e sem editar arquivos do Afterburner.
- Para não duplicar informações, desligue o OSD do próprio Afterburner.
- Sensores que o hardware não expõe (ex.: tensão da CPU em alguns Ryzen) aparecem
  esmaecidos como "indisponível neste hardware".
- O FPS é o do jogo (o RTSS não mede os quadros gerados pelo Lossless Scaling).
- As preferências ficam em `Data\monitoring.json`, ao lado do `.exe`.

## Configurações do app

A categoria **Configurações** (terceiro item da barra lateral) guarda as preferências do próprio Game Plus em `Data\settings.json`:

- **Tema:** escuro (padrão) ou claro.
- **Opacidade do painel:** 50 a 100% (só o fundo do painel; os textos continuam nítidos).
- **Escala da interface:** 75 a 150%. Se não couber na tela (ex.: handheld 720p), é reduzida automaticamente.
- **Itens do menu lateral:** mostrar/ocultar Lossless Scaling e Monitoramento (Configurações sempre aparece; pelo menos um item precisa ficar visível). Ocultar também desliga a função (ex.: a geração de quadros), e um item exibido de novo volta desligado. Funcionalidades novas entram nessa lista automaticamente.
- **Idioma:** Português (Brasil) e English; por padrão segue o idioma do Windows.
- **Iniciar com o Windows** e **Ao iniciar** (abrir a janela, que é o padrão, ou só na bandeja).
- **Abrir com um clique na bandeja** (desligado: só o clique duplo abre).

### Adicionar um idioma

Os idiomas são arquivos JSON na pasta `Languages\` ao lado do `.exe` (`en.json` e `pt-BR.json` já vêm lá). Para criar outro, sem recompilar:

1. Copie `en.json` para, por exemplo, `es.json`.
2. Mude `_code` (ex.: `"es"`) e `_name` (nome que aparece na lista, ex.: `"Español"`) e traduza os valores (mantenha as chaves e os `{0}`).
3. Abra o app: o idioma aparece em Configurações → Idioma. Traduções parciais funcionam: o que faltar aparece em inglês.

## Build e execução

Requer .NET 10 SDK (Windows) com workload de desktop.

```bash
dotnet build src/GamePlus/GamePlus.csproj -c Release
```

O executável fica em
`src/GamePlus/bin/Release/net10.0-windows/win-x64/GamePlus.exe`.

### Versão portátil (sem instalar o .NET)

```bash
dotnet publish src/GamePlus -p:PublishProfile=Portable
```

Gera a pasta `publish/` (~173 MB) com o runtime embarcado. Copie a **pasta inteira**
para outro PC e abra o `GamePlus.exe`; `config.json`, `Data\` (cópia
das configs do Lossless Scaling) e `Data\app.log` ficam ao lado do `.exe`.

Não use arquivo único (`PublishSingleFile`): no ROG Xbox Ally X o overlay não abria
com ele, mesmo sem compressão. Alternativa menor (~300 KB), que exige o
.NET Desktop Runtime 10 instalado: `-p:SelfContained=false -p:PublishSingleFile=true`.

Rode-o normalmente (por padrão ele abre a janela; nas Configurações dá para iniciar só na bandeja). O app roda em instância única: abrir o .exe de novo só abre a janela do que já está rodando. Pelo menu
da bandeja dá pra:

- Abrir as configurações (também com um clique no ícone, ou duplo clique se preferir; ver Configurações)
- Ativar "Iniciar com o Windows"
- Sair

## Configuração (`config.json`, na mesma pasta do `.exe`)

Criado automaticamente na primeira execução, com os padrões:

```json
{
  "PollIntervalMs": 16,
  "ComboHoldMs": 350,
  "ComboCooldownMs": 600,
  "ToggleCombo": ["Back", "LeftThumb"],
  "OverlayCombo": ["Back", "Start"],
  "LosslessScalingExePath": null,
  "AutoRestartOnSave": true
}
```

- `ToggleCombo` / `OverlayCombo`: nomes de botões XInput (`A`, `B`, `X`, `Y`,
  `Back`, `Start`, `LeftShoulder`, `RightShoulder`, `LeftThumb`, `RightThumb`,
  `DPadUp`, `DPadDown`, `DPadLeft`, `DPadRight`). Precisam ser mantidos juntos
  por `ComboHoldMs` para disparar.
- `LosslessScalingExePath`: defina se o app não conseguir localizar sozinho o
  `LosslessScaling.exe` (ele procura o processo em execução, bibliotecas Steam
  comuns e o registro de desinstalação do Windows).

## Onde o Lossless Scaling foi encontrado nesta máquina

- Executável: `C:\Program Files (x86)\Steam\steamapps\common\Lossless Scaling\LosslessScaling.exe`
- Configurações: `%LocalAppData%\Lossless Scaling\Settings.xml`

## Se algo der errado (recuperação)

Os valores de cada campo do overlay vêm dos enums reais extraídos de
`LosslessScaling.dll` (o LS valida o `Settings.xml` com `XmlSerializer` e não
abre se algum valor for inválido, ex.: `'QUALITY' is not a valid value for
LSFGSizeEnum`). Se isso acontecer de novo, restaure o `Settings.xml.bak` ou:

1. Fechar o Lossless Scaling e o companion.
2. Apagar `%LocalAppData%\Lossless Scaling\Settings.xml`.
3. Abrir o Lossless Scaling normalmente — ele recria o arquivo com os padrões
   de fábrica (você perde só as preferências salvas, não a instalação).

## Limitações conhecidas

- Editar e salvar reinicia o Lossless Scaling (se estiver rodando) para as
  mudanças pegarem, já que ele lê o `Settings.xml` só na inicialização — pode
  causar uma pausa breve na geração de quadros.
- O toggle simula a hotkey configurada dentro do próprio Lossless Scaling; se
  você mudar essa hotkey por lá, o companion acompanha automaticamente (lê o
  arquivo a cada acionamento), sem precisar reconfigurar nada aqui.

## Para quem desenvolve

Estrutura, padrões e regras do projeto estão em [CLAUDE.md](CLAUDE.md).
