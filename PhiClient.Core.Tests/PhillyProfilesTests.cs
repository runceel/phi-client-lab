using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class PhillyProfilesTests
{
    [Theory]
    [InlineData("127.0.0.1:9000")]
    [InlineData("127.0.0.1 9000")]
    [InlineData("127.0.0.1\t : 9000")]
    public void OriginalCp932FolderAndAddressFormsPreserveTheIdOnlyInTheTransientResult(string address)
    {
        var profile = PhillyProfiles.Parse("勇者.Chara", PhiWire.Encode($"  synthetic-ID  \r\n{address}\r\nignored"));
        Assert.Equal("勇者", profile.Name);
        Assert.Equal("127.0.0.1", profile.Host);
        Assert.Equal(9000, profile.Port);
        Assert.Equal("synthetic-ID", profile.Id);
        Assert.DoesNotContain(profile.Id, profile.ToString());
    }

    [Theory]
    [InlineData("\r\n127.0.0.1:9000")]
    [InlineData("synthetic ID\r\n127.0.0.1:9000")]
    [InlineData("synthetic-ID\r\n127.0.0.1:0")]
    [InlineData("synthetic-ID\r\n127.0.0.1:9000 extra")]
    [InlineData("synthetic-ID")]
    public void InvalidProfilesAreRejectedWithoutIncludingTheirIdInTheError(string text)
    {
        var error = Assert.ThrowsAny<Exception>(() => PhillyProfiles.Parse("勇者.chara", PhiWire.Encode(text)));
        Assert.DoesNotContain("synthetic", error.Message);
    }

    [Fact]
    public async Task OnlyImmediateCharaDirectoriesAreImportedAndRejectedFilesAreCounted()
    {
        var root = Path.Combine(Path.GetTempPath(), "phi-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var (directory, text) in new[]
            {
                ("勇者.chara", "synthetic-ID\r\n127.0.0.1:9000"),
                ("a.Chara", "synthetic-other\n127.0.0.1 9001"),
                ("broken.chara", "missing-address"),
                (Path.Combine("other", "nested.chara"), "synthetic-nested\n127.0.0.1:9002")
            })
            {
                var path = Path.Combine(root, directory);
                Directory.CreateDirectory(path);
                await File.WriteAllBytesAsync(Path.Combine(path, ".phirc"), PhiWire.Encode(text));
            }
            var result = await PhillyProfiles.ReadAsync(root);
            Assert.Equal(["a", "勇者"], result.Profiles.Select(profile => profile.Name));
            Assert.Equal(1, result.Rejected);
            await File.WriteAllBytesAsync(Path.Combine(root, "broken.chara", ".phirc"), new byte[65537]);
            Assert.Equal(1, (await PhillyProfiles.ReadAsync(root)).Rejected);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ReplacementUsesFreshCredentialKeysAndOrdinalNamesWithoutChangingOtherProfiles()
    {
        SavedCharacter[] existing = [new("old-one", "Same", "127.0.0.1", 1),
            new("old-two", "Same", "127.0.0.1", 2), new("keep", "same", "127.0.0.1", 3)];
        var result = PhillyProfiles.Plan(existing, [new("Same", "127.0.0.1", 9000, "synthetic-ID")]);
        Assert.Equal(2, result.Count);
        Assert.Same(existing[2], result[0]);
        Assert.Equal("Same", result[1].Name);
        Assert.DoesNotContain(result[1].Key, new[] { "old-one", "old-two", "synthetic-ID" });
        Assert.Equal(9000, result[1].Port);
        Assert.Throws<InvalidDataException>(() => PhillyProfiles.Plan(existing,
            [new("Same", "127.0.0.1", 1, "synthetic-a"), new("Same", "127.0.0.1", 2, "synthetic-b")]));
    }
}
