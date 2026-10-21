using System.Text;
using System.Text.Json;
using Looppaper.Core.Logging;

namespace Looppaper.Core.Compression;

/// <summary>
/// Remembers per video file (path + size + last-write time, see <see cref="VideoFileKey"/>) what the
/// person answered when offered compression, so a "no" is not asked again on every start. Kept in a
/// small JSON file (<see cref="AppPaths.CompressionStateFile"/>). Every call reads and writes the
/// file, so the terminal command and the background wallpaper see each other's answers. A missing or
/// broken file counts as empty; errors are logged, never thrown.
/// </summary>
public sealed class CompressionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
    };

    private readonly string _file;
    private readonly ILog _log;

    public CompressionStore(string file, ILog log)
    {
        _file = file;
        _log = log;
    }

    public string FilePath => _file;

    /// <summary>True when the person said no to compressing this file version.</summary>
    public bool IsDeclined(VideoFileKey key) => Find(Load(), key)?.Declined == true;

    public void SetDeclined(VideoFileKey key, bool declined) => Update(key, e => e.Declined = declined);

    /// <summary>True when the background process already said that ffmpeg is needed for this file version.</summary>
    public bool WasToldFfmpegMissing(VideoFileKey key) => Find(Load(), key)?.FfmpegMissingNotified == true;

    public void SetToldFfmpegMissing(VideoFileKey key) => Update(key, e => e.FfmpegMissingNotified = true);

    private static Entry? Find(StoreFile store, VideoFileKey key) =>
        store.Entries.FirstOrDefault(e => e.ToKey().Matches(key));

    private void Update(VideoFileKey key, Action<Entry> change)
    {
        var store = Load();

        // An older version of the same file (edited or replaced) no longer applies.
        store.Entries.RemoveAll(e => string.Equals(e.Path, key.FullPath, StringComparison.OrdinalIgnoreCase) && !e.ToKey().Matches(key));
        var entry = Find(store, key);
        if (entry is null)
        {
            entry = new Entry { Path = key.FullPath, Size = key.SizeBytes, LastWriteUtcTicks = key.LastWriteUtcTicks };
            store.Entries.Add(entry);
        }

        change(entry);
        if (entry.IsEmpty)
        {
            store.Entries.Remove(entry);
        }

        Save(store);
    }

    private StoreFile Load()
    {
        try
        {
            if (!File.Exists(_file))
            {
                return new StoreFile();
            }

            var store = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(_file), JsonOptions) ?? new StoreFile();
            store.Entries.RemoveAll(e => e is null || string.IsNullOrEmpty(e.Path));
            return store;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _log.Warn($"Cannot read {_file}; starting with no remembered answers", ex);
            return new StoreFile();
        }
    }

    private void Save(StoreFile store)
    {
        try
        {
            var dir = Path.GetDirectoryName(_file);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var temp = _file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(store, JsonOptions), new UTF8Encoding(false));
            File.Move(temp, _file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Cannot write {_file}", ex);
        }
    }

    private sealed class StoreFile
    {
        public int Version { get; set; } = 1;

        public List<Entry> Entries { get; set; } = [];
    }

    private sealed class Entry
    {
        public string Path { get; set; } = string.Empty;

        public long Size { get; set; }

        public long LastWriteUtcTicks { get; set; }

        public bool Declined { get; set; }

        public bool FfmpegMissingNotified { get; set; }

        public bool IsEmpty => !Declined && !FfmpegMissingNotified;

        public VideoFileKey ToKey() => new(Path, Size, LastWriteUtcTicks);
    }
}
