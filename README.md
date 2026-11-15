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

## 設定

**File > 設定…** から編集し、**設定を保存** でこのPCの `settings.json` に保存します。
閉じると未保存の変更を破棄します。Philly.iniの取り込みは編集画面に反映するだけで、
保存・接続・キャラクターIDの取り込みは行いません。キーは原典と同じ大文字小文字で解釈し、
重複項目は先頭を採用します。

F8–F12は通常時とリスト表示中で別の送信文字列を設定でき、`\n` は改行として送ります。
Alt+Gとフルキー移動も設定できます。自動装備を有効にすると、原典と同じ対象選択のない
系統魔法だけ、実際に外した1番の武器を名前で装備し直します。detectなどの対象選択魔法では
装備し直しません。自動装備・武器を外す操作はそれぞれ無効にできます。

設定の保存・キー・自動装備・マップ倍率・拡張マップ・滑らかな移動は実装済みです。
ログの時刻・フォント・行コピーの設定も適用します。通知、独立マップ窓などの設定の適用と、
全画面・全操作の実UI確認は、互換性監査の後続作業です。
これらは対応範囲から除外していません。

## 拡張マップと位置予測

絶対座標を送る世界の7×7北固定マップだけを、世界別に記録します。未知のマスと盲目中の
地形は記録せず、記録した外周や視界外の補完部分を25%暗く表示します。窓のマップ欄と倍率に
合わせて外周を最大4マス追加します。記録は `Maps` フォルダーへ32×32マスの区画単位で
約5秒ごとに保存します。異常なファイルは上書きせず、読み込み・保存の失敗を知らせます。
**View > 現在の世界の記録地図を消す…** は確認後にその世界だけを消します。

「拡張マップ」が有効なら、送った北固定の `go N/E/S/W` の返事を120–1000ms待ってから
表示上の位置を予測します。待ち時間は直近8件の遅延に合わせ、予測は100msより速く進めません。
サーバーの座標・移動失敗・世界移動で補正し、追加の移動命令は送信しません。
最後に受信した7×7地形は予測時にも保持します。

滑らかな移動は原典と同じ実験的な90msの表示処理です。同じ世界で2マス以内の北固定移動に
限り有効で、世界変更・大きな座標変更では使いません。Windowsでアニメーションを無効に
している場合も使いません。描画は整数ピクセル・最近傍を使い、クリック座標も倍率に合わせます。
非常に高いDPIと倍率の組み合わせで4096pxの描画上限に達した場合は、倍率を下げる案内を表示します。

## Playing

**Operation > 全タブへ発言** は、開いている接続中のタブだけへ同じ行を送ります。
送信ごとに対象タブ数を確認し、キャンセルすると全タブ発言を解除して下書きを保持します。
保存済みのキャラクターを新たに開く操作ではありません。

**Operation > 大声で発言** は世界全体への発言です。確認画面の **今回だけ送信** は
送信後に通常発言へ戻し、**すべて大声** は次の発言にも適用します。
全タブ発言を有効にしている間は通常発言として送ります。
**発言の前に付ける言葉…** は空の入力行と送信後の新しい行へ追加します。空欄で解除できます。
末尾行で修飾キーを使わずDownを押すと、その行を設定した言葉に置き換えます。
IME変換中はこの操作を行いません。

これらの状態遷移・行編集・100msの送信間隔は純粋テストで確認しています。
追加モードのnative UIA試験はメニュー項目の取得で中断しており、実キーとIMEの確認も
前面化が可能になってから行う残確認です。

1. Register an existing character: enter a display name, server hostname, TCP
   port and character ID, then **Save character**. The first line of an existing
   Philly/wphi `.phirc` contains its ID. Folder import is not implemented.
