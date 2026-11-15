namespace PhiClient.Core;

public enum RegistrationStage { None, World, Character, Review, Creating, SavePending, Complete, Unknown, Rejected }

public sealed record RegistrationForm(
    RegistrationStage Stage = RegistrationStage.None, bool NewCharacter = true,
    bool ManualEndpoint = false, bool CustomPassword = false, bool ShowErrors = false,
    string Message = "", SavedCharacter? Character = null)
{
    public bool CanEdit => Stage is RegistrationStage.World or RegistrationStage.Character or
        RegistrationStage.Review or RegistrationStage.Rejected;
    public bool NeedsDiscardWarning => Stage is RegistrationStage.SavePending or RegistrationStage.Unknown;

    public DialogModel DiscardConfirmation() => new("登録画面を閉じますか",
        Stage == RegistrationStage.SavePending
            ? "世界では作成済みですが、このPCには保存されていません。IDとパスワードを控えずに閉じると、接続できなくなるおそれがあります。" :
        Stage == RegistrationStage.Unknown
            ? "作成済みの可能性があります。結果が不明なまま再作成しないでください。閉じると下書きと秘密情報を消去します。" :
            "入力した内容を破棄します。サーバーへ作成要求は送りません。", "破棄して閉じる",
        CancelDefault: NeedsDiscardWarning);

    public static IReadOnlyDictionary<string, string> Errors(CharacterDraft draft, bool newCharacter,
        bool worldOnly = false, bool customPassword = false)
    {
        var errors = new Dictionary<string, string>();
        try
        {
            if (!int.TryParse(draft.Port, out var port))
                throw new ArgumentException("TCPポートを数値で入力してください。");
            PhiConnection.ValidateEndpoint(draft.Host.Trim(), port);
        }
        catch (ArgumentException error) { errors["server-host"] = error.Message; }
        if (worldOnly) return errors;
        if (string.IsNullOrWhiteSpace(draft.Name)) errors["character-name"] = "名前を入力してください。";
        if (newCharacter)
        {
            Check("character-name", draft.Name.Trim(), "000000", "0", "");
            Check("new-password", "確認用", draft.Password.Length == 0 && !customPassword ? "000000" : draft.Password, "0", "");
            Check("initial-image", "確認用", "000000", draft.Image, "");
            Check("registration-email", "確認用", "000000", "0", draft.Email);
        }
        else
        {
            try { PhiWire.Login(draft.Id); }
            catch (ArgumentException error) { errors["character-id"] = error.Message; }
        }
        return errors;

        void Check(string field, string name, string password, string image, string email)
        {
            try { PhiWire.Registration(name, password, image, email); }
            catch (ArgumentException error) { errors[field] = error.Message; }
        }
    }

    public static string WireText(string value) => PhiWire.Encoding.GetString(PhiWire.Encode(value));
}

public sealed class RegistrationRejectedException(string message) : IOException(message);
