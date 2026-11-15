# Original basic graphics / 同梱用基本ドット絵

外部素材が未設定・欠落した場合のための、32pxセル用オリジナル素材です。
灰緑の草木、砂色の土、青灰の水、木と石の建物、方向が分かる人物を、
補間・アンチエイリアスなしで描いています。wphi実素材を同梱・加工したものではありません。

## 組み込み契約

- **外部画像を最優先**し、正常にロードできたものは置換しません。欠落したmap/item/actor単位で補完します。
- `manifest.json` が対応表とSHA-256を持ちます。マップ32スロット、items 8スロット、
  既定actor全14コードを12種のシートで収録しています。外部固有キャラ名の一覧の複製ではありません。
- PNGはデコード済みの寸法です。BGRA8 / straight alphaで`PixelImage`へ渡し、
  **`Transparent()`を呼ばない**でください。全alphaは0/255です。
- 既存デコーダーを使う場合はBMPを使用します。`map.bmp` は1024×96の右半分が白黒マスク、
  `items.bmp` は256×64の下半分が白黒マスク。actorは96×128でRGB `#008080`が透過色です。
  既存の `Transparent(horizontalMask: true)` / `Transparent(verticalMask: true)` / `Transparent()` がそのまま使えます。
- mapの実画像は512×96、16列×2行、1チップ32×48。地面は各チップのy=16–47です。
  木などは上16pxへ張り出します。スロット3/18/19/20/23/24/25は合成用の透過レイヤーです。
- actor行は **B / R / F / L**、各32px。小型2フレームはx=0/16・幅16、
  `Size='*'`の2フレームはx=32/64・幅32。高さはすべて32です。
  人物・獣等は向きと歩行差分、魔法・精神体等は脈動差分を持ちます。
  看板と静止オブジェは意図的に同一フレームです。
- 未知actorは`wanderer`、未知地形は既存`MapGraphics.Chips`の既定スロット0。
  未踏`?`はスロット31の暗い霧です。未知actorを透明にしません。
- `MapGraphics.Draw`へ供給するだけで既存のセル・上余白16px・水際・看板・行順重なりを維持します。
  クリック座標、ラベル、倍率、描画器、利用者設定は本パックでは変更しません。
- 本変更はassetのみです。アプリ側の同梱Content/EmbeddedResource設定、ロード、fallback選択は
  実装セッションで行ってください。BMPを任意の外部フォルダーへコピーして利用者設定を変更する必要はありません。

## 確認画像

`contact-sheet.png` は上4段を左からmapスロット0–31の順に配置しています。
下部のactorは左から `person`, `wanderer`, `beast`, `berserker`, `golem`, `skeleton`、
次段は `spirit`, `eraser`, `sign`, `magic`, `animated-object`, `object`。
各欄の上段はB/R/F/L、下段はFの第2フレーム。最下段はitemsです。
ラベルはゲーム画像に焼き込まず、この索引で対応を示します。

`scene-7x7-native.png` は実際のCore描画器で作った224×240画像、
`scene-7x7.png` と `scene-7x7-frame1.png` は最近傍3倍です。
接続・アプリ起動のスクリーンショットではなく、合成fixtureです。

## 再生成・検証

リポジトリルートで実行します。.NET 11 SDKのみを使い、画像用外部パッケージは不要です。

```powershell
dotnet run --project .\assets\bundled-basic\generator\BundledAssets.csproj -- .\assets\bundled-basic
.\assets\bundled-basic\Test-Assets.ps1
```

generatorは画像入力を一切読まず、整数座標の画素・線・楕円・テクスチャから描きます。
BMPの独立再デコードと既存Coreのマスク処理でalpha/RGBを往復照合し、
寸法・全既定actorキー・全方向/大小/フレームの非空・動きの差分・既知地形の非空、
土/草/水/霧の対向端一致、水際全256近傍の描画を検証します。
`Test-Assets.ps1` はWindowsのOSデコーダーでPNG/BMPを読み直し、SHA-256、
寸法、PNG全画素のalpha、4点のプレビュー寸法を確認します。既存のCore試験も変更しません。

## 出典・権利の範囲

新規generatorとその出力、manifest、本説明に限り、同梱の `LICENSE.txt`（MIT）を適用します。
リポジトリ全体のライセンスファイルは本作業時点で見つからなかったため、
この許諾を既存アプリコードや他ディレクトリへ拡張するものではありません。
AI支援で制作した新規表現について、法域をまたいだ著作権成立・独占権を保証するものではありません。

参照したのはローカル取得済みwphi `d8f11bf60c95d470fa14ca83bcc4c02d5c83fecc`
のDefaultマップ、人物、獣、itemsの表示です。色調・可読性・セル密度を観察しただけで、
画素の抽出、トレース、元キャラクター固有表現の再現はしていません。
参照画像は第三者画像モデルに送信していません。
PCGL、Cressのskin、作者別の条件は `tools/WphiGraphics-NOTICE.md` 等の既存記載から変更しません。
本ディレクトリには原典画像・原典画像を含む比較シートは置きません。
