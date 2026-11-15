using PhiClient.Core;

partial class App
{
    private void ImportPhillyProfiles() => _ = PerformAsync(async () =>
    {
        var answer = await RequestDialogAsync(new("Phillyの登録を取り込む",
            "Phillyフォルダー直下の「表示名.chara」にある登録情報を、このPCへ取り込みます。",
            "読み込む", "このPCのPhillyフォルダー（絶対パス）", "", "philly-folder"));
        if (!answer.Accepted) return;
        var imported = await PhillyProfiles.ReadAsync(answer.Value.Trim(), worldLifetime.Token);
        if (imported.Profiles.Count == 0)
            throw new InvalidDataException($"取り込める登録が0件でした。{imported.Rejected}件の登録ファイルとフォルダーを確認してください。");
        var names = imported.Profiles.Select(profile => profile.Name).ToHashSet(StringComparer.Ordinal);
        if (sessions.Any(session => names.Contains(session.Character.Name)))
            throw new InvalidOperationException("同じ表示名のキャラクターのタブを閉じてから取り込んでください。");
        var replacements = characters.Count(character => names.Contains(character.Name));
        var confirmation = await RequestDialogAsync(new("登録情報を取り込む",
            $"{imported.Profiles.Count}件を保存し、同じ表示名の既存登録{replacements}件を置き換えます。" +
            $"読み取りに問題のある{imported.Rejected}件は保持します。IDはWindowsの資格情報へ保存します。" +
            "接続はキャラクターを選んでから行います。",
            "取り込む"));
        if (!confirmation.Accepted) return;
        if (sessions.Any(session => names.Contains(session.Character.Name)))
            throw new InvalidOperationException("同じ表示名のキャラクターのタブを閉じてから取り込んでください。");
        var saved = await store.ImportAsync(imported.Profiles);
        characters = saved.Characters;
        notice = $"登録{imported.Profiles.Count}件を取り込みました。読み取りに問題のある{imported.Rejected}件は元のままです。";
        if (saved.RetainedCredentials > 0)
            notice += $"旧資格情報{saved.RetainedCredentials}件の削除に失敗しました。Windowsの資格情報を確認してください。";
    });
}
