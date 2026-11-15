using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private void BeginRegistration(bool create)
    {
        Dispatch(new ClientMessage.RegistrationStarted(create));
        showCharacters = true;
        notice = "";
        QueueRegistrationFocus(create ? "world-filter" : "character-name");
    }

    private void EditRegistration(RegistrationForm value) => Dispatch(new ClientMessage.RegistrationChanged(value));

    private Element RegistrationView()
    {
        var form = model.Registration;
        var terminal = !form.CanEdit;
        var errors = RegistrationForm.Errors(model.Draft, form.NewCharacter,
            form.Stage == RegistrationStage.World, form.CustomPassword);
        Element content = terminal ? RegistrationResultView() :
            !form.NewCharacter ? VStack(8, RegistrationWorldView(),
                InputBox(name, value => name = value, header: "表示名（このPC用）").AutomationId("character-name"),
                RegistrationError("character-name", errors),
                SecretInputBox(id, value => id = value, "キャラクターID").AutomationName("キャラクターID").AutomationId("character-id"),
                RegistrationError("character-id", errors),
                Caption("IDをこのPCのWindows資格情報マネージャーに保存します。サーバーへの作成要求は送りません。")
                    .TextWrapping(TextWrapping.Wrap)) :
            form.Stage == RegistrationStage.World ? RegistrationWorldView() :
            form.Stage == RegistrationStage.Review ? RegistrationReviewView() : RegistrationCharacterView(errors);
        var step = form.Stage == RegistrationStage.World ? 1 :
            form.Stage is RegistrationStage.Character or RegistrationStage.Rejected ? 2 : 3;
        return FlexColumn(
            VStack(4, TextBlock(form.NewCharacter ? "キャラクターを新規作成" : "既存のキャラクターIDを登録").FontSize(20),
                form.NewCharacter ? Caption($"{step} / 3　世界 → キャラクター → 確認")
                    .AutomationId("registration-progress") : Empty()).Flex(shrink: 0),
            ScrollViewer(VStack(8, content,
                form.ShowErrors && errors.Count > 0
                    ? TextBlock($"入力を確認してください（{errors.Count}項目）。").TextWrapping(TextWrapping.Wrap)
                    : Empty()))
                .AutomationId("registration-scroll").Flex(grow: 1, basis: 0)
                .OnMount(element => AttachReadableScroll((Microsoft.UI.Xaml.Controls.ScrollViewer)element)),
            RegistrationFooter().Flex(shrink: 0)
        ).Width(Math.Max(240, Math.Min(720, windowWidth - 40))).HorizontalAlignment(HorizontalAlignment.Center)
            .WithKey($"registration-{form.NewCharacter}-{form.Stage}");
    }

    private Element RegistrationWorldView()
    {
        var form = model.Registration;
        var worlds = model.Worlds;
        var rows = worlds.Items.Where(world => world.Name.Contains(worlds.Filter, StringComparison.OrdinalIgnoreCase) ||
            world.Host.Contains(worlds.Filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        var selected = Array.FindIndex(rows, world => world.Host == host && world.Port.ToString() == port);
        var errors = RegistrationForm.Errors(model.Draft, form.NewCharacter, worldOnly: true);
        return VStack(6,
            TextBlock("接続先の世界").FontSize(16),
            HStack(8,
                RadioButton("一覧から選ぶ", !form.ManualEndpoint,
                    value => { if (value) EditRegistration(form with { ManualEndpoint = false }); }, "endpoint"),
                RadioButton("手入力する", form.ManualEndpoint,
                    value => { if (value) EditRegistration(form with { ManualEndpoint = true }); }, "endpoint")),
            form.ManualEndpoint ? VStack(6,
                InputBox(host, value => host = value, header: "ゲームサーバー（ホスト名 / IPアドレス）").AutomationId("server-host"),
                InputBox(port, value => port = value, header: "TCPポート").AutomationId("server-port")) :
            VStack(4,
                InputBox(worlds.Filter, value => Dispatch(new ClientMessage.WorldFilterChanged(value)),
                    header: "世界名・アドレスで絞り込み").AutomationId("world-filter"),
                ListView(selected, index =>
                {
                    if (index >= 0 && index < rows.Length) Dispatch(new ClientMessage.WorldSelected(rows[index]));
                }, rows.Select(world => (Element)VStack(2,
                    TextBlock(world.Name).TextWrapping(TextWrapping.Wrap),
                    Caption($"{world.Host}:{world.Port}").TextWrapping(TextWrapping.Wrap))).ToArray())
                    .Height(Math.Clamp(windowHeight * .24, 120, 220)).AutomationId("world-list"),
                Caption($"取得元: {worlds.Source}　表示: {rows.Length} / {worlds.Items.Length}件")
                    .TextWrapping(TextWrapping.Wrap).AutomationId("world-source")),
            TextBlock(host.Length == 0 ? "世界を選ぶか、接続先を手入力してください。" : $"選択中: {host}:{port}")
                .TextWrapping(TextWrapping.Wrap).AutomationId("selected-world"),
            Caption("選ぶだけでは接続・作成しません。一覧の内容や、掲載サーバーの安全性・稼働状況は保証されません。")
                .TextWrapping(TextWrapping.Wrap),
            RegistrationError("server-host", errors),
            Expander("世界一覧の更新・取り込み", VStack(6,
                TextBlock(worlds.Message).TextWrapping(TextWrapping.Wrap).AutomationId("world-status"),
                InputBox(worlds.Url, value => Dispatch(new ClientMessage.WorldUrlChanged(value)),
                    header: "一覧の配布URL（LZH / CP932 INI）").AutomationId("world-source-url"),
                Button("HTTP 一覧を更新", () => _ = LoadWorldsAsync(false)).AutomationId("world-refresh"),
                Button("INI ファイルから取り込む", () => _ = LoadWorldsAsync(true)).AutomationId("world-import"),
                TextBlock((worlds.Url.Trim() == WorldList.DownloadUrl
                    ? "wphiの世界一覧は、HTTPで配布されるresist.lzhから取得します。"
                    : "指定した配布URLから、HTTPまたはHTTPSで世界一覧を取得します。") +
                    "一覧の取得とゲームサーバーへの接続は別の操作です。").TextWrapping(TextWrapping.Wrap))
                .IsEnabled(!busy && !worlds.Loading)).AutomationId("world-management")
        ).IsEnabled(!model.RegistrationGraphics.Loading);
    }

    private Element RegistrationCharacterView(IReadOnlyDictionary<string, string> errors) => VStack(6,
        TextBlock($"作成先: {host}:{port}").TextWrapping(TextWrapping.Wrap),
        model.Registration.Stage == RegistrationStage.Rejected
            ? TextBlock(model.Registration.Message).TextWrapping(TextWrapping.Wrap) : Empty(),
        InputBox(name, value => name = value, header: "名前（世界へ送る名前・このPCの表示名）").AutomationId("character-name"),
        RegistrationError("character-name", errors),
        RegistrationForm.WireText(name) != name
            ? TextBlock($"この世界にはCP932で「{RegistrationForm.WireText(name)}」と送信されます。名前を確認してください。")
                .TextWrapping(TextWrapping.Wrap) : Empty(),
        CheckBox(model.Registration.CustomPassword, value =>
        {
            EditRegistration(model.Registration with { CustomPassword = value });
            password = "";
        }, "パスワードを自分で指定する（6文字）").AutomationId("custom-password"),
        model.Registration.CustomPassword
            ? SecretInputBox(password, value => password = value, "パスワード").AutomationName("6文字のパスワード").AutomationId("new-password")
            : Caption("最後に「この世界に作成する」を押したとき、6文字のパスワードを自動生成します。")
                .TextWrapping(TextWrapping.Wrap),
        RegistrationError("new-password", errors),
        RegistrationForm.WireText(password) != password
            ? Caption("このパスワードにはCP932で表せない文字があります。対応する文字で入力するか、自動生成を選んでください。")
                .TextWrapping(TextWrapping.Wrap) : Empty(),
        Expander("メールアドレス（任意）", VStack(4,
            InputBox(email, value => email = value, header: "メールアドレス").AutomationId("registration-email"),
            RegistrationError("registration-email", errors))),
        InputBox(image, value => { image = value; _ = PrepareRegistrationPreviewAsync(); },
            header: "キャラクター画像の番号（0から）").AutomationId("initial-image"),
        RegistrationError("initial-image", errors),
        RegistrationGraphicsView());

    private Element RegistrationReviewView() => VStack(8,
        TextBlock("送信する内容を確認").FontSize(16),
        TextBlock($"世界: {host}:{port}\n名前: {name.Trim()}\n画像番号: {image}\n" +
            $"パスワード: {(model.Registration.CustomPassword ? "指定した6文字（非表示）" : "作成時に自動生成")}\n" +
            $"メール: {(email.Length == 0 ? "送信しない" : email)}").TextWrapping(TextWrapping.Wrap),
        TextBlock("認証情報とゲームデータは、暗号化されずにTCPで送信されます。信頼できるサーバーだけで作成してください。")
            .TextWrapping(TextWrapping.Wrap).AutomationId("registration-security"),
        Caption("作成後はこのPCにIDを保存します。ゲームには自動接続しません。").TextWrapping(TextWrapping.Wrap));

    private Element RegistrationResultView()
    {
        var form = model.Registration;
        return VStack(8,
            TextBlock(form.Stage switch
            {
                RegistrationStage.Creating => $"「{name}」を{host}:{port}に作成しています…",
                RegistrationStage.SavePending => "世界での作成は完了。このPCへの保存は未完了です。",
                RegistrationStage.Complete => form.NewCharacter
                    ? "世界で作成し、このPCに保存しました。まだログインしていません。" : "このPCに接続情報を保存しました。",
                _ => "作成要求後に結果を確認できませんでした。作成済みの可能性があります。"
            }).TextWrapping(TextWrapping.Wrap).AutomationId("registration-result"),
            TextBlock(form.Message).TextWrapping(TextWrapping.Wrap).AutomationId("registration-result-detail"),
            form.Stage == RegistrationStage.SavePending ? VStack(6,
                Caption("IDと作成時のパスワードをこの画面で保持しています。保存先を確認して再試行してください。" +
                    "再試行はPCへの保存だけを行い、世界への作成要求は再送しません。").TextWrapping(TextWrapping.Wrap),
                Button("作成済みIDを確認する", () => _ = ShowRegistrationSecretAsync(false)),
                Button("作成時のパスワードを確認する", () => _ = ShowRegistrationSecretAsync(true))) : Empty(),
            form.Stage == RegistrationStage.Unknown
                ? VStack(6, TextBlock("自動では再送しません。世界の管理者などに作成状況を確認してください。" +
                    "同じ内容を再作成すると、重複する可能性があります。").TextWrapping(TextWrapping.Wrap),
                    Button("作成時のパスワードを確認する", () => _ = ShowRegistrationSecretAsync(true))) : Empty());
    }

    private Element RegistrationFooter()
    {
        var form = model.Registration;
        if (form.Stage == RegistrationStage.Complete)
            return HStack(8,
                Button("キャラクター一覧へ", () => _ = EndRegistrationAsync()).AutomationId("registration-done"),
                Button("接続する", () =>
                {
                    if (form.Character is not { } character) return;
                    Dispatch(new ClientMessage.RegistrationEnded());
                    _ = PerformAsync(() => OpenAsync(character));
                }).AccentButton().IsEnabled(!busy).AutomationId("registration-connect"));
        var cancel = Button(form.Stage == RegistrationStage.SavePending ? "破棄して閉じる…" :
                    form.Stage == RegistrationStage.Unknown ? "入力情報を破棄して閉じる…" : "中止",
                () => _ = EndRegistrationAsync()).IsEnabled(!busy && form.Stage != RegistrationStage.Creating)
                .AutomationId("registration-cancel");
        var next = form.Stage is RegistrationStage.Unknown or RegistrationStage.Creating ? Empty() :
                Button(form.Stage == RegistrationStage.SavePending ? "このPCへの保存を再試行" :
                        !form.NewCharacter ? "このPCに保存する" :
                        form.Stage == RegistrationStage.Review ? "この世界に作成する" :
                        form.Stage == RegistrationStage.World ? "キャラクターの入力へ" : "確認へ",
                    AdvanceRegistration).AccentButton()
                    .IsEnabled(loaded && !busy && !model.Worlds.Loading && !model.RegistrationGraphics.Loading)
                    .AutomationId("save-character");
        if (windowWidth < 480 && form.Stage == RegistrationStage.SavePending) return VStack(4, next, cancel);
        return HStack(8, cancel,
            form.NewCharacter && form.CanEdit && form.Stage != RegistrationStage.World
                ? Button("戻る", () =>
                {
                    var target = form.Stage == RegistrationStage.Review ? RegistrationStage.Character : RegistrationStage.World;
                    EditRegistration(form with { Stage = target, ShowErrors = false });
                    QueueRegistrationFocus(target == RegistrationStage.Character ? "character-name" :
                        form.ManualEndpoint ? "server-host" : "world-filter");
                }).IsEnabled(!busy && !model.RegistrationGraphics.Loading)
                    .AutomationId("registration-back") : Empty(),
            next);
    }

    private Element RegistrationError(string field, IReadOnlyDictionary<string, string> errors) =>
        model.Registration.ShowErrors && errors.TryGetValue(field, out var error)
            ? TextBlock(error).TextWrapping(TextWrapping.Wrap).AutomationId($"{field}-error") : Empty();

    private void AdvanceRegistration()
    {
        var form = model.Registration;
        if (form.Stage == RegistrationStage.SavePending) { _ = PerformAsync(SaveCharacterAsync); return; }
        if (!form.CanEdit) return;
        var errors = RegistrationForm.Errors(model.Draft, form.NewCharacter,
            form.Stage == RegistrationStage.World, form.CustomPassword);
        if (errors.Count != 0)
        {
            EditRegistration(form with { ShowErrors = true });
            dispatcher?.TryEnqueue(() => FocusRegistrationField(mainRoot, errors.First().Key));
            return;
        }
        if (!form.NewCharacter || form.Stage == RegistrationStage.Review) _ = PerformAsync(SaveCharacterAsync);
        else
        {
            var target = form.Stage == RegistrationStage.World ? RegistrationStage.Character : RegistrationStage.Review;
            EditRegistration(form with { Stage = target, ShowErrors = false });
            QueueRegistrationFocus(target == RegistrationStage.Character ? "character-name" : "registration-back");
        }
    }

    private void QueueRegistrationFocus(string id) =>
        dispatcher?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => FocusRegistrationField(mainRoot, id));

    private static bool FocusRegistrationField(DependencyObject? root, string id)
    {
        if (root is null) return false;
        if (AutomationProperties.GetAutomationId(root) == id && root is Microsoft.UI.Xaml.Controls.Control control)
            return control.Focus(FocusState.Programmatic);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FocusRegistrationField(VisualTreeHelper.GetChild(root, i), id)) return true;
        return false;
    }

    private async Task ShowRegistrationSecretAsync(bool showPassword) =>
        await RequestDialogAsync(new(showPassword ? "作成時のパスワード" : "作成済みキャラクターID",
            "安全な場所に控えてください。この確認を閉じても、PCへの保存が完了するまでは登録画面で保持します。",
            InputLabel: showPassword ? "パスワード" : "キャラクターID",
            Value: showPassword ? password : id,
            InputId: showPassword ? "pending-registration-secret" : "pending-registration-id", Secret: true));

    private async Task<bool> EndRegistrationAsync()
    {
        if (model.Registration.Stage == RegistrationStage.Creating || busy) return false;
        if (model.Registration.NeedsDiscardWarning ||
            model.Registration.Stage != RegistrationStage.Complete &&
            new[] { name, host, id, password, email }.Any(value => value.Length > 0))
        {
            var answer = await RequestDialogAsync(model.Registration.DiscardConfirmation());
            if (!answer.Accepted) return false;
        }
        registrationGraphicsCancellation?.Cancel();
        Dispatch(new ClientMessage.RegistrationEnded());
        notice = "";
        return true;
    }
}
