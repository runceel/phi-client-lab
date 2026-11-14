using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using Windows.Security.Credentials;
using PhiClient.Core;

namespace PhiClient;

public sealed class CharacterStore
{
    private const string Resource = "PhiClient.Character";
    private readonly PasswordVault vault = new();
    public string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhiClient");

    public async Task<List<SavedCharacter>> LoadAsync()
    {
        var path = Path.Combine(Folder, "characters.json");
        if (!File.Exists(path))
            return [];
        await using var file = File.OpenRead(path);
        var characters = await JsonSerializer.DeserializeAsync(file, CharacterJsonContext.Default.ListSavedCharacter)
            ?? throw new InvalidDataException("characters.json contains no character list.");
        if (characters.Any(c => c is null || string.IsNullOrWhiteSpace(c.Key) ||
            string.IsNullOrWhiteSpace(c.Name)) || characters.Select(c => c.Key).Distinct().Count() != characters.Count)
            throw new InvalidDataException("characters.json contains invalid or duplicate characters.");
        foreach (var character in characters)
            Core.PhiConnection.ValidateEndpoint(character.Host, character.Port);
        return characters;
    }

    public string GetId(SavedCharacter character)
    {
        var credential = vault.Retrieve(Resource, character.Key);
        credential.RetrievePassword();
        return credential.Password;
    }

    public void AddId(SavedCharacter character, string id) =>
        vault.Add(new PasswordCredential(Resource, character.Key, id));

    public void RemoveId(SavedCharacter character) => vault.Remove(vault.Retrieve(Resource, character.Key));

    public async Task AddAsync(SavedCharacter character, string id, IReadOnlyList<SavedCharacter> updated)
    {
        AddId(character, id);
        try
        {
            await SaveAsync(updated);
        }
        catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException)
        {
            try { RemoveId(character); }
            catch (COMException rollbackError)
            {
                throw new IOException("Saving profiles failed and the new credential could not be rolled back.",
                    new AggregateException(saveError, rollbackError));
            }
            throw;
        }
    }

    public async Task RemoveAsync(SavedCharacter character, IReadOnlyList<SavedCharacter> remaining)
    {
        var id = GetId(character);
        RemoveId(character);
        try
        {
            await SaveAsync(remaining);
        }
        catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException)
        {
            try { AddId(character, id); }
            catch (COMException rollbackError)
            {
                throw new IOException("Saving profiles failed and the removed credential could not be restored.",
                    new AggregateException(saveError, rollbackError));
            }
            throw;
        }
    }

    public async Task SaveAsync(IReadOnlyList<SavedCharacter> characters)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, "characters.json");
        var temporary = path + ".tmp";
        await using (var file = File.Create(temporary))
            await JsonSerializer.SerializeAsync(file, characters.ToList(), CharacterJsonContext.Default.ListSavedCharacter);
        File.Move(temporary, path, true);
    }

}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<SavedCharacter>))]
internal partial class CharacterJsonContext : JsonSerializerContext
{
}
