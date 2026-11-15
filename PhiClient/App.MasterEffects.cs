using System.Text;
using PhiClient;
using PhiClient.Core;

partial class App
{
    private async Task LoadMasterMenuAsync(string? requested = null)
    {
        var saved = Path.Combine(store.Folder, "master-magic-file.txt");
        var remembered = File.Exists(saved) ? (await File.ReadAllTextAsync(saved)).Trim() : "";
        var path = requested ?? new[] { Path.Combine(store.Folder, "MasterMagicMenu.xml"), remembered,
            graphics.Folder.Length == 0 ? "" : Path.Combine(graphics.Folder, "Config", "MasterMagicMenu.xml") }.FirstOrDefault(File.Exists);
        if (path is null)
        {
            Dispatch(new ClientMessage.MasterMenuChanged(null,
                "超魔法のXMLは未設定です。wphiまたは利用を許可されたPhillyのConfig\\MasterMagicMenu.xmlを読み込めます。"));
            return;
        }
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\"))
            throw new ArgumentException("超魔法のXMLは、このPCにあるファイルの絶対パスで指定してください。");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (file.Length > 1024 * 1024)
            throw new IOException("超魔法のXMLは1MiB以下にしてください。");
        var bytes = new byte[(int)file.Length];
        await file.ReadExactlyAsync(bytes);
        var xml = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })
            ? Encoding.UTF8.GetString(bytes.AsSpan(3)) : PhiWire.Encoding.GetString(bytes);
        var menu = MasterMagic.Parse(xml);
        if (requested is not null)
        {
            Directory.CreateDirectory(store.Folder);
            await File.WriteAllTextAsync(saved + ".tmp", path);
            File.Move(saved + ".tmp", saved, true);
        }
        Dispatch(new ClientMessage.MasterMenuChanged(menu, $"超魔法のXMLを読み込みました: {path}"));
    }

    private void ChooseMasterMenu() => _ = PerformAsync(async () =>
    {
        var answer = await RequestDialogAsync(new("超魔法のXMLを読み込む",
            model.MasterMessage + "\nXML内の手順を、選んだ世界へ送信します。信頼できるwphi／Phillyの設定だけを指定してください。",
            "読み込む", "MasterMagicMenu.xmlの絶対パス",
            Path.Combine(graphics.Folder, "Config", "MasterMagicMenu.xml"), "master-file"));
        if (answer.Accepted) await LoadMasterMenuAsync(answer.Value.Trim());
    });

    private void CancelMaster(GameSession session)
    {
        session.MasterCancellation?.Cancel();
        session.Append("超魔法の残りの手順を中止しました。送信済みの操作は取り消されません。");
    }

    private void RunMaster(GameSession session, MasterItem item) => _ = SessionOperationAsync(session, async () =>
    {
        if (!session.Connected || session.Connection is not { } connection || session.Model.Transferring ||
            session.PreparingMagic || session.Model.Master is not null)
            throw new InvalidOperationException("接続と実行中の操作が終わってから、超魔法を選んでください。");
        if (!MasterMagic.Visible(session.Stats, model.Options))
            throw new InvalidOperationException("超魔法はマスター、または表示を明示的に有効にした場合だけ使用できます。");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(worldLifetime.Token);
        var generation = connection.TransportVersion;
        session.MasterCancellation = cancel;
        var run = new MasterRun(item.Steps);
        session.Dispatch(new GameMessage.MasterChanged(run));
        void Check()
        {
            cancel.Token.ThrowIfCancellationRequested();
            if (session.Connection != connection || !session.Connected || session.Model.Transferring ||
                connection.TransportVersion != generation || session.Model.Master is null)
                throw new InvalidOperationException("接続先が変わったため、超魔法の残りの手順は送信しませんでした。");
        }
        try
        {
            while (true)
            {
                Check();
                var next = MasterMagic.Advance(run);
                session.Dispatch(new GameMessage.MasterChanged(next.Next));
                if (!next.Lines.IsEmpty)
                {
                    session.Dispatch(new GameMessage.SelectionCleared());
                    await connection.SendScriptAsync(next.Lines.ToArray(), next.HiddenLists, generation, cancel.Token);
                }
                if (next.Next.Prompt is not { } prompt) return;
                var answer = await RequestDialogAsync(prompt.Kind == MasterStepKind.Confirm
                    ? new(item.Caption, prompt.Text, "実行")
                    : new(item.Caption, "", "次へ", prompt.Text, InputId: "master-answer"), cancel.Token);
                Check();
                var answered = MasterMagic.Answer(next.Next, answer.Accepted ? answer.Value : null);
                if (answered is null) return;
                run = answered;
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        finally
        {
            session.MasterCancellation = null;
            session.Dispatch(new GameMessage.MasterChanged(null));
        }
    });
}
