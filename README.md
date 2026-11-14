# PHI Client Lab

A native C# / WinUI 3 PHI client, independently implemented from the
[wphi protocol and behavior documentation](https://github.com/s7k/wphi/blob/main/docs/04-client-spec.md).
The UI uses the official experimental
[Microsoft.UI.Reactor](https://github.com/microsoft/microsoft-ui-reactor)
NuGet package (`0.1.0-preview.17`), not MauiReactor or a local imitation.
This is an initial functional client, **not a feature-complete wphi replacement**.

## Requirements and running

- Windows 11 with Developer Mode enabled for development package registration.
- .NET 11 SDK and WinApp CLI 0.7 or later (developed with 0.8.0).
- x64 or ARM64. The app is packaged (single-project MSIX); do not run the EXE
  directly or change it to unpackaged mode to bypass activation errors.
- Internet access for the first NuGet restore and Windows App Runtime setup.

From the repository root in PowerShell:

```powershell
dotnet build .\PhiClient\PhiClient.csproj -p:Platform=x64
winapp run .\PhiClient\PhiClient.csproj --arch x64 --detach
dotnet test .\PhiClient.Core.Tests\PhiClient.Core.Tests.csproj
```

For ARM64, replace `x64` with `ARM64` for the build and `arm64` for `--arch`.
Native AOT publishing is not configured or verified. The official template uses
the .NET 11 Windows target and brings in its matching Windows App SDK through
Reactor. The per-app WinUI analyzer is included with `PrivateAssets=all`.

If packaged registration fails from a deeply nested or access-restricted checkout,
use a short, per-configuration layout outside AppData:

```powershell
winapp run .\PhiClient\PhiClient.csproj --arch x64 --detach `
  --output-appx-directory "$env:USERPROFILE\PhiClientLayouts\Debug-x64"
```

The MSIX-installed WinApp CLI redirects writes under AppData, so an AppData
layout cannot be registered. The development package references its layout
directory: keep it in place and do not share it between configurations or
architectures. Do not disable packaging or change system ACLs to work around
registration errors.

For a trimmed Release, use its normal restore:

```powershell
dotnet publish .\PhiClient\PhiClient.csproj -c Release -p:Platform=x64
```

A Debug-only restore does not include
the Release linker/crossgen tools; do not reuse it with `--no-restore`.
Use a fresh layout when switching between build and publish outputs.
When registering the published folder directly, supply the MSBuild-generated
`AppxManifest.xml` with its Windows App Runtime package dependency, not the
unexpanded source `Package.appxmanifest`. A successful build alone does not
verify that the trimmed package activates.

## Playing

1. Register an existing character: enter a display name, server hostname, TCP
   port and character ID, then **Save character**. The first line of an existing
   Philly/wphi `.phirc` contains its ID. Folder import is not implemented.
2. Alternatively, select **Create a new character instead**, provide a name,
   six ASCII letter/digit password, initial image number and email. This sends a
   real registration request to the server you specify; there is no default
   public endpoint and no automatic registration.
3. Choose **Connect**. Each connected character gets its own tab and independent
   connection, status, map, log and command input.
4. Enter or **Send** sends the current line and keeps it for reuse.
   Shift+Enter inserts a newline; Ctrl+Enter or **Party** sends to the party.
   Ordinary Enter does not turn a leading `*` into an accidental world shout:
   use **Operation > Shout current line** and confirm instead.
5. Esc switches from the command field to movement mode; Tab returns to input.
   Gameplay shortcuts do not intercept typing or editing controls.
   Native menus expose actions, system magic, map options and keyboard help.
6. **Action > Write sign** opens a multiline editor. **Send sign** sends separate lines
   followed by `.`, and **Cancel sign** sends `.!`. Protocol-command lines and
   standalone terminators cannot be inserted into sign content.
7. **Disconnect** sends `#x` before closing. Close a tab before deleting its
   saved character; deletion needs a second confirmation click.

| Keys outside text fields | Action |
|---|---|
| Arrows / numpad 8,2,4,6 | Move using the server-confirmed map style |
| Ctrl+movement | Strafe on north-up maps; face a direction on rotating maps |
| Shift+arrows | Relative forward/backward movement or turn left/right |
| Numpad 7,9,5 | Turn left, right, back |
| 789 / UIO / JKL / M, `- ; / '` | Full-key equivalents; toggle in Operation |
| Z/X/C/V/F | Get, put, use, sort, floor items |
| A/S/D/B | Read, write, erase sign, watch board |
| Q/W/M/R/P | Equip, unequip, check+look, spells, pay |
| Space / J; L / numpad Enter | Attack; cast |
| F1–F7 / Alt+1–7 | Numbered spells |
| Alt+letter / Alt+Shift+letter | System magic; see the Magic menu |
| T / Shift+T; Shift+7 / Shift+5 | Rotating / north-up; 7x7 / 5x5 |
| Shift+F5 | Show/hide map pane |
| Ctrl+Tab / Ctrl+Shift+Tab | Next/previous character, including from the input field |
| Pause / Shift+F1; Alt+Pause | Force disconnect current; all characters |
| Lists: 1–9, arrows, Space, Enter | Current-page selection, cursor, check, confirm checked rows |
| Lists: Esc/0/period/left/right; minus/plus | Cancel; all |

List continuation keeps prior checks and uses the server's real numbers.
System magic that needs free hands first waits for the actual equipment list,
removes the weapon if present, and suppresses only intermediate equipment/spell
lists. Target lists remain visible. Preparation times out visibly after five
seconds; automatic re-equipping is not implemented. Call, Return and shouting
require confirmation. Japanese IME composition has dedicated commit-key guards.
A native Microsoft IME check verified physical romaji composition, commit Enter
without sending, and a separate Enter sending exactly one Japanese line.

## Character graphics and map chips

Choose **View > Graphics folder**, enter an absolute local folder, then **Load**.
To use **the actual graphics shipped by wphi**, run this explicit setup command
in PowerShell 7.4 or later:

```powershell
.\tools\Get-WphiGraphics.ps1
```

Then load the printed absolute directory (by default `.local\wphi-graphics`).
The setup script downloads the reviewed wphi revision
`d8f11bf60c95d470fa14ca83bcc4c02d5c83fecc`, verifies the archive's SHA-256,
and imports its unchanged BMPs and license/author notices. It refuses existing
directories and does not import Config files or any public-server endpoints.
It does not redraw or substitute the graphics. To use an existing checkout,
enter its root or `resources\phi` directly; a checkout root is resolved to that
graphics subfolder. For an offline setup, pass `-ArchivePath` with the matching
original archive and `-OutputDirectory` with a new local folder.

The imported non-sushi character sheets are **PCGL-applicable graphics parts**.
PCGL permits PHI-supporting use with the scope declaration and license attached;
the importer preserves both original PCGL files and wphi's provenance README.
Sushi sheets are expressly excluded from PCGL and identified upstream as MIT
(Copyright 2026 s7k); the MIT notice is also attached. The Mine skin identifies
**Cress** as author, and wphi records its maintainer's reuse confirmation on
2026-11-07. This record is not treated as a blanket PCGL/MIT grant for the skin.
Artwork stays in the external local folder, not in this application's package
or source repository. See `tools\WphiGraphics-NOTICE.md` for exact scope and
sources. Preserve these notices with screenshots and any permitted distribution.
Other user-provided collections still require their own permission.

The app itself does not download images or connect while loading graphics.
Point at the directory containing these paths:

```text
chara\<graphic-or-character-name>.bmp
chara\*.ali
chip\<server-mapset>.bmp
Skins\Mine\chip\Default.bmp
Skins\Mine\Chara\Index.txt
Skins\Mine\Chara\<default-graphic>.bmp
Skins\Mine\Images\Items.bmp
```

Characters use the original 96-pixel-wide BMP sheet: four 32-pixel-high rows
(back, right, front, left), two 16x32 frames followed by two 32x32 frames.
The original bundled 96x160 sheets also contain a bottom portrait row, which
is not part of map animation and is not drawn on the map.
Exact RGB `(0,128,128)` is transparent. Larger `*` actors use the 32x32 frames;
ordinary animated sheets alternate their two frames every 500 ms.
This includes type `0x75`, as in wphi. Name-keyed `#ex-obj` instructions
resize sprites with nearest-neighbor sampling and the original bottom anchor;
the definition is captured when the actor's `O` row arrives.

Map sheets are 1024x96 BMPs: the left 512 pixels hold 32 chips, in 16 columns
of 32x48; the right half is the opacity mask. Items use a horizontal strip,
at least 256x64: the bottom half masks the top half. A mask pixel is opaque
only when all RGB channels exceed 200. The map includes the top overhang,
tree/ground composition, water edges, signs, item/circle clipping, row-ordered
occlusion, and same-cell actor offsets. Attribute bits 0–1 select the four
`#mapset` / `#mapset-define` sheets. Missing named mapsets use `Default.bmp`
with a visible notice.

Rendering follows wphi commit `d8f11bf60c95d470fa14ca83bcc4c02d5c83fecc`.
Maps default to 100%: 32 logical pixels per cell, with nearest-neighbor
physical-DPI resampling rather than automatic fitting. Normal maps have a
16-pixel transparent top margin; missing terrain displays black beneath any
retained actors. The pointer coordinate transform uses that same margin.
Noncentral names have white text with a black outline, strip PHI tags, and
use the last nonempty name at a shared cell. The name list uses stable y order.

EagleEye uses a square of the maximum received width/height, padding with `?`.
It has no top margin, always uses frame 0 and mapset 0, and scrolls rather
than shrinking to the pane. Attribute `0x58` becomes a nameless, front-facing
large `t_man` / type `0x40` actor, not a sign or an item. Supply
`chara\t_man.bmp` or the type's permitted default graphic.

Character resolution follows the original priority: `.ali` alias, graphic name,
character name, then the type default in `Skins\Mine\Chara\Index.txt`;
`setcg` type `0x40` tries the graphic name before aliases. Index/alias text uses
CP932. Default files are searched in the skin's Chara folder and then `chara`.
Repeated aliases are last-wins in file enumeration order; multiple alias files
have no guaranteed ordering. `LANU_` graphic filenames are excluded.
Reload the folder after modifying images; file and resolved-name caches are
bounded. Sheets are limited to 8 MiB / 2048 pixels per side.

The configured folder is saved as `graphics-folder.txt` beside the profile file.
Leave it blank and Load to return to the explicitly labelled schematic map.
Missing or corrupt files are reported on screen; missing actors use their
tag-stripped initial in yellow (or `?` for an empty name).

The importer checks are reproducible with the downloaded original archive:

```powershell
.\tools\Test-WphiGraphics.ps1 -ArchivePath C:\Downloads\wphi-original.zip -ArtifactDirectory C:\Temp\wphi-import-check
```

Use a new artifact directory. These checks compare every imported file against
the untouched archive, require the license/author notices, exclude Config files,
and reject altered archives and attempts to overwrite an existing collection.

## Data and trust

Profiles (name, host, port and a random credential key) are stored in
`%LOCALAPPDATA%\PhiClient\characters.json`; **Open settings folder** opens the
effective location, including any Windows package redirection.
Character IDs are stored separately in Windows Credential Manager under
`PhiClient.Character`, not in JSON or source control. Registration passwords
are not saved. A server-created character remains on the server if saving its
local profile subsequently fails. In that case the returned ID remains in the
existing-ID form so saving can be retried without creating another character.

PHI uses **unencrypted TCP**. IDs and registration details are visible to the
server and network path. Use only a trusted server; the credential vault protects
storage, not network transport. Server-requested world transfers are rejected
with `#no-srv` rather than forwarding credentials to a different host.
Logs render as native plain text: PHI formatting tags are stripped, and remote
HTML, links and images are not executed or downloaded.

Connections use CP932, LF delimiters, a 10-second connect timeout, a 30-second
send timeout and bounded 64 KiB incoming lines. Oversized lines are discarded
until the next LF with a visible diagnostic. Keepalive starts after two minutes
without receiving data; a three-minute warning is shown. `#lag` and `#remap` are
answered automatically, except inside literal `#more` blocks. Display history
is bounded to 500 rows / 100,000 characters, with individual displayed rows
truncated at 8,192 characters.

## Implemented and not yet implemented

Implemented: registration and creation, credential-backed profiles, independent
native character tabs, menus, multiline/current-line/party input, HP/MP bars and
named conditions, 7x7 and legacy 5x5 terrain, 15x15 EagleEye, user-supplied sprite
and tile-sheet rendering, full-key movement and common actions, system-magic
preparation, checked/paged selections, controlled dialogs, and sign editing.

Not yet implemented: full Philly folder/settings import, world discovery and
image selection previews, automatic asset downloads, configurable skins other
than the Mine directory layout, rich speech styling, overlaid actor status
decorations, legacy 5x5 actor decoding, configurable 50–400% map zoom,
extended maps / movement prediction, automatic re-equipping, dedicated
private-message UI, master-magic menus, world transfers, configurable F8–F12 /
Alt+G keymaps, window-layout persistence and updates. The map is integrated,
not a separate floating window. The title/direction display remains textual,
not wphi's compass. Extended maps and movement prediction remain requested
wphi compatibility gaps, not features excluded because Philly lacks them.
Raw commands can access server features, but do not add missing client-side UI.

## Layout and verification

- `PhiClient.Core\ClientModel.cs`, `GameModel.cs`: UI-free state, typed messages
  and pure update functions; selection updates are copy-on-write.
- `PhiClient\App.cs`, `App.Game.cs`: Reactor views; `App.State.cs` connects the
  actual `ClientModel` to Reactor's `UseReducer`, rather than a dummy counter.
- `App.Effects.cs`, `App.Input.cs`, `App.GraphicsEffects.cs`, `App.Dialogs.cs`:
  lifecycle/network commands, physical-key/IME adapters, graphics configuration,
  and the native dialog bridge. Async results dispatch typed messages.
- `CharacterStore`, `GraphicsStore`, `GameSession`: Windows persistence, bounded
  local asset loading, and per-tab runtime handles. Connections/bitmaps/controls
  are not stored in the core models; file loading never runs in the views or
  pure updates. Graphics are prepared before rendering and exposed as live
  native bitmaps.
- Other `PhiClient.Core` files: transport, framing, parsing, input mapping and
  platform-independent pixel composition. `MapPresentation.cs` holds pure
  EagleEye conversion, names/initials and pointer-coordinate projection;
  `App.MapEffects.cs` owns native bitmap/DPI lifecycle adaptation.
- `PhiClient.Core.Tests`: loopback-server tests; no live game service is needed.

`dotnet test` exercises the wire-level login, split CP932 input, literal sign
blocks, map bytes and packed actors, list numbering, server control replies,
64 KiB framing boundaries, outbound injection rejection, and registration
success/rejection, keyboard/modifier precedence, CR/LF current-line behavior,
paged selections, magic preparation, sprite masks/occlusion, and immutable
state transitions/session isolation. Native packaged checks use a synthetic
loopback server and newly authored test graphics, not copied upstream art.
The water-edge regression checks all 256 independent 8-neighbor combinations
and all four quadrants against a separately transcribed source table.
Synthetic fixtures verify numerical rendering rules; they do not establish
visual equivalence to original artwork that is not licensed or supplied.
Public-server interoperability, ARM64 execution, high-contrast rendering and
other IME/input-method combinations remain unverified.

## Upstream and licensing

Behavior reference: [s7k/wphi](https://github.com/s7k/wphi) at
`aafc1f137f8cba8894153a7a97c2a15878420c8d`.
Its workspace `Cargo.toml` declares MIT, but no root license text was present
at the inspected revision. No wphi source files, images, fonts, sounds, skins
or bundled third-party assets are copied into this repository.
The client logic here is newly authored against the documented wire protocol.
Reactor and the app scaffold come from Microsoft's official distribution;
their applicable notices and licenses remain with their packages.
