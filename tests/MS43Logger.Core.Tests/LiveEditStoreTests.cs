using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

public sealed class LiveEditStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-edits-" + Guid.NewGuid().ToString("N"));

    public LiveEditStoreTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File => Path.Combine(_folder, "live-edits.json");

    private static IReadOnlyList<byte[]> Flash(int seed) =>
        OverlayContract.Maps.Select(m => FakeOverlayEcu.Image(seed).AsSpan(m.Address, m.Bytes).ToArray()).ToArray();

    [Fact]
    public void FlashKey_IsTheHashOfTheFiveMaps()
    {
        Assert.Equal(64, LiveEditStore.FlashKey(Flash(0)).Length);
        Assert.Equal(LiveEditStore.FlashKey(Flash(0)), LiveEditStore.FlashKey(Flash(0)));
        Assert.NotEqual(LiveEditStore.FlashKey(Flash(0)), LiveEditStore.FlashKey(Flash(1)));
    }

    [Fact]
    public void SaveThenLoad_ReturnsTheEdits()
    {
        var key = LiveEditStore.FlashKey(Flash(0));
        var edits = new Dictionary<string, byte[]> { ["ign98_is"] = Enumerable.Range(0, 36).Select(i => (byte)i).ToArray() };

        new LiveEditStore(File).Save(key, edits);
        var loaded = new LiveEditStore(File).Load(key);

        Assert.Equal(edits.Keys, loaded.Keys);
        Assert.Equal(edits["ign98_is"], loaded["ign98_is"]);
    }

    [Fact]
    public void Load_ForOtherFlashedBytes_ReturnsNothing_AndOtherKeysNamesThem()
    {
        var store = new LiveEditStore(File);
        var mine = LiveEditStore.FlashKey(Flash(0));
        var other = LiveEditStore.FlashKey(Flash(1));
        store.Save(other, new Dictionary<string, byte[]> { ["af98"] = new byte[192] });

        Assert.Empty(store.Load(mine));
        Assert.Equal(new[] { other }, store.OtherKeys(mine));
        store.Save(mine, new Dictionary<string, byte[]> { ["ign98_is"] = new byte[36] });
        Assert.Single(store.Load(other));                                  // saving one key keeps the others
    }

    [Fact]
    public void ACorruptFile_LoadsEmpty_AndIsNotOverwrittenUntilTheNextSave()
    {
        System.IO.File.WriteAllText(File, "{ not json");
        var store = new LiveEditStore(File);
        var key = LiveEditStore.FlashKey(Flash(0));

        Assert.Empty(store.Load(key));
        Assert.Equal("{ not json", System.IO.File.ReadAllText(File));
        store.Save(key, new Dictionary<string, byte[]> { ["ign98_is"] = new byte[36] });
        Assert.Single(store.Load(key));
    }

    [Fact]
    public void NoFile_LoadsEmpty()
    {
        Assert.Empty(new LiveEditStore(File).Load(LiveEditStore.FlashKey(Flash(0))));
    }

    [Fact]
    public void KeysHolding_NamesTheFlashKeysWithEditsForThatMap()
    {
        var store = new LiveEditStore(File);
        store.Save("A", new Dictionary<string, byte[]> { ["ign98_pl"] = new byte[320] });
        store.Save("B", new Dictionary<string, byte[]> { ["af98"] = new byte[192] });

        Assert.Equal(new[] { "A" }, store.KeysHolding("ign98_pl"));
        Assert.Empty(store.KeysHolding("cam_in"));
    }
}
