using System.Text;
using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class SoundFilesTests
{
    [Fact]
    public async Task LocalCp932ConfigurationUsesOriginalRelativeWaveAndOptionalExclusions()
    {
        var root = Path.Combine(Path.GetTempPath(), "phi-sound-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Config"));
        Directory.CreateDirectory(Path.Combine(root, "sound"));
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var cp932 = Encoding.GetEncoding(932);
            await File.WriteAllBytesAsync(Path.Combine(root, "Config", "SoundRegexp.ini"),
                cp932.GetBytes("説明\r\n/攻撃/\r\n"));
            await File.WriteAllBytesAsync(Path.Combine(root, "Config", "SoundFile.ini"),
                cp932.GetBytes("説明\r\nsound\\attack.WAV\r\n"));
            await File.WriteAllBytesAsync(Path.Combine(root, "sound", "attack.WAV"), "RIFF"u8.ToArray());
            var sounds = await SoundFiles.LoadAsync(root);
            var match = Assert.IsType<SoundMatch>(sounds.Find("勇者が攻撃した"));
            Assert.Equal(Path.Combine(root, "sound", "attack.WAV"), SoundFiles.WavePath(root, match.WavePath!));
            Assert.True(sounds.SuppressTalkFlash("DM > ようこそ"));
            await File.WriteAllBytesAsync(Path.Combine(root, "Config", "NotFlashRegexp.ini"), cp932.GetBytes("説明\r\n"));
            Assert.False((await SoundFiles.LoadAsync(root)).SuppressTalkFlash("DM > ようこそ"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExplicitMissingRootAndMissingRequiredConfigurationAreFailures()
    {
        var root = Path.Combine(Path.GetTempPath(), "phi-sound-" + Guid.NewGuid().ToString("N"));
        Assert.Null((await SoundFiles.LoadAsync("")).Find("anything"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => SoundFiles.LoadAsync(root));
        Directory.CreateDirectory(root);
        try { await Assert.ThrowsAsync<DirectoryNotFoundException>(() => SoundFiles.LoadAsync(root)); }
        finally { Directory.Delete(root); }
        await Assert.ThrowsAsync<ArgumentException>(() => SoundFiles.LoadAsync(@"\\server\share"));
        Assert.Throws<ArgumentException>(() => SoundFiles.WavePath("relative", "sound.wav"));
    }

    [Fact]
    public async Task InvalidCp932AndOversizedConfigurationAreExplicitFailures()
    {
        var root = Path.Combine(Path.GetTempPath(), "phi-sound-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Config"));
        var file = Path.Combine(root, "Config", "SoundRegexp.ini");
        try
        {
            await File.WriteAllBytesAsync(file, [0x81]);
            await Assert.ThrowsAsync<DecoderFallbackException>(() => SoundFiles.LoadAsync(root));
            await File.WriteAllBytesAsync(file, new byte[1024 * 1024 + 1]);
            await Assert.ThrowsAsync<InvalidDataException>(() => SoundFiles.LoadAsync(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
