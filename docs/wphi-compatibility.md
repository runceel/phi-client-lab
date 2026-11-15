# wphi互換性・日本語UXの照合記録

## 対象と現在の判定

### 2026-11-15の追加比較

再比較の原典を [`275fcd23d37d652d2e49cd856bd8f750178e884c`](https://github.com/s7k/wphi/tree/275fcd23d37d652d2e49cd856bd8f750178e884c)
へ固定しました。下記の初期56項目の原典`d8f11bf`から22コミット分を照合し、
ログ検索（追加REQ-057、#11）、地図の発言・戦闘効果（追加REQ-058、#12）、
利用者ガイドへの導線（追加REQ-059、#13）を別Issueに分けています。
上流の以後の更新を、この比較の完了条件へ自動追加しません。
タグメニューのnative化は移植版でも既に行っており、新たな未実装とは数えません。

REQ-059はHelpから原典ガイドを開く導線を追加しました。wphi向けと明記し、
既存の外部リンク確認・公開アドレス確認・起動失敗通知を経由します。
この導線の確認のために外部ブラウザーを自動起動することはありません。

REQ-057（#11）は、保持済みの世界ログの表示文字列検索、最新一致からの双方向循環、
件数・行の強調、検索中の新着追随抑止、閉じた後のログフォーカス復帰を実装しています。
検索表示の変更はキャラクター別のModel/messageを通し、同値Modelの再発行には依存しません。
空欄・0件・新着時の現在行保持・保持上限による行削除・キャラクター別表示状態・追随設定を
純粋テスト（関連するModel回帰も含め20件成功）と警告0/エラー0のx64 buildで確認しました。
nativeの複数段落に対する強調位置、スクロール、実キー・IME・
focusは未受け入れで、ビルド成功をその代替にはしません。#12/#22/#20の差分は含めません。

原典は[s7k/wphi](https://github.com/s7k/wphi/tree/d8f11bf60c95d470fa14ca83bcc4c02d5c83fecc)
の`d8f11bf60c95d470fa14ca83bcc4c02d5c83fecc`です。
README、`docs/04-client-spec.md`、parser、通信、描画、設定、画面、キー処理を基準に、
下表の56項目を照合しました。地図幅/UIA修正は`30b96a7`、
主窓の再起動位置の修正は`f693173`です。
経緯は[#1](https://github.com/runceel/phi-client-lab/issues/1)と
[#2](https://github.com/runceel/phi-client-lab/pull/2)へ記録します。

**機能実装と所有モックによる確認を進めましたが、総合受け入れは継続中です。**
前面化を要する物理キー・IME等と、下記の残確認を完了扱いにしていません。
UIAのInvoke・Toggle・RangeValue・TextPatternによる操作は、物理入力の代わりには数えません。
本物のゲームサーバーへの接続・登録、公開世界一覧の更新は行っていません。

UIは正式な`Microsoft.UI.Reactor 0.1.0-preview.17`を使用しています。
Model/Update/Viewと、通信・保存・native操作を分けています。
原典の実素材は利用条件付きの外部フォルダー取り込みであり、ソースやMSIXへ同梱していません。
別途制作したオリジナル基本画像だけを同梱しています。

## チャット送信後の下書き

送信済みの行を残す旧処理を変更し、送信成功時には現在行だけを除去します。
他の未送信行と発言prefixを保ち、入力revisionが進んだ下書きは古い送信完了で消しません。
確認取消・送信失敗には完了更新を出さず、IME変換中/確定中は送信と入力編集を抑止します。
nativeの選択範囲はReactor反映後に、同じ入力欄・revision・本文の場合だけ更新します。
この処理からフォーカス移動は行いません。

初期修正`25379e1`では、非同期`TextChanged`より前にcontrolled描画が古い本文を再適用できる経路に対し、
チャットの`TextChanging`でModel/hookを先に更新しました。ただし、Modelからの書き戻しも同じイベントを発火し、
進行中の古い描画値まで新たな編集としてModelへ戻す余地があります。この捕捉だけで問題が解決したとは判定しません。
登録・設定・priv・看板・通常ダイアログの編集欄にも、元の非同期controlled経路が残っていました。

初期修正時の`GameInputTests`は **Passed: 53, Failed: 0, Skipped: 0**、
Core全体は **Passed: 269, Failed: 0, Skipped: 0**。packaged x64 buildは警告0/エラー0です。
結果は `files\chat-send-regression\chat-input-targeted.trx`、`chat-core-all.trx`、同期化後の`chat-sync-core-all.trx`に保存しました。

**製品チャットのnative受け入れは未完了です。**
初期修正前の新規試験identityと所有loopbackでは、UIAによる下書き設定が送信前に空へ戻りました。
直接ValuePatternと公式WinApp set-valueの両方で送信前条件を満たせず、追加の反復は止めました。
`files\chat-send-20261115-203738\results.json`は失敗記録であり、送信後クリアの成功証拠ではありません。
取消/送信失敗/prefix caretの後続native確認と、物理Enter/IME/focusは保留です。
初期修正直後は追加のUI試行を行っていません。以前の失敗との因果は未確定です。
合成登録の削除・試験前データの復元・製品データSHA不変・正常終了0は各バッチで確認し、
所有mockも停止しました。以前の行保持native試験は変更後の合格へ読み替えません。

### Issue #4: 編集欄の同期所有権

編集可能なTextBoxは`InputBox`に統一し、Reactorの`Value`/`OnChanged`は未設定にします。
`TextChanging`から直ちにModelと`UseReducer`のhookを公開し、Modelからnativeへの反映は
`TextInputBinding.Apply`の間だけ編集通知を抑止します。描画の途中でnative編集が入った場合は、
編集epochが変わった古い描画からの代入を避け、次の描画で最新の下書きを反映します。
同文のnative代入は避け、再利用された欄のcallbackは値の反映より先に更新します。
PasswordBoxも同じ仕組みで、`PasswordProperty`の同期通知を使います。秘密をログへ記録しません。
同期イベント内の`Refresh`はhook公開だけに限定し、ログ・地図・別窓等のvisual tree操作は行いません。
既存のIME、送信成功/失敗/取消、revision、caret/focusのポリシーは変更していません。

preview.17の根拠は、配布packageに対応する
[`a61657f`](https://github.com/microsoft/microsoft-ui-reactor/tree/a61657ff404c180464061912b0c02d4a4560492a)の
`Element.cs`（TextChanged trampoline）、`PropEntry.cs`（controlled drift書き戻し）、
`RenderContext.cs`（hookの同期更新）です。Reactor本体や非公開APIは変更していません。
現行mainの独立identityによるCUA確認では、登録host/port/nameとModel由来の接続先ラベルが一致しました。
したがって過去のreadback失敗を常時再現する不具合とも、driver固有の問題とも断定していません。

`TextInputBindingTests`の5件は、受信再描画、送信中の同文へ戻る編集、進行中描画のecho、
欄の再利用時のcallback更新、代入例外後の編集を対象にしています。
入力関連のfocused回帰は **77件合格**、Core全体は **287件合格**、
x64 buildは警告0/エラー0です。これらを製品チャットの実Enter・IME・focusの合格とは扱いません。

独立identityのWinUIプローブに製品の`App.TextInputs.cs`を直接リンクし、同じpreview.17上で
同期Model/hook更新、受信相当の再描画、`ChatSent`による成功行除去、新下書き保持、
callback再利用、PasswordPropertyの同期とecho抑止の**9項目を確認**しました。
ただし旧controlled経路の反例では期待した巻き戻りが起きず、**プローブ全体の結果はFAIL**です。
その後のepoch検証項目は未実行で、過去のUIA失敗の因果が実証されたとは扱いません。
初回のプローブ起動設定不整合と、nativeの改行正規化を考慮しないassertは試験側で修正しました。
反復上限で終了し、不合格記録を成功記録へ置き換えていません。

修正版の実アプリでも、保存しない登録フォームでhost/port/nameの保持、Model由来の接続先ラベル、
手入力→一覧への切替後の絞り込み（17件→1件）と接続先保持をCUAで観測しました。
資格情報の入力・保存、設定保存、ネットワーク接続は行っていません。
両アプリは終了0、試験用2登録は解除し、永続backupを残して製品登録と3データのSHA不変を確認しました。
結果は入力同期セッションの`files\input-native-summary.json`と`native-probe-results.json`、
回帰結果は`files\regression`に保存しています。
実アプリのチャット送信wire、Enter/IME/focus/caret、priv/看板/設定のnative統合は未確認です。
この時点では[#4](https://github.com/runceel/phi-client-lab/issues/4)は未完了で、修正PRをDraftとして提出しました。

### 製品統合受け入れの追加記録（`94d81b7`）

入力同期の[#10](https://github.com/runceel/phi-client-lab/pull/10)統合後、製品コードをコピーした
独立identityと所有loopbackだけで確認しました。以下は`94d81b7`の実測であり、
その後のmain全体を確認した記録ではありません。旧controlledプローブの反例未再現によるFAILと、
製品に不具合が残っているかどうかは区別します。

登録フォームのhost/port/nameとModel由来の接続先が一致し、承認済みの合成ID1件を保存して接続できました。
受信ログ・地図の再描画中も複数行下書きが残り、送信ボタンでは現在行だけがwireへ1回届き、
未送信行が入力欄に残りました。続く送信ではprefixへ戻り、大声確認を取り消すと下書きが残り、
取消本文が送られないことを確認しました。privの開閉を跨ぐ下書き、看板下書きの再描画保持、
独立設定の倍率147%・滑らかな移動の実保存も確認しています。priv/看板本文の送信確認ではありません。
通常幅の移動パッドは↓が下中央で、下段左右は非操作領域でした。

実Enterと狭幅化はCUAが許可された操作経路を確保できず、`no_viable_candidate`で止まりました。
この結果をアプリのキー処理の不具合とも合格とも扱いません。実Enter/Tab/Shift+Tab/IME/focus/caret、
nativeボタンのSpace、長押し、狭幅、秘密の保存待ち/結果不明時の破棄確認は未確認です。
送信失敗・非同期送信中の新下書きについても、今回の製品統合経路では未確認です。
[#4](https://github.com/runceel/phi-client-lab/issues/4)と
[#5](https://github.com/runceel/phi-client-lab/issues/5)はOpenのままです。

切断・合成登録削除後のCloseでは、exit `0xC000027B`と同一試験PIDのWERイベントを観測しました。
これは正常終了ではなく、原因調査を[#18](https://github.com/runceel/phi-client-lab/issues/18)へ分離しました。
所有プロセス・mock停止、独立登録解除、製品登録と3データのSHA不変は確認済みです。
永続backupとprivate証拠は入力同期セッションの`files\acceptance-recovery`、
`acceptance-exit.json`、`acceptance-crash-events.json`、`acceptance-cleanup.json`に保存しました。
今回のfocused回帰は86件合格、x64 buildは警告0/エラー0です。これらを未確認の実キーの代用にはしません。

### Issue #18: Close後のタイトル計測

空の独立fixtureでも、終了時のprivate dumpは`AttachCenteredTitle.Layout`内のnull参照を示しました。
native stackは`DispatcherQueueController::ShutdownQueue`内で、
`XamlRoot.ContentIslandEnvironment`の戻りがnullになった直後の`AppWindowId`参照で例外が起きています。
captured ILもこの呼出し順を裏付けます。`XamlRoot`が残っていても、そのcontent islandが生きているとは限りません。
ログではCLR例外、WinRTの`80004003`、`0xC000027B`へ続いており、ソースだけからの原因推測ではありません。

[#24](https://github.com/runceel/phi-client-lab/pull/24)は、未loadのchrome、消失したcontent island、
失効したAppWindowでは計測しないように限定修正します。通常の中央寄せの数式やイベント配線は変更しません。
修正commit `aeb89ca`の新規空fixtureで、正式タイトルの表示・中央位置を確認し、
debuggerなしのClose1回で自然exit **0**、該当runのApplication Error/WER一致 **0件**を確認しました。
独立manifest/layout/所有PID/exe/binary SHAとdebugger未接続を照合し、
登録解除後も製品登録と3データのSHA不変・永続backupを確認しています。
強制cleanupした収集runの終了コードとは区別します。既存のタイトル幅回帰は6件合格、x64 buildは警告0/エラー0です。
private証拠は入力同期セッションの`files\close-capture-recovery`と`close-fixed-recovery`に保存しました。
この結果で入力/Tab/IMEの未確認項目を合格に読み替えることはありません。

## 追加の操作性改善

チャット等の編集欄・IME・モーダル・メニュー・地図幅操作を保護しながら、通常ボタンと
ログへのフォーカスでもゲームキーを受け付けるよう入力ルーティングを整理しました。
ログのコピー・全選択は維持します。物理キー/IMEの受け入れ完了を意味しません。

追加確定したTabは、主画面のチャット入力欄とログ欄を往復するモード切替です。
押し続けても往復を繰り返さず、下書きを編集せずにfocusだけ移します。Escでのログ復帰も残します。
Shift+Tabはnativeのフォーカス移動へ返し、設定・登録・priv・メニュー・リンク・スクロールバー等の
Tabを横取りしません。IME変換中/確定中は既存ガードを優先します。
ボタン類のSpace/Enterと選択UIの矢印・Home/End等もゲーム命令へ変換しません。
ログ本文の修飾付き選択操作とコピーを維持し、ログのゲーム操作キーは引き続き有効です。

独立UX最終レビューのP1は、下段の空セルを実寸の非操作Canvasへ変更し、
パッドの↓を下中央へ固定しました。保存待ち/結果不明の破棄確認だけはキャンセルを既定とし、
通常の作成・送信確認の既定操作は変更していません。
これらの修正は、チャットnative受け入れと設定4値の原値復旧の未解決を免除しません。

追加修正後の入力/登録回帰は **Passed: 72, Failed: 0, Skipped: 0**、
Core全体は **Passed: 282, Failed: 0, Skipped: 0**、x64 buildは警告0/エラー0です。
`files\tab-ux-p1-regression\focused.trx`と`core-all.trx`に保存しました。
一度の最小nativeバッチは登録完了の前提で失敗し、setter後の待機・値一致確認を加えた
最後の確認も`server-host`のnative値不一致で止まりました。チャット入力の再試行は行っていません。
パッド位置・破棄確認の既定focus・Tab/Space/Enterの実操作には到達しておらず、合格とは扱いません。
`files\ux-p1-native-20261115-211010\results.json`はcleanup失敗を含み、別途の
`recovery-results.json`で正常終了0・独立データ復元・製品データ不変を確認しました。
最後の`files\ux-p1-native-20261115-211120\results.json`はsetup失敗・cleanup合格の記録です。
追加反復を止め、所有mockを停止しました。新しい成功画面の証拠はありません。

地図の余分な縦積みを整理し、補助情報を「地図情報・保存」にまとめました。
地形記録の外周用領域は保ち、外周を使わない表示では画像の自然高まで余白を減らします。
描画比率・クリック座標は従来の倍率処理を使います。既存の窓配置は上書きしません。
3×3パッドは左旋回/北・前/右旋回、西・左/攻撃/東・右、空欄/南・後/空欄の7操作です。
中央はSpaceの攻撃で、NumPad5や斜め移動ではありません。
表示切替と50–400%の整数倍率入力・−/＋・100%復帰を置き、設定を保存します。EagleEyeにも倍率を適用します。

先行`37bc0ea`のCoreは **Passed: 253, Failed: 0, Skipped: 0**、x64 buildは警告/エラー0。
`files\ui-map-usability-20261115-185153\results.json`は所有loopback/UIAの9項目が合格しました。
北固定/回転の全9位置のwire、非表示からの再表示と保存、50/400%境界、
通常地図の125%描画寸法、EagleEyeの縦横スクロール比率、独立窓、設定復元を含みます。
同フォルダーの01–08のPNGを実装側で確認しました。独立日本語評価済みの画面には数えません。
初回は主窓にHPがあると誤認したharness、次はEagleEyeが自動表示済みなのに再表示ボタンを
期待したharnessが失敗しました。倍率の待機が1回タイムアウトした記録も保持しています。
後続の同一製品版は上下限を含めて合格しましたが、物理操作の安定性まで証明したものではありません。
狭幅での詳細欄の折り返しは追加調整し、
`files\ui-map-usability-20261115-185546\results.json`の3項目と04/05のPNGで再確認しました。
`files\usability-regression\usability-core.trx`に253件の回帰結果、
`files\usability-cleanup.json`に正常再起動後の設定/登録/素材指定の不変とTCP接続0件を記録しました。

先行の9位置パッドはその後の設計照合で上記7操作へ訂正しました。先行wire試験を
現在の9操作の保証としては扱いません。

## コンパクトな主画面・3段階登録・タイトルバー

独立UX設計とimpeccable Operate/craft-floorに沿い、通常画面をマップ＋チャットへ整理しました。
1キャラ時はTabView枠を省き、操作ガイドを一時表示、パッドを保存式の補助表示にしています。
設定の未指定時だけパッドを初期非表示にし、明示保存済みのtrue/falseは保持します。
狭幅では地図と会話を上下に配分し、世界ログに80 DIP以上を確保します。
privは狭幅で会話を切り替え、同じ狭い領域へ2つのチャットを積みません。

公式`TitleBarElement.RightHeader`、`Tall`、`AutoRefreshDragRegions`、`IsDragRegion(false)`で、
メニュー/ガイド/必要な接続状態をnativeタイトルバーへ統合しました。本文の重複操作行を除去しています。
通常高さ32 DIPと別操作行を重ねず、操作可能な48 DIPのtitle barを使います。
狭幅で中央の`Content`がクリップされた初回結果を受け、幅を確保する`RightHeader`へ一括修正しました。
最小化/最大化/閉じるはWindows標準のままです。drag/Snap/最大化・復元の物理操作や別DPI間移動を
実証したという意味ではなく、下記の手動確認を残しています。

登録は専用の世界→キャラクター→確認で、既存IDのローカル登録とは分離しました。
世界選択では通信せず、候補取得は明示操作だけ、作成時だけ秘密値を生成します。
明確な拒否/結果不明/PC保存待ち/完了を分け、結果不明に再送操作を出しません。
作成成功後の保存失敗は同一ID/秘密値/keyを保持して保存だけ再試行し、自動接続しません。
候補取得中の中止を可能にし、取消後の応答を無視します。候補応答の再描画で入力focus/scrollを戻しません。
終了は公開`ReactorApp.Exit()`へ統一し、直接Destroy/アプリExitによる既存の終了時AVを避けました。

現在のCoreは **Passed: 257, Failed: 0, Skipped: 0**、x64 buildは警告/エラー0です。
登録の保存故障/秘密の伏せ/不明時再送禁止等は`192504`バッチの前半7項目が合格しました。
`193809`の独立identityで候補取消・7操作wire・整数倍率/再表示・狭幅privまで合格し、
タイトルバーの狭幅だけ不合格。1回の一括修正後、`194037`は残るchrome/独立地図を含む5/5合格です。
これらは全体の実キー/IME受け入れや独立UX最終評価を代替しません。

**試験データ復旧の阻害:** `192504`のharnessがメニュー待機不足で中断し、設定原本をメモリ内だけに
持っていたため失われました。元の`singleWindow`/`automap`/`showMovementPad`/`mapZoom`は確定できず、
推測で復元していません。現在値を保全し、所有合成登録だけを通常UIで削除、製品appとlistenerを停止しました。
以後はWinAppの`--unique-identity`、別layout、永続事前backupで試験しています。
独立identity試験で製品settingsのSHA256不変と、合成登録除去/metadata・試験設定の復元/終了code0を確認しました。
これは失われた製品設定の復元ではありません。4値の確定と復旧は未解決のマージ阻害条件です。

## オリジナル同梱画像の統合

素材専用commit `505c89c`を`c2beffb`として取り込み、`assets\bundled-basic`のBMP14枚と
manifest/素材専用MITライセンスをアプリContentへ追加しました。既存BMP decoderで透過を1回適用し、
原典の画像や制作元PNGを加工・二重透過していません。actor12種で既定14コード、未知種別はwandererです。
通常/独立/EagleEyeの既存renderer・座標・倍率を維持しました。
外部のmapset→外部Default→同梱地形、外部items→同梱items、
既存alias/画像名/名前/既定のactor→同梱actorの順で、読み込めた外部画像を優先します。
候補プレビューは実画像だけのままで、同梱actorによる候補の偽装はしません。

packaged x64は警告/エラー0、Coreは **Passed: 257, Failed: 0, Skipped: 0**。
`files\bundled-regression\bundled-core.trx`へ結果を保存しました。
別identityの最終残確認 `files\ui-bundled-20261115-200814\results.json` は **7/7 PASS**。
package内14枚のSHA/ライセンス、未指定、外部地形/actorの優先と欠落item/actorの補完、
外部item優先、空フォルダーからの復帰、独立地図/EagleEyeとPNGを確認しました。
判定はbitmapの存在だけでなく、外部marker色と同梱画像の画素/マスクも照合しています。
独立素材担当の画像単体検証とは区別した、アプリへ組み込んだ後の確認です。

狭幅の状態行は`compact-map-scroll`の縦スクロール内へ収め、下端へ移動して
HP/MP・Poison/Panicがログ境界より上に完全表示され、チャット入力が残ることを確認しました。
証拠は `files\ui-bundled-20261115-200058\results.json` の
`NarrowMapScrollExposesCompleteConditionsWithoutHidingChat` と `02-bundled-narrow-conditions.png`。
このバッチの後続はdriver待機不足で失敗しており、バッチ全体の合格ではありません。
その他の初期driverにもSelectionItem未対応、PNGの名前ラベルを含む外側寸法の誤期待、
透過itemの空白画素での判定、円上itemの21px切り抜き範囲、複数行入力によるEagleEye要求の誤りがありました。
中断結果は保持し、該当の残確認を上記最終7/7で完了しました。物理キー/IMEの試験には数えません。

通常/狭幅の代表画像は`200058`の01/02、独立/EagleEyeは`200814`の06/07です。
EagleEyeの07画像は外側スクロール上端で、下部補助情報は既存`detached-map-scroll`内です。
独立窓の下端までのスクロール操作は今回の追加assertには含めていません。
新たなレイアウト探索は行わず、新素材を含む予定した画面を確認しました。
すべての独立試験後に合成登録を除去し、事前settings/metadata/素材指定のbytesまたは不在を復元、
正常終了code0・製品3ファイルのSHA不変・app/mock停止・listener0を確認しました。
これは前述した製品4値の復旧ではありません。

## 56項目の最終照合表

実装欄のパスは、`Core:`が`PhiClient.Core`、`UI:`が`PhiClient`、
試験欄は`PhiClient.Core.Tests`を基準とします。
「native」は後掲の所有loopback/UIA試験です。
試験ファイルの対応は各機能の根拠であり、全組合せ・全端末での合格を意味しません。

| ID | 実装・照合対象 | 試験と残確認 |
|---|---|---|
| 001 | CP932分割受信、LF/CR、置換、行上限。Core: `Protocol.cs`, `PhiConnection.cs` | `PhiConnectionTests`。byte単位分割、過大行からの復帰、login交換。 |
| 002 | login11行、TCP設定、接続・送信期限。Core: `PhiConnection.cs` | `PhiConnectionTests`で11行と送信を照合。実ネットワーク障害下の端末試験は対象外。 |
| 003 | keepalive、lag/remap、無応答案内。Core: `PhiConnection.cs` | `PhiConnectionTests`でlag/remap応答とliteral抑制。長時間の実サーバー運用は未確認。 |
| 004 | 通常・強制・server切断。Core: `PhiConnection.cs`; UI: `App.Effects.cs` | native全切断・全強制切断、Core接続試験。 |
| 005 | `#more`本文を命令・効果音にしない。Core: `Protocol.cs`, `SoundRules.cs` | `PhiConnectionTests`, `SoundRulesTests`, `WorldTransferTests`。 |
| 006 | status/conditions/attack/magic。Core: `PlayerStatus.cs`, `Protocol.cs`; UI: `GameViews.cs` | `GameDisplayTests`、native地図・超魔法の状態更新。 |
| 007 | name/user固定byte欄と更新。Core: `Protocol.cs`, `UserListRefresh.cs` | `WorldTransferTests`, `PhiConnectionTests`、native priv。 |
| 008 | 三重重なりの案内。Core: `Protocol.cs`; UI: `GameViews.cs` | parserと状態の照合。実素材の重なりは描画試験でも確認。 |
| 009 | ex-posを次の地図だけへ付ける。Core: `Protocol.cs` | `WorldTransferTests.NamesUsersAndAbsolutePositionsUseOriginalFieldsAndSingleFrameConsumption`。 |
| 010 | 7×7、O/W、属性、透過、向き、重なり。Core: `Protocol.cs`, `MapGraphics.cs` | `MapGraphicsTests`, `MapCompatibilityTests`、実素材native地図。 |
| 011 | 旧5×5別平面、変更なし、actor保持。Core: `Protocol.cs` | `GameDisplayTests`, `MapCompatibilityTests`でCR/LF/CRLFと不正actor後の保持。 |
| 012 | mapset/define、alias、cache更新。Core: `MapGraphics.cs`; UI: `App.GraphicsEffects.cs` | `MapGraphicsTests`, `ModelUpdateTests`、原典素材取り込み試験。 |
| 013 | 名前、欠落時頭文字、一覧、巨大画像。Core: `MapPresentation.cs`, `MapGraphics.cs` | `MapCompatibilityTests`、実素材native/PNG。 |
| 014 | 水際256通り、4象限、マスク、item切り抜き。Core: `MapGraphics.cs` | 独立した4象限期待表による`MapCompatibilityTests`、`MapGraphicsTests`。 |
| 015 | EagleEye、0x58、コマ0、座標。Core: `Protocol.cs`, `MapGraphics.cs` | `GameDisplayTests`, `MapCompatibilityTests`、native PNG。 |
| 016 | 方位・地域・世界、倍率、クリック座標。Core: `MapPresentation.cs`, `GameInput.cs` | `MapCompatibilityTests`, `GameInputTests`。別DPI間の実操作は保留。 |
| 017 | 世界別記録、未知・盲目除外、暗い外周。Core: `Automap.cs`, `AutomapStore.cs` | `AutomapTests`で保存・同時変更・所有loopback。 |
| 018 | 移動予測、120–1000ms、応答・失敗・warp。Core: `MovePrediction.cs` | `MovePredictionTests`, `AutomapTests`。追加の移動命令が発生しないことも確認。 |
| 019 | 長押し待機・repeat・smoothMove。UI: `App.Input.cs`; Core: `MapMotion.cs` | `MapMotionTests`で90msと描画。物理長押し・repeatは保留。 |
| 020 | リスト番号・ページ・選択・待機・取消。Core: `ChoiceSelection.cs`, `GameInput.cs` | `GameInputTests`、native privバッチでページの保持と番号。待機表示の実画面は未確認。 |
| 021 | 非番号行の保持、list終了、他送信での終了。Core: `Protocol.cs`, `ChoiceSelection.cs` | `GameInputTests`でraw・offset・継続境界・空list。 |
| 022 | 装備解除、隠すlist、期限。Core: `PhiConnection.cs` | `PhiConnectionTests`で中間listだけの抑制、期限後の次のlist保持。 |
| 023 | 魔法別再装備、実際に外した武器名。Core: `PhiConnection.cs`, `SettingsStore.cs` | `PhiConnectionTests`, `SettingsTests`で対象選択・設定無効・確認行。 |
| 024 | 系統魔法、検索、召喚、金額、確認。Core: `GameInput.cs`; UI: `App.Dialogs.cs` | 入力解決・確認のCore試験。全dialogの物理キーは保留。 |
| 025 | XML超魔法、順序、変数、隠すlist、資格。Core: `MasterMagic.cs`; UI: `App.MasterEffects.cs` | `MasterMagicTests`, `PhiConnectionTests`、未改変原典XMLのnative 9項目。 |
| 026 | フルキー・修飾キー・list中の操作。Core: `GameInput.cs`; UI: `App.Input.cs` | `GameInputTests`。実キーによるfocus・修飾キーの経路は保留。 |
| 027 | F8–F12、list用shortcut、Alt+G、literal改行。Core: `SettingsStore.cs`, `GameInput.cs` | 設定往復と入力優先順位。実キー経路は保留。 |
| 028 | Enter/Shift/Ctrl、成功行の除去・未送信行保持、空行、大声保護。Core: `GameInput.cs`, `GameModel.cs`; UI: `App.ChatEffects.cs` | `GameInputTests`で成功行除去・入力revisionによる新下書き保護。送信済み行を残す原典互換動作は利用者指定で変更。実キー経路は保留。 |
| 029 | IME確定Enterの抑制、編集中のゲームキー除外。UI: `App.Input.cs`, `App.TagInput.cs` | 実装・ビルド確認。日本語IMEの実変換・確定は保留。 |
| 030 | 大声今回/継続、全タブ発言、prefix。UI: `App.ChatEffects.cs` | `GameInputTests`、所有2タブnative 9項目。 |
| 031 | 複数行看板、空行、終端、取消、server終了。Core: `GameInput.cs`; UI: `App.Game.cs` | 入力・protocol照合、nativeタグ挿入と看板取消。全server終了状態の実画面は未確認。 |
| 032 | priv一覧・番号・複数宛先・self echo・不明相手。Core: `Priv.cs`, `UserListRefresh.cs` | `PrivTests`, `PhiConnectionTests`、native priv 9項目。 |
| 033 | 参加者組別priv、多人数初回返信確認。Core: `Priv.cs`; UI: `App.PrivEffects.cs` | `PrivTests`、native取消・下書き保持・承認後の送信。 |
| 034 | reserve/trans/ok、移動先へopen再送なし。Core: `PhiConnection.Transfer.cs` | `WorldTransferTests`で成功・両側拒否・literal。追加native 5項目で予約待ち/移動完了/設定10行も確認。 |
| 035 | 移動先確認、失敗時の行保持、接続先保存。UI: `App.TransferEffects.cs`, `App.Effects.cs` | `WorldTransferTests`で未承認先への接続抑制、行順と世代。追加nativeで移動中の送信拒否、同じ合成登録への移動先保存を確認。別ホスト確認画面のnative操作は未確認。 |
| 036 | 自動生成選択時、明示作成で暗号学的6文字生成、本人確認。Core: `Protocol.cs`, `RegistrationForm.cs`; UI: `App.Effects.cs` | `PhiConnectionTests`, `RegistrationFormTests`、native生成/伏せ/保存再試行。新3段階の独立最終評価は未実施。 |
| 037 | Unicode文字数、禁止文字、メール任意。Core: `Protocol.cs` | `PhiConnectionTests.RegistrationUsesOriginalUnicodeValidationAndCryptographicAlphabet`。 |
| 038 | REGINFO IMG、0基準番号、実画像。Core: `PhiConnection.cs`; UI: `App.RegistrationEffects.cs` | `RegistrationGraphicsTests`、native 5項目。取得は一覧要求だけでlogin/作成なし。 |
| 039 | 3段階作成・拒否・結果不明・PC保存のみ再試行。UI: `App.RegistrationFlow.cs`, `App.Effects.cs`, `CharacterStore.cs` | `RegistrationFormTests`、native保存故障/同一ID/key/要求1回/自動接続なし/不明時再送なし。製品設定の復旧未完は別の阻害条件。 |
| 040 | 世界一覧取得元、cache/import/選択/失敗。Core: `WorldDirectoryClient.cs`, `WorldList.cs` | `WorldListTests`、native 9項目。公開一覧更新なし。 |
| 041 | 複数タブ、登録選択、全接続/切断/強制。UI: `App.Effects.cs` | 所有2タブnative 9項目。既存の実プロフィールを接続対象にしていない。 |
| 042 | Philly登録・設定取り込み、秘密の保管。Core: `PhillyProfiles.cs`, `SettingsStore.cs`; UI: `App.ProfileImportEffects.cs` | `PhillyProfilesTests`、native 6項目。保存故障・同名置換・元データ保持。 |
| 043 | 設定保存/復元、失敗表示。Core: `SettingsStore.cs`; UI: `App.SettingsEffects.cs` | `SettingsTests`、native設定取り込み4項目、地図幅保存。 |
| 044 | raw/time/channel/from、表示上限。Core: `LogEntry.cs`, `ClientModel.cs` | `LogEntryTests`, `ModelUpdateTests`、nativeログ保存。 |
| 045 | 色・size・font・style・close・br・語置換。Core: `LogMarkup.cs`; UI: `LogPresenter.cs` | `LogMarkupTests`、native上線・折返し・選択、変換後HTML桁数。 |
| 046 | URL/画像、HTMLを実行しない、非公開宛先抑制。Core: `LogImageClient.cs`, `LogImages.cs`; UI: `App.LogImageEffects.cs` | 画像関連3試験ファイル、別identityのnative静止画/GIF fixture。製品の任意外部画像を試す通信はしていない。 |
| 047 | 行コピー、タグ/時刻/名前の除去。Core: `LogFormat.cs`; UI: `LogPresenter.cs` | `LogEntryTests`、nativeコピー・範囲選択。幅だけの変更後も選択を保持。 |
| 048 | 入力/看板のタグ、選択とカーソル。Core: `TagEditing.cs`; UI: `App.TagInput.cs` | `TagEditingTests`、native 5項目。物理右クリック・IME中の実入力は保留。 |
| 049 | 自動/HTML/text/buffer保存、folder、follow。Core: `LogStore.cs`; UI: `App.LogEffects.cs`, `App.ShutdownEffects.cs` | `LogStoreTests`、nativeログ・終了保存故障/復旧。folderを実際に開く操作は残確認。 |
| 050 | SoundRegexp/File、NotFlash、前面抑制。Core: `SoundRules.cs`, `SoundFiles.cs`; UI: `App.SoundEffects.cs` | 純粋/CP932試験、native 6項目で失敗通知・復元。正常WAV実音、前面/背面通知は保留。 |
| 051 | 外部素材優先、同梱基本画像、reload、欠落、利用条件。Core: `MapGraphics.cs`; UI: `GraphicsStore.cs`, `App.GraphicsEffects.cs` | 描画試験、`tools\Test-WphiGraphics.ps1`、実素材native。同梱統合は`200814`の7項目と外部marker/同梱画素の照合。素材設定の独立評価は未提供。 |
| 052 | 通常/EagleEye PNG、folder。UI: `App.MapEffects.cs` | native 5項目、独立PNGデコード・寸法/透明画素・目視。保存先を開く操作は残確認。 |
| 053 | 独立/統合窓、配置、統合幅。UI: `App.MapWindow.cs`, `App.MapLayoutEffects.cs`, `MapPaneSplitter.cs`, `App.ShutdownEffects.cs` | native 9項目と製品主窓のprocess跨ぎ配置。別identity配置fixtureも確認。地図窓の再起動跨ぎ配置・reset肯定実行・別DPIドラッグは残確認。 |
| 054 | 版情報、明示的更新確認、時刻trace。Core: `ProductRelease.cs`, `DiagnosticTrace.cs`; UI: `App.DiagnosticsEffects.cs` | 両Core試験、native 5項目。公開Releasesの実取得・外部ページ起動は試験対象外。 |
| 055 | 初回/3段階登録/接続/切断/失敗/busy、通常はマップ＋チャット。UI: `App.RegistrationFlow.cs`, `App.Game.cs`, `App.cs`等 | 既往R1–R12/N1/P2。新3段階・48 DIP nativeタイトルバー・候補取消・loading/error/保存失敗/完了の実装側UIA。独立最終評価は未実施。 |
| 056 | 狭幅、DPI、IME、focus、読み上げ。UI: 各Viewとnative peer | 520×720px/144DPIで会話80 DIP以上、タイトルバーcommand/caption非重複。物理IME/focus、drag/Snap、複数DPI、高contrast、読み上げ実音は保留。 |

## 直近の修正とUIAのA/B

`30b96a7`で統合地図の幅を200–1600 DIPに調整・保存できるようにしました。
ログ用の領域を残して現在の窓幅へ収め、窓を狭めたときも希望幅を保持します。
native ThumbとRangeValue peerを使い、左右/Home/Endの処理を実装しています。
独立地図窓は実際の表示幅へ追従します。幅だけの変更でログを作り直さず、
native範囲選択を保持します。

同じ所有モック/UIA手順で、pool有効時は地図幅の変更後に設定のUIA子孫が欠け、
保存ボタンも取得できませんでした。画面自体には表示されていました。
深さ/ノード数制限なしの別プロセスによる列挙でも21要素にとどまりました。
部分一致の修正、UI threadへのqueue、leaf peer、Viewのkeyでは回復しませんでした。
根拠のなかった設定Viewのkeyは除去しました。

主窓だけ、正式な公開API
`configure: host => host.Reconciler.Pool.Enabled = false`へ変更すると、
同条件の9項目が合格しました。設定子孫/保存操作、幅保存、実viewport、
ログ選択保持、独立窓復帰、同HWND、地図close/reopen、後片付けを確認しています。
内部のpeer親参照が残るという説明は原因仮説であり、内部原因を確定したものではありません。

独立窓は独立したhost/Reconciler/poolを持つため、無効化は主窓へ限定しています。
根拠は公式固定source
[`ReactorApp.cs`](https://github.com/microsoft/microsoft-ui-reactor/blob/a61657ff404c180464061912b0c02d4a4560492a/src/Reactor/Hosting/ReactorApp.cs#L749)
ほかの公開構造です。reflection/非公開APIは使用していません。
pool無効化ではcontrolの新規割当が増える可能性があります。
性能ベンチマークは未実施であり、性能改善とは主張しません。

## ログの閲覧位置と新着追随（#8）

世界ログと内緒話で同じ`LogViewport`を使い、閲覧位置と追随状態は
キャラクター・会話ごとの`LogScrollState`に保持します。ユーザーのスクロールと、
アプリの末尾移動・折返し・画像サイズ変更によるレイアウト更新を区別します。
残存する行ID・文字位置・画面内の高さをアンカーにし、削除済みの行は残存先頭へ収めます。
新着追加だけでは既存のParagraph/Runを作り直しません。

無通信fixtureへ実`LogPresenter`/`LogViewport`をリンクし、nativeの20項目を確認しました。
位置保持、新着、末尾復帰、折返し、画像の遅延表示、先頭削除、別ログ/再マウント、
全体追随設定、選択と下書きfocus保持が対象です。CUAで実PageUp→新着の位置維持、
End→新着の追随とボタンの表示/非表示も確認しました。
証拠は当該作業セッションの`files\log-scroll-fixture\native-results.json`と`report.md`です。
専用identity/layoutを実登録・PID・exeまで照合し、終了後の登録/データ消失を確認しています。
製品の保存先・資格情報・実ゲーム接続は使っていません。
これは製品全体の入力routing・Tab・IME、物理ホイール/タッチ、
読み上げソフトによる発話の受け入れ完了を意味しません。

## native証拠

次は作業セッション成果物内の相対パスです。原典素材をソースやパッケージへ同梱したという意味ではありません。
以下の過去成功バッチでは設定復元と合成登録の削除を確認しました。
後続`192504`の製品設定復元未完を、これらの過去結果で上書きしません。
結果形式はarrayまたは`results`配列なので、どちらも確認対象にしています。

| 証拠 | 合格した範囲 |
|---|---|
| `files\ui-native-ux-20261115-192504\results.json` | 新登録等の前半7項目合格、後半はメニュー待機不足で中断、cleanup失敗。合格バッチではない。 |
| `files\ui-native-ux-20261115-193809\results.json` | 別identityで候補取消/応答時scroll、明示接続、7パッドwire、整数倍率/再表示、狭幅会話/priv合格。狭幅titlebar不合格、cleanup/終了0は合格。 |
| `files\ui-native-ux-20261115-194037\results.json` | 別identityの最終5/5。明示接続、狭幅map/chat/priv、titlebar/caption、独立地図150%/viewport、試験設定復元/終了0。製品settings hash不変。 |
| `files\ui-bundled-20261115-200814\results.json` | 同梱統合の最終残確認7/7。package SHA、外部優先/欠落補完、空フォルダー指定保持、独立/EagleEyeとPNG、独立3ファイル復元/製品不変/終了0。 |
| `files\ui-bundled-20261115-200058\results.json` | 狭幅の状態行下端到達は個別合格。後続driverの失敗を含むため全体合格ではない。 |
| `files\ui-map-windows-20261115-163817\results.json` | 9項目。主窓pool無効版の幅・地図窓・設定・ログ選択・復元。 |
| `files\ui-alltabs-20261115-164117\results.json` | 9項目。2タブ発言、大声、prefix、全接続/切断/強制、取消、復元。 |
| `files\ui-master-20261115-164545\results.json` | 9項目。原典XML、資格、順序、整数/文字、危険操作取消、不正整数拒否、復元。hover/focus/物理キーなし。 |
| `files\ui-settings-copy-20261115-145837\results.json` | 4項目。N1全文表示、取り込みだけでは未保存、保存による適用、元の設定とModelの復元。 |
| `files\ui-shutdown-20261115-135447\results.json` | 5項目。ログ保存故障、終了保留/記録保持、障害除去後のHTML救済保存、process終了と再起動。地図保存の故障注入ではない。 |
| `files\ui-world-directory-20261115-091552\results.json` | 9項目。取得元、import/cache、選択、失敗。 |
| `files\ui-priv-20261115-112435\results.json` | 9項目。宛先、初回確認取消/送信、下書き、コピー、未解決相手。 |
| `files\ui-profile-import-20261115-124849\results.json` | 6項目。登録取り込み、置換、保存故障、既存データ保持。 |
| `files\ui-reginfo-20261115-113653\results.json` | 5項目。一覧要求、番号、実素材、欠落、古い結果破棄。 |
| `files\ui-tags-20261115-130024\results.json` | 5項目。発言/看板タグ、カーソル、取消。 |
| `files\ui-maps-20261115-131249\results.json` | 5項目。通常/EagleEye PNGと画素/寸法。 |
| `files\ui-sounds-20261115-133643\results.json` | 6項目。設定、CP932、native decode失敗通知、復元。正常WAV実音の証明ではない。 |
| `files\native-log-animation\gif-results.json` | 4項目。別identityのnative GIF連続表示で赤/青のコマ変化。 |
| `files\native-log-animation\results.json` | 5項目。別identityのnative静止画fixture。 |
| `files\native-log-images\placement-results.json` | 1項目。別identity窓の破棄/再生成後の位置/寸法。製品窓の再起動試験とは区別。 |
| `files\native-transfer-verified\results.json` | 5項目。所有2 listener、予約応答保留中の表示、発言の拒否、同じIDのreserve/trans、open再送なし、接続先保存、復元。 |
| `files\ui-registration-save-20261115-165802\results.json` | 5項目。空欄パスワードの作成、metadata一時ファイルlockによる保存失敗、ID保持、作成要求1回のまま再保存、伏せた6文字の完了画面、設定/metadata/draft復元。 |
| `files\final-regression\placement-after-fix.json` | `f693173`で、製品主窓の通常終了/packaged再起動を跨いで304,304px・1800×1275pxが一致。 |
| `files\final-regression\final-core.trx` | Core Passed: 242, Failed: 0, Skipped: 0。x64ビルドは警告/エラー0。 |

地図幅の失敗結果`ui-map-windows-20261115-161933/162406/162724/162959`と、
以前の会話/超魔法の失敗結果も保持しています。後続の合格結果で、失敗記録を消していません。
追加の転送試験はボタンの無効化を誤って期待した初回、登録試験は伏せたread-only欄を
PasswordBoxと誤認した初回と正常切断`#x`を数え落とした回を保持しています。
実際の送信拒否、6文字の伏せ字、原典どおりの切断を検査するようharnessを修正しました。
登録の初回に残った合成登録は対象key/名前/所有endpointを照合して別途削除し、
最終バッチでは通常cleanupまで合格しています。

代表画像は`files\ui-map-windows-20261115-163817`の
`01-separate-map.png`、`02-integrated-map.png`、`03-resized-map.png`、
`04-minimum-map.png`です。画像と併せてPCGL原文、適用範囲、作者表記、
素材README、寿司素材のMIT表記を同フォルダーへ保存しています。
公開・共有するときもこれらの条件を維持してください。
追加画像は`files\native-transfer-verified\01-transfer-wait.png`と
`02-transfer-complete.png`、`files\ui-registration-save-20261115-165802`の
`01-created-local-save-failed.png`と`02-local-save-retried-password-masked.png`です。
これら4枚も実装側で目視しました。ID/パスワードの実値は含めていません。

最後の通常再起動で主窓が76pxずれることを検出し、`f693173`で修正しました。
データflush後、native `AppWindow.Destroy`の前に公式`SavePlacement`を明示的に呼びます。
修正後の製品process間で位置/寸法の一致を確認しました。
失敗時の`files\final-regression\restart-cleanup.json`も保持しています。
同ファイルでは保存済みsettings/characters/素材指定がbyte単位で不変だったことも確認しています。

## 日本語UX評価の範囲

impeccableのnative Operate方針に従い、既存の画面・素材・操作を改善しました。
文言は「ここで何を伝えるか」を整理し、周囲の表示を読んだうえで、
口頭ならどう説明するかを考え、条件を保って短くしています。

| 指摘 | 反映した内容 |
|---|---|
| R1 | privの確認待ち・相手番号取得・送信中を状態別に分けました。一括置換ではありません。 |
| R2 | 終了保存失敗を日本語の主通知にし、記録保持と再試行を案内しました。 |
| R3 | ゲームサーバー入力の修正動作とラベルを揃えました。 |
| R4 | 未暗号化TCPとID/登録情報の注意を日本語で保持しました。 |
| R5–R10 | ボタン名と動作を一致させ、実装者用語を利用者の操作へ置き換えました。取得元ごとの説明と重要な制約は維持しました。 |
| R11 | 提供画面の基本操作ラベルを日本語へ統一しました。全メニューを翻訳済みという意味ではありません。 |
| R12 | 狭い登録画面の世界一覧説明を折り返し、接続条件も全文へ到達できるようにしました。 |
| N1/P2 | 「設定を編集画面へ取り込みました。『設定を保存』で適用します。」とし、取り込みと適用を区別しました。 |

`552c33c`の提供17画面について、独立評価者はR1–R12の重大な指摘箇所を
改善済みと判定しました。`9b2394a`でN1/P2を反映しています。
独立評価は画面と文言の確認であり、通信やファイル保存の試験結果ではありません。
保存結果は上記の実装側UIAバッチで別に確認しました。

たとえば「設定を取り込む」だけでは適用時点が分かりにくいため、
「編集画面へ取り込み、『設定を保存』で適用する」と行動を分けました。
privも単なる「処理中」ではなく、その時点の確認/宛先取得/送信を表示します。
未暗号化通信やID送信の注意は、否定形を一律に削る対象にはしていません。

実送信中、待機/世界移動、素材設定、作成完了/部分保存失敗、設定下部、
実キー/IME等の未提供状態を、独立レビュー済みには数えません。
独立の一括再確認は終了しており、追加の微調整レビューは依頼していません。

## 原典との差異と受け入れの残確認

安全上の入力/画像/通信/設定サイズ上限、非公開宛先/redirect/共有パスの拒否、
HTMLを実行しない処理はREADMEに記載しています。
原典と完全同一という意味ではなく、意図した差異です。
移植版の更新確認はこのリポジトリの公開Releasesを対象とし、wphiの配布物を導入しません。

他スキンとactor状態装飾は、以前の残差記述を訂正しました。
原典`src-tauri/src/assets.rs:36–37`はMine固定で、
`ui/src/drawMap.ts:262–305`もstatusによる画像装飾をしていません。
wphiにある機能を対象から外したという判断ではありません。

残る実操作は次のとおりです。前面ガードを回避する代替操作は行いません。

| 残確認 | 必要な操作と影響 |
|---|---|
| 製品の試験設定復旧 | `singleWindow`/`automap`/`showMovementPad`/`mapZoom`の試験前値を利用者が確定して復元。未確定のため推測していない。表示方式・地形記録・パッド・倍率に影響。新たな製品identity試験は禁止し、別identityと永続backupを使う。 |
| 新構成の独立UX最終確認 | 最新の通常/狭幅主画面、登録3段階・保存待ち/不明/完了を一度のまとめた確認へ渡す。既往17画面評価を新構成の合格としない。 |
| 物理キー/IME/focus | 所有モックへ接続し、日本語変換の確定Enterで送信されないこと、Esc/Tab、修飾移動、F8–F12/Alt+G、list優先、看板/priv/タグ編集中の競合、長押しを実キーで確認。REQ-019/026–029/048/056の受け入れに影響。 |
| 音声/通知 | 利用可能なローカルWAVで正常再生、利用者の前面/背面切替で点滅抑制を確認。REQ-050の受け入れに影響。 |
| 配置/DPI/アクセシビリティ | 製品の地図窓の再起動跨ぎ配置、resetを承認して現在画面への復帰、統合境界の物理ドラッグ、別DPI間移動、高contrastと読み上げを確認。主窓の通常再起動配置は修正/確認済み。REQ-053/056の受け入れに影響。 |
| タイトルバーの実操作 | 空白/タイトルからdrag、最大化captionのhoverでSnap、最大化/復元/最小化、Alt+Spaceとkeyboard/menu focusを確認。標準captionの存在/対応可能状態/狭幅非重複はUIA確認済みだが、物理経路は未確認。 |
| 残る所有native状態 | list待機、priv実送信中、別ホスト確認の製品画面。世界移動の予約待ち/成功後の保存と、登録成功後の保存失敗/再試行は今回の追加5項目ずつで確認済み。独立レビューへは追加していない。 |
| その他の未実施操作 | 保存先folderの起動、更新ページの実起動、dialog中の独立窓操作抑制。実装と試験済み範囲を区別する。 |

ユーザー不在による前面化の制約は未確認として記録します。
必須checksが無いことやGitHub上のmergeable判定だけを、上記の受け入れ完了とは扱いません。
未解決の今回要件を残したままマージする判断は行っていません。

## 再実行と終了状態

リポジトリ内で再実行できる回帰コマンドです。Coreとアプリの共有output更新が重ならないよう、
順番に実行します。

```powershell
dotnet test .\PhiClient.Core.Tests\PhiClient.Core.Tests.csproj --no-restore --nologo -v:q
dotnet build .\PhiClient\PhiClient.csproj -p:Platform=x64 --no-restore --nologo -v:q
```

native harnessは利用者のprofileへ接続しないため、固定された作業セッションと
所有endpoint manifestを照合する専用手順です。任意のサーバーや既存プロフィールへ
流用しないでください。`ui-chat.ps1 -MapWindowsOnly`、`ui-alltabs.ps1`、
`ui-master.ps1`、`ui-transfer.ps1`、`ui-registration-save.ps1`の実行記録を保存しています。

試験用サーバー、製品アプリ、独立identityの試験アプリは停止しています。
合成登録と保存故障用ファイルを片付け、元の実登録と実素材指定は保持しました。
製品settingsの4値は前述のとおり復元未完、現在値を別途保全しています。
PRはDraft、IssueはOpenのままです。物理キー/IME等の受け入れを完了扱いにせず、
阻害条件を付けて引き継ぎます。
