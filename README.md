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
4. Type chat or raw PHI commands and press Enter or **Send**. Lists show the
   server's actual selection numbers, including continuation pages. Choose an
   item or **Cancel selection**.
5. Press Esc in the command field for movement mode. Arrows / numpad 8, 2, 4, 6
   move; Ctrl moves without turning on north-up maps; Shift+Left/Right turn.
   Numpad 7/9 turn, 5 turns back. Z/X/C get/put/use; Space attacks.
   Tab returns to the command field. On-screen buttons offer the same basic
   actions. The confirmed server map style controls movement commands.
6. **Write sign** opens a multiline editor. **Send sign** sends separate lines
   followed by `.`, and **Cancel sign** sends `.!`. Protocol-command lines and
   standalone terminators cannot be inserted into sign content.
7. **Disconnect** sends `#x` before closing. Close a tab before deleting its
   saved character; deletion needs a second confirmation click.

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

Implemented: registration by ID, server-side character creation, credential-backed
profiles, simultaneous character sessions, CP932 TCP login, text/chat commands,
status display, north-up/rotating 7x7 symbolic maps with actor positions,
movement and common actions, numbered selection lists, and sign editing.

The map uses native text tiles (`@` self, `o` actor, `#` wall, `.` floor);
it does not reproduce wphi's sprite rendering. Not yet implemented:
Philly folder/settings import, world discovery and image selection previews,
character/map graphics and downloads, skins, rich speech styling, 5x5 maps,
EagleEye graphics, extended maps / movement prediction, automatic equipment
handling and magic shortcuts, dedicated private-message UI, master-magic menus,
world transfers, configurable keymaps, window-layout persistence and updates.
Raw commands can access server features, but do not add missing client-side UI.

## Layout and verification

- `PhiClient`: packaged pure-C# Reactor UI and Windows credential storage.
- `PhiClient.Core`: transport, line framing and protocol parsing without UI.
- `PhiClient.Core.Tests`: loopback-server tests; no live game service is needed.

`dotnet test` exercises the wire-level login, split CP932 input, literal sign
blocks, map bytes and packed actors, list numbering, server control replies,
64 KiB framing boundaries, outbound injection rejection, and registration
success/rejection. It does not assert interoperability with a live public world.

## Upstream and licensing

Behavior reference: [s7k/wphi](https://github.com/s7k/wphi) at
`aafc1f137f8cba8894153a7a97c2a15878420c8d`.
Its workspace `Cargo.toml` declares MIT, but no root license text was present
at the inspected revision. No wphi source files, images, fonts, sounds, skins
or bundled third-party assets are copied into this repository.
The client logic here is newly authored against the documented wire protocol.
Reactor and the app scaffold come from Microsoft's official distribution;
their applicable notices and licenses remain with their packages.
