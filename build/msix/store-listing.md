# Microsoft Store listing — Claude Code Tray (T409, T412)

What Partner Center asks for when the package is submitted, kept beside the manifest so a submission and
the code it describes are reviewed together. Paste each block into the field it names. Keep the wording
in step with the README: the pitch, the privacy facts and the unofficial disclaimer are the same claims.

## Images

`build\build-msix.cmd` writes them to `dist\store-art\`, each named for its slot (`ClaudeTray.exe
--store-art <dir>` alone redraws them):

| Slot | File |
|---|---|
| Poster art 9:16 | `poster-9x16-1440x2160.png` |
| Box art 1:1 | `box-1x1-2160x2160.png` |
| App tile icon 300x300 | `tile-300x300.png` |
| 1:1 150x150 | `tile-150x150.png` |
| 1:1 71x71 | `tile-71x71.png` |

Screenshots: the README's set under `site/public/shots/` (Windows needs at least one, 1366x768 or larger
is accepted; PNG).

## Once per submission (not per language)

**Privacy policy URL:** https://alegauss.github.io/claude-tray/features/privacy/

**Website:** https://alegauss.github.io/claude-tray/

**Support contact:** https://github.com/alegauss/claude-tray/issues

### Notes for certification

The app is a notification-area (system tray) utility: after launch it shows an icon, not a window. Click
the icon to open the main window; right-click it for the menu. Without Claude Code signed in on the test
machine the icon shows a "not authenticated" state, which is the expected behaviour — the app needs a
Claude Code sign-in to have anything to show.

It starts with Windows through the package's startup task, which the user can turn off in Settings >
Apps > Startup; the app's own Settings page links there. The `ClaudeTray.exe` execution alias exists so
Claude Code's status-line command can run the app from any shell.

Data: the app sends no telemetry, analytics or crash reports, and nothing to the developer. To read
usage it makes one minimal request to the Anthropic API on each poll (every 5 minutes by default, less
when idle), using the OAuth sign-in Claude Code already stores in
`%USERPROFILE%\.claude\.credentials.json`, and reads only the rate-limit headers of the reply. From Claude
Code's local transcripts it reads token counts, model ids, flags, tool and skill names and the working
directory — never message content. Everything it stores stays in its own app data folder.

### Reusing the Claude Code sign-in (the policy question)

The app does not ask for, store or transmit credentials of its own. It reads the OAuth token Claude Code
already keeps on the user's machine, only to make the same minimal request Claude Code itself makes, so
it can show the user their own limits. The README sets out how this sits with Anthropic's terms
(section "Authentication & Anthropic's terms"). Whether that is acceptable for the Store is the owner's
answer to give, not this file's.

---

## English (en-us)

**Short description**

Your Claude Code limit in the Windows tray: red before you run out, and a weekly forecast paced to the
hours you actually work.

**Description**

Claude Code Tray shows how much of your Claude Code 5-hour session and weekly limit you have used, as a
crisp icon in the Windows notification area.

When your current pace would use the limit up before it resets, the icon turns red and a notification
says when — while there is still time to change course. The weekly forecast is paced to the hours you
actually work, not a straight line through nights and weekends.

It also shows per-session cost at API list prices, usage statistics, and a context audit of what Claude
Code loads before your first prompt — all computed on your own computer. It reads the sign-in Claude
Code already keeps here, so there is no API key to paste and no setup.

This is an unofficial, community-built app. It is not affiliated with, endorsed by, or sponsored by
Anthropic. "Claude" and "Claude Code" are trademarks of Anthropic, PBC.

**Product features**

- Your 5-hour and weekly Claude Code usage as a sharp tray icon
- Turns red before you run out, with a notification while there is time
- Weekly forecast paced to the hours you actually work
- Per-session cost at API list prices
- Context audit: what Claude Code loads before your first prompt
- Several Claude Code profiles, one click apart
- No API key, no setup, no telemetry

**Keywords:** Claude Code, usage, rate limit, tray, quota, forecast, developer tools

## Português (Brasil) (pt-br)

**Descrição curta**

Seu limite do Claude Code na bandeja do Windows: fica vermelho antes de acabar, e uma previsão semanal no
ritmo das horas em que você de fato trabalha.

**Descrição**

O Claude Code Tray mostra quanto você já usou da sessão de 5 horas e do limite semanal do Claude Code,
num ícone nítido na área de notificação do Windows.

Quando o seu ritmo atual esgotaria o limite antes do reset, o ícone fica vermelho e uma notificação diz
quando — enquanto ainda dá tempo de mudar o rumo. A previsão semanal segue as horas em que você de fato
trabalha, e não uma linha reta que atravessa noites e fins de semana.

Também mostra o custo por sessão a preços de lista da API, estatísticas de uso e uma auditoria do
contexto que o Claude Code carrega antes do seu primeiro prompt — tudo calculado no seu próprio
computador. Ele usa o login que o Claude Code já guarda aqui: sem chave de API para colar e sem
configuração.

Este é um app não oficial, feito pela comunidade. Não é afiliado, endossado nem patrocinado pela
Anthropic. "Claude" e "Claude Code" são marcas registradas da Anthropic, PBC.

**Recursos do produto**

- Seu uso de 5 horas e semanal do Claude Code num ícone nítido na bandeja
- Fica vermelho antes de acabar, com uma notificação enquanto há tempo
- Previsão semanal no ritmo das horas em que você trabalha
- Custo por sessão a preços de lista da API
- Auditoria de contexto: o que o Claude Code carrega antes do primeiro prompt
- Vários perfis do Claude Code, a um clique de distância
- Sem chave de API, sem configuração, sem telemetria

**Palavras-chave:** Claude Code, uso, limite, bandeja, cota, previsão, ferramentas de desenvolvimento

## Português (Portugal) (pt-pt)

**Descrição breve**

O seu limite do Claude Code na área de notificação do Windows: fica vermelho antes de acabar, e uma
previsão semanal ao ritmo das horas em que realmente trabalha.

**Descrição**

O Claude Code Tray mostra quanto já usou da sessão de 5 horas e do limite semanal do Claude Code, num
ícone nítido na área de notificação do Windows.

Quando o seu ritmo atual esgotaria o limite antes de repor, o ícone fica vermelho e uma notificação diz
quando — enquanto ainda há tempo para mudar de rumo. A previsão semanal segue as horas em que realmente
trabalha, e não uma linha reta que atravessa noites e fins de semana.

Mostra também o custo por sessão a preços de tabela da API, estatísticas de utilização e uma auditoria
do contexto que o Claude Code carrega antes do seu primeiro prompt — tudo calculado no seu próprio
computador. Usa a sessão que o Claude Code já guarda aqui: sem chave de API para colar e sem
configuração.

Esta é uma aplicação não oficial, feita pela comunidade. Não é afiliada, apoiada nem patrocinada pela
Anthropic. "Claude" e "Claude Code" são marcas registadas da Anthropic, PBC.

**Funcionalidades do produto**

- A sua utilização de 5 horas e semanal do Claude Code num ícone nítido
- Fica vermelho antes de acabar, com uma notificação enquanto há tempo
- Previsão semanal ao ritmo das horas em que trabalha
- Custo por sessão a preços de tabela da API
- Auditoria de contexto: o que o Claude Code carrega antes do primeiro prompt
- Vários perfis do Claude Code, à distância de um clique
- Sem chave de API, sem configuração, sem telemetria

**Palavras-chave:** Claude Code, utilização, limite, área de notificação, quota, previsão, ferramentas de programação

## Français (fr-fr)

**Description courte**

Votre limite Claude Code dans la zone de notification de Windows : rouge avant d'être à court, et une
prévision hebdomadaire calée sur les heures où vous travaillez vraiment.

**Description**

Claude Code Tray montre ce que vous avez déjà utilisé de la session de 5 heures et de la limite
hebdomadaire de Claude Code, sous la forme d'une icône nette dans la zone de notification de Windows.

Quand votre rythme actuel épuiserait la limite avant sa réinitialisation, l'icône passe au rouge et une
notification indique quand — tant qu'il est encore temps de changer de cap. La prévision hebdomadaire
suit les heures où vous travaillez vraiment, et non une ligne droite qui traverse les nuits et les
week-ends.

Il affiche aussi le coût par session aux tarifs publics de l'API, des statistiques d'usage et un audit
du contexte que Claude Code charge avant votre premier prompt — le tout calculé sur votre propre
ordinateur. Il utilise la connexion que Claude Code conserve déjà ici : pas de clé d'API à coller, aucune
configuration.

Cette application est non officielle et développée par la communauté. Elle n'est ni affiliée, ni
approuvée, ni sponsorisée par Anthropic. « Claude » et « Claude Code » sont des marques d'Anthropic, PBC.

**Fonctionnalités du produit**

- Votre usage Claude Code sur 5 heures et sur la semaine, dans une icône nette
- Passe au rouge avant d'être à court, avec une notification tant qu'il est temps
- Prévision hebdomadaire calée sur vos heures de travail
- Coût par session aux tarifs publics de l'API
- Audit du contexte : ce que Claude Code charge avant le premier prompt
- Plusieurs profils Claude Code, à un clic l'un de l'autre
- Pas de clé d'API, aucune configuration, aucune télémétrie

**Mots-clés :** Claude Code, usage, limite, zone de notification, quota, prévision, outils de développement

## Español (es-es)

**Descripción breve**

Tu límite de Claude Code en la bandeja de Windows: en rojo antes de que se agote, y una previsión semanal
al ritmo de las horas en que realmente trabajas.

**Descripción**

Claude Code Tray muestra cuánto has usado de la sesión de 5 horas y del límite semanal de Claude Code,
con un icono nítido en el área de notificación de Windows.

Cuando tu ritmo actual agotaría el límite antes del reinicio, el icono se pone en rojo y una
notificación indica cuándo, mientras aún hay tiempo de cambiar el rumbo. La previsión semanal sigue las
horas en que realmente trabajas, no una línea recta que atraviesa noches y fines de semana.

También muestra el coste por sesión a precios de lista de la API, estadísticas de uso y una auditoría
del contexto que Claude Code carga antes de tu primer prompt, todo calculado en tu propio ordenador.
Usa el inicio de sesión que Claude Code ya guarda aquí: sin clave de API que pegar y sin configuración.

Esta es una aplicación no oficial, creada por la comunidad. No está afiliada, respaldada ni patrocinada
por Anthropic. «Claude» y «Claude Code» son marcas comerciales de Anthropic, PBC.

**Características del producto**

- Tu uso de 5 horas y semanal de Claude Code en un icono nítido
- Se pone en rojo antes de agotarse, con un aviso mientras hay tiempo
- Previsión semanal al ritmo de las horas en que trabajas
- Coste por sesión a precios de lista de la API
- Auditoría de contexto: lo que Claude Code carga antes del primer prompt
- Varios perfiles de Claude Code, a un clic de distancia
- Sin clave de API, sin configuración, sin telemetría

**Palabras clave:** Claude Code, uso, límite, bandeja, cuota, previsión, herramientas de desarrollo
