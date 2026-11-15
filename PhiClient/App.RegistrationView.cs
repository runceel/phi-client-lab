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
            Button(candidates.Loading ? "キャラグラの候補を取得しています…" : "キャラグラの候補を取得",
                    () => _ = PerformAsync(LoadRegistrationGraphicsAsync))
                .IsEnabled(loaded && !busy).AutomationId("load-registration-graphics"),
            Caption("指定したゲームサーバーへ接続して一覧だけを取得します。作成・ログインはしません。")
                .TextWrapping(TextWrapping.Wrap),
            TextBlock(candidates.Message).TextWrapping(TextWrapping.Wrap).AutomationId("registration-graphics-message"),
            candidates.Names.IsEmpty ? Empty() :
                ListView(selected, SelectRegistrationGraphic, candidates.Names.Select((name, index) =>
                    (Element)TextBlock($"{index}: {name}").TextWrapping(TextWrapping.Wrap)
                        .AutomationId($"registration-graphic-{index}")).ToArray())
                    .MaxHeight(160).IsEnabled(!busy).AutomationId("registration-graphics-list"),
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
