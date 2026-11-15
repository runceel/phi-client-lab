using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using PhiClient;
using PhiClient.Core;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

partial class App
{
    private SoundRules? soundRules = SoundRules.Parse("", "");
    private HWND notificationWindow;
    private readonly HashSet<MediaPlayer> soundPlayers = [];
    private readonly HashSet<int> failedSounds = [];
    private long soundGeneration;

    private static string SoundIssues(SoundRules rules) =>
        "次の効果音規則を確認してください: " +
        string.Join("、", rules.Issues.Take(8).Select(issue => $"{issue.File} {issue.Line}行目")) +
        (rules.Issues.Count > 8 ? $"（ほか{rules.Issues.Count - 8}件）" : "");

    private void AttachNotificationWindow(FrameworkElement root)
    {
        void Loaded()
        {
            if (root.XamlRoot is { } xaml)
            {
                notificationWindow = new(Microsoft.UI.Win32Interop.GetWindowFromWindowId(
                    xaml.ContentIslandEnvironment.AppWindowId));
                AttachClosingWindow(root);
            }
        }
        root.Loaded += (_, _) => Loaded();
        root.Unloaded += (_, _) => notificationWindow = default;
        if (root.IsLoaded) Loaded();
    }

    private void SetSoundRules(SoundRules rules)
    {
        DisposeSounds();
        soundRules = rules;
        failedSounds.Clear();
    }

    private void DisposeSounds()
    {
        soundGeneration++;
        foreach (var player in soundPlayers) player.Dispose();
        soundPlayers.Clear();
    }

    private void ObserveSound(GameSession session, PhiConnection connection, LogLine line) =>
        _ = SessionOperationAsync(session, async () =>
        {
            if (soundRules is not { } rules || line.Literal) return;
            SoundNotification plan;
            try { plan = rules.Plan(line, model.Options, PInvoke.GetForegroundWindow() == notificationWindow); }
            catch (InvalidDataException error)
            {
                soundRules = null;
                throw new IOException("通知の規則照合を停止しました。「設定」で規則を確認して保存してください。", error);
            }
            if (plan.Flash)
            {
                if (notificationWindow == default)
                    throw new InvalidOperationException("通知先のウィンドウを取得できませんでした。");
                var flash = new FLASHWINFO
                {
                    cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = notificationWindow,
                    dwFlags = FLASHWINFO_FLAGS.FLASHW_TRAY | FLASHWINFO_FLAGS.FLASHW_TIMERNOFG, uCount = 3
                };
                // The return value is the previous caption state, not a success flag.
                PInvoke.FlashWindowEx(in flash);
            }
            if (plan.Sound is not { WavePath: { } wave } match || failedSounds.Contains(match.Line)) return;
            var generation = soundGeneration;
            MediaPlayer? pendingPlayer = null;
            try
            {
                var path = SoundFiles.WavePath(model.Options.SoundFolder, wave);
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                if (disposed || soundGeneration != generation || session.Connection != connection) return;
                if (soundPlayers.Count >= 16)
                    throw new IOException("同時に再生する効果音が上限に達しました。");
                var player = new MediaPlayer();
                pendingPlayer = player;
                soundPlayers.Add(player);
                void Finish(string? failure) => dispatcher?.TryEnqueue(() =>
                {
                    if (!soundPlayers.Remove(player)) return;
                    player.Dispose();
                    if (failure is not null && !disposed && generation == soundGeneration)
                    {
                        failedSounds.Add(match.Line);
                        session.Append($"効果音の{match.Line}行目を再生できませんでした。設定を保存すると再試行します: {failure}");
                    }
                });
                player.MediaEnded += (_, _) => Finish(null);
                player.MediaFailed += (_, error) => Finish(error.ErrorMessage);
                player.Source = MediaSource.CreateFromStorageFile(file);
                player.Play();
            }
            catch (Exception error) when (IsExpected(error))
            {
                if (pendingPlayer is not null)
                {
                    soundPlayers.Remove(pendingPlayer);
                    pendingPlayer.Dispose();
                }
                if (generation != soundGeneration) return;
                failedSounds.Add(match.Line);
                throw new IOException($"効果音の{match.Line}行目を再生できませんでした。設定を保存すると再試行します。", error);
            }
        });
}