2. 新規作成では **キャラグラの候補を取得** で、その世界が返す初期画像の一覧を取得できます。
   原典と同じ `#ex-get REGINFO IMG` を送り、`#ex-put REGINFO IMG` から `#ex-put .` までを
   0番から順番に表示します。この操作は指定したゲームサーバーへTCP接続しますが、
   ID・パスワード・作成要求は送りません。起動時や世界選択時の自動取得は行いません。
   一覧で選ぶか番号を入力すると、設定した実wphi素材の正面・大きい絵・コマ0を表示します。
   素材がない場合はその理由を表示し、別のキャラグラで代用しません。
   サーバーまたはポートを変えると候補を破棄し、古い取得結果も適用しません。
   候補の取得に対応しない世界では、番号を手動入力できます。
   名前（2文字以上）と初期画像番号を入力します。メールアドレスは任意です。
   パスワードが空欄なら、**作成ボタンを押した時点**で原典と同じ文字集合
   `abcdefghijkmnpqrstuvwxyz23456789` から暗号学的乱数で6文字を生成します。
   入力済みならその6文字を使い、空白・制御文字を拒否します。CP932で表現できない文字は
   原典と同じく送信時に `?` へ変換されるため、パスワードには上記の文字集合を推奨します。
   作成後の確認画面で本人がパスワードを表示して控えられます。ログや設定ファイルには保存しません。
   指定した世界に実際の作成要求を送る操作なので、接続先を確認してください。自動作成は行いません。
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

## 接続先世界の一覧

登録画面の右側に世界一覧を表示します。ゲーム中は登録欄の
**世界一覧から接続先を選ぶ** で開けます。世界名・アドレスで絞り込み、
**この世界を使う** を選ぶと、既存 ID 登録／新規キャラ作成のサーバーと
ポートだけを入力します。名前・ID・パスワードなどは変更せず、
選択だけでは保存・ログイン・キャラ作成・世界移動を行いません。
接続には、登録後の **Connect** などの明示操作が必要です。

wphi の原典では、同梱 `Config/unew.ini` の `Schedule15` が指定する
`http://www.tofup.net/raphi/resist.lzh` を HTTP で取得し、
展開した `resist.ini` を読みます。これはゲームサーバーへの照会ではなく、
一覧ファイルの配布です。未取得または同梱の `Date` と取得記録が違う場合に
更新し、取得できなければ前回のファイル、それもなければ同梱
`Config/worlds.ini` を使います。原典には `worlds.ini` の配布元が
見つからない旨のコメントがあります。

本アプリは起動時に外部通信をせず、保存済みの一覧、なければ原典
`d8f11bf60c95d470fa14ca83bcc4c02d5c83fecc` の同梱一覧から
世界名・アドレス・ポートを抽出した 17 件を表示します。
**HTTP 一覧を更新** で配布元へ明示的に取得を要求できます。
原典の `Date` による自動再取得は行いません。配布 URL の変更は実行中だけ有効で、
再起動時は原典の URL に戻ります。保存済みの一覧は引き続き利用できます。
資格情報付き URL とリダイレクトは受け付けません。
取得失敗時は理由を表示し、前の一覧を保持します。

**INI ファイルから取り込む** では、既存の wphi／Philly の
`resist.ini` または `worlds.ini` のローカル絶対パスを指定できます。
原典と同様に CP932 の INI を読み、節名を世界名、`Address` をホスト、
`Port` をポートとして扱います。キーの大文字・小文字を区別せず、
不正な接続先は件数と理由を表示して除外します。全件が不正なら失敗します。
世界の説明文や資格情報は一覧として取り込みません。

HTTP の配布ファイルは 2 MiB、INI は 1 MiB までです。
LZH は Windows 標準の `tar.exe` で `resist.ini` だけをメモリーに読み、
アーカイブ内の他のファイルをディスクに展開しません。
`tar.exe` が利用できない場合はエラーを表示するため、INI から取り込んでください。
一覧は設定フォルダーの `world-directory\resist.ini` に保存されます。

一覧の配布元は平文 HTTP であり、内容の真正性や接続先の稼働・安全性は
保証されません。表示された世界へ自動接続したり、稼働確認したりはしません。
開発時の取得・選択・接続適用のテストは、所有確認済みのローカルモックだけで
行います。実ゲームサーバーへの接続は行いません。

