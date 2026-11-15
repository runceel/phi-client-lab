using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private Element RegistrationGraphicsView()
    {
        var candidates = model.RegistrationGraphics;
        var selected = int.TryParse(image, out var number) ? number : -1;
        var current = registrationPreviewKey == (candidates.Request, image);
        var bitmap = current ? registrationPreview : null;
        return VStack(6,
            Button(candidates.Loading ? "キャラクター画像の一覧を取得しています…" : "キャラクター画像の一覧を取得",
                    () => _ = LoadRegistrationGraphicsAsync())
                .IsEnabled(loaded && !busy && !candidates.Loading).AutomationId("load-registration-graphics"),
            Caption("指定したゲームサーバーに接続し、キャラクター画像の一覧を取得します。この操作は一覧の取得だけを行います。")
                .TextWrapping(TextWrapping.Wrap),
            TextBlock(candidates.Message).TextWrapping(TextWrapping.Wrap).AutomationId("registration-graphics-message"),
            candidates.Names.IsEmpty ? TextBlock(candidates.Loading ? "一覧の返事を待っています（最大30秒）。" :
                    "一覧未取得・0件の場合も画像番号で続行できます。").TextWrapping(TextWrapping.Wrap).MinHeight(96) :
                ListView(selected, SelectRegistrationGraphic, candidates.Names.Select((name, index) =>
                    (Element)TextBlock($"{index}: {name}").TextWrapping(TextWrapping.Wrap)
                        .AutomationId($"registration-graphic-{index}")).ToArray())
                    .Height(120).IsEnabled(!busy).AutomationId("registration-graphics-list"),
            bitmap is not null ?
                Image("ms-appx:///Assets/Square44x44Logo.scale-200.png").Width(32).Height(32)
                    .WithKey($"registration-preview-{candidates.Request}-{selected}-{bitmap.PixelWidth}-{graphics.Revision}")
                    .AutomationName("選んだ候補の実際のキャラグラ。正面の大きい絵・コマ0")
                    .AutomationId("registration-graphic-preview")
                    .OnMount(element => AttachRegistrationPreview((Microsoft.UI.Xaml.Controls.Image)element, bitmap)) : Empty(),
            TextBlock(current ? registrationPreviewMessage : "").TextWrapping(TextWrapping.Wrap)
                .AutomationId("registration-preview-message"));
    }
}