原典の根拠：
[取得とキャッシュ](https://github.com/s7k/wphi/blob/d8f11bf60c95d470fa14ca83bcc4c02d5c83fecc/src-tauri/src/main.rs#L415-L434)、
[HTTP/LZH と INI 解析](https://github.com/s7k/wphi/blob/d8f11bf60c95d470fa14ca83bcc4c02d5c83fecc/src-tauri/src/download.rs#L66-L135)、
[選択による入力](https://github.com/s7k/wphi/blob/d8f11bf60c95d470fa14ca83bcc4c02d5c83fecc/ui/src/NewCharacter.tsx#L145-L168)。

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
directories. From Config it imports only the reviewed `MasterMagicMenu.xml`;
it does not import any public-server endpoints or other Config files.
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
the untouched archive, require the license/author notices, allow only the reviewed MasterMagic menu from Config,
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
storage, not network transport.

サーバーから世界移動を要求された場合は、原典と同じ
`#reserve` → `#rsv-ok` → `#trans` → `#trs-ok` → `#ch-srv-ok` の順で接続を切り替えます。
移動先に `#open` を再送せず、ログイン設定だけを送ります。別ホストへの移動はID送信前に確認し、
取り消した場合は移動先へ接続しません。失敗時は元世界の未処理行を順序どおり処理します。
移動中の操作や、古い世界で確認待ちになった操作は新しい世界へ持ち越しません。
成功後は同じ登録キャラクターの接続先を保存します。保存に失敗した場合も実際の移動は取り消されないため、
画面のエラーを確認してからアプリを終了してください。

ログはネイティブの `RichTextBlock` で表示します。PHI の色・文字サイズ・フォント・太字・
斜体・下線・取消線・改行・タグの終了を解釈し、元のタグ付き行と受信時刻を別に保持します。
右クリックの **行全体をコピー** / **行全体を入力欄へコピー** は、部分選択でも行全体を使います。
タグ・時刻・名前の除去やタグの無効化は設定に従います。入力欄へのコピーだけでは送信しません。
サーバーが送った HTML は文字として表示し、実行しません。画像は現段階ではリンク付きの
`[画像]` 表示で、受信時の自動取得・埋め込み表示はまだ未対応です。外部リンクは確認後に
ブラウザーで開きます。ローカル・非公開アドレスは拒否しますが、外部ブラウザーの通信まで
アプリが固定・制御するものではありません。

**Operation > このキャラクターのログを自動保存** で、タブ単位の自動保存を切り替えます。
起動時は保存しません。有効にしてからの行を `Logs\<キャラクター名>\<日付>.html` に追記し、
設定で HTML 保存を無効にした場合は `.txt` に追記します。自動保存には表示設定にかかわらず
時刻を付けます。**ログ欄の内容をHTMLに保存** は現在の表示履歴を別ファイルへ保存します。
**ログの保存先を開く** で保存先を開けます。書き込み失敗はログへ表示し、自動保存を停止します。
保存した HTML からもスクリプトや画像を自動取得・実行しない制限を付けています。

Connections use CP932, LF delimiters, a 10-second connect timeout, a 30-second
send timeout and bounded 64 KiB incoming lines. Oversized lines are discarded
until the next LF with a visible diagnostic. Keepalive starts after two minutes
without receiving data; a three-minute warning is shown. `#lag` and `#remap` are
answered automatically, except inside literal `#more` blocks. Display history
is bounded to 2,000 rows / 2 MiB of raw text, with individual displayed plain rows
truncated at 8,192 characters. Raw rows remain available for styled display and copy within that history bound.

## Implemented and not yet implemented

Implemented: registration and creation, credential-backed profiles, independent
native character tabs, menus, multiline/current-line/party input, HP/MP bars and
named conditions, 7x7 and legacy 5x5 terrain, 15x15 EagleEye, user-supplied sprite
and tile-sheet rendering, full-key movement and common actions, system-magic
preparation, checked/paged selections, controlled dialogs, and sign editing.

今回の互換性監査で、設定の保存・元のINI取り込み、F8–F12 / Alt+G、対象魔法の再装備、
50–400% の倍率、方位表示、世界別の地形記録、移動予測、滑らかな移動、
reserve/trans による世界移動、原典のパスワード生成とログ装飾・コピー・保存を追加しています。
全体の実UI確認はまだ進行中です。

**File > 全タブを接続／全タブを切断／全タブを強制切断** は、原典と同じく
現在開いているタブのキャラクターを対象にします。全タブを接続すると、切断済みのタブだけを
再接続し、タブと入力を保持します。保存済みの別のキャラクターは、個別にタブを開いて使います。
通常の切断は接続中の世界へ `#x` を送り、強制切断は通信を直接終了します。
`Alt+Pause` も全タブの強制切断です。

発言はカーソルのある行を送信し、送れた行を入力欄に残して次の行へ進みます。
最後の行から送ると空の行を追加し、最大100行を保持します。確認を取り消した場合や
送信に失敗した場合は入力を保持します。通常の発言では先頭の `*` を原典どおり保護し、
明示的に大声を選んだ場合は元の行へ `*` を付けます。

**Character > priv（内緒話）** で、その世界の相手一覧と内緒話の画面を開きます。
相手を選んで会話を開くと、入力欄の Enter または **内緒話を送信** で送信できます。
会話は参加者の組ごとに分かれ、受信した内緒話は通常のログにも残ります。
受信タグから作られた多人数の会話では、最初に返信する前に全員の名前を確認します。
キャンセル・送信失敗時は入力を保持します。番号が見つからない相手は明示し、
見つかった相手だけに送信します。内緒話の行を入力欄へコピーする操作は、
通常の発言欄ではなく内緒話の入力欄を対象にし、コピーだけでは送信しません。

相手一覧は `#ulist` で取得します。並行する取得はまとめ、10秒以内は直近の一覧を使います。
最後の `#user` 受信から300ms待ち、最大60秒で取得を終えます。返事がない場合はエラーを
表示します。世界移動・切断で取得を中止し、別の世界へ古い内緒話を送信しません。
会話は最大50組、各組の履歴は最大2,000行・2MiBです。安全のため宛先は1,000人、
一度の送信量は8Mi文字相当までに制限しています。

**File > 超魔法のXMLを読み込む…** から、信頼できるwphi／Phillyの
`Config\MasterMagicMenu.xml` を指定できます。Shift_JIS / CP932、またはBOM付きUTF-8を
読み込み、指定したファイルの参照を保存します。起動時はアプリ保存先の
`MasterMagicMenu.xml`、保存済み参照、素材フォルダーの `Config\MasterMagicMenu.xml` の順に探します。
原典の素材取り込みスクリプトは、許可記録を確認したこのXMLだけをConfigから追加します。
原典READMEに記録された2026-11-09のPhilly作者による同梱許可と出典を保持し、
公開世界の接続設定や他のConfigファイルは取り込みません。既存の素材フォルダーへの上書きも拒否します。

**超魔法** メニューは経験値が原典のマスター値 `99999999` のキャラクター、または
設定で表示を有効にしたキャラクターに表示します。選んだ項目の送信・確認・整数・文字入力を
XMLの順番どおりに実行します。表示設定はメニューの切替であり、実行結果は接続先の権限と仕様によります。
整数は半角の数字と先頭の `-`、文字は `%string`、整数は `%int` として置換します。
取消・不正な整数・世界移動・切断で残りの手順を中止します。送信済みの操作は取り消されません。
実行中の手動送信を止め、元の接続世代に固定して、古い手順が移動先へ流れないようにしています。
XMLは1MiB、項目は2,048件、手順は1項目4,096個までです。DTDと外部entityは読み込みません。
超魔法の実UI総合確認は、全体の回帰確認とあわせて進行中です。

未対応・確認中: Philly の登録情報取り込み、ログ画像の自動表示、
Mine 以外のスキン、actor の状態装飾、旧5×5 actor の全仕様、
入力欄のタグメニュー、通知・音、独立マップ窓、
窓配置の保存と復帰、更新確認など。これらは要件から除外していません。
生のコマンドを送れることを、対応するクライアント UI の実装済みとは扱いません。

リストは番号のない説明行も保持し、原典と同じく先頭行から決めた番号のずれと行順で選択します。
続きの先頭番号が前ページの最後と一致する場合だけページを結合します。
続きがない場合や読み込み待ちの場合の `9` は送信しません。空の完了リストは既存の表示を
消さず、明示的なリスト終了で閉じます。

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
