using System.Text;

namespace GameDailyHub.Windows.Services;

/// <summary>Read only bytes appended after construction. Decoder survives split UTF-8 sequences.</summary>
public sealed class LogTail : IDisposable
{
    private sealed class Cursor(long offset, DateTime created)
    {
        public long Offset = offset;
        public DateTime Created = created;
        public Decoder Decoder = new UTF8Encoding(false, false).GetDecoder();
        public string Pending = "";
        public byte[] Anchor = [];
    }
    private readonly string _directory;
    private readonly string _pattern;
    private readonly Dictionary<string, Cursor> _cursors = new(StringComparer.OrdinalIgnoreCase);
    public bool SawDiscontinuity { get; private set; }
    public LogTail(string path)
    {
        _directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        _pattern = Path.GetFileName(path);
        if (!Directory.Exists(_directory)) return;
        foreach (var file in Directory.GetFiles(_directory, _pattern))
        {
            var info = new FileInfo(file);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            _cursors[file] = new(info.Length, info.CreationTimeUtc) { Anchor = ReadAnchor(stream, info.Length) };
        }
    }
    public async Task<IReadOnlyList<string>> ReadAsync(CancellationToken token)
    {
        var lines = new List<string>();
        if (!Directory.Exists(_directory)) return lines;
        var files = Directory.GetFiles(_directory, _pattern);
        if (_cursors.Keys.Any(path => !files.Contains(path, StringComparer.OrdinalIgnoreCase))) SawDiscontinuity = true;
        foreach (var file in files.OrderBy(File.GetCreationTimeUtc))
        {
            try
            {
                var info = new FileInfo(file);
                if (!_cursors.TryGetValue(file, out var cursor)) _cursors[file] = cursor = new(0, info.CreationTimeUtc);
                if (info.CreationTimeUtc != cursor.Created || info.Length < cursor.Offset)
                {
                    SawDiscontinuity = true;
                    _cursors[file] = cursor = new(0, info.CreationTimeUtc);
                }
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 8192, FileOptions.Asynchronous);
                if (cursor.Anchor.Length > 0 && !ReadAnchor(stream, cursor.Offset).SequenceEqual(cursor.Anchor))
                {
                    SawDiscontinuity = true;
                    _cursors[file] = cursor = new(0, info.CreationTimeUtc);
                }
                stream.Seek(cursor.Offset, SeekOrigin.Begin);
                var buffer = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(buffer, token)) > 0)
                {
                    cursor.Offset += read;
                    var chars = new char[Encoding.UTF8.GetMaxCharCount(read)];
                    var count = cursor.Decoder.GetChars(buffer, 0, read, chars, 0, false);
                    cursor.Pending += new string(chars, 0, count);
                    int newline;
                    while ((newline = cursor.Pending.IndexOf('\n')) >= 0)
                    {
                        lines.Add(cursor.Pending[..newline].TrimEnd('\r'));
                        cursor.Pending = cursor.Pending[(newline + 1)..];
                    }
                    if (cursor.Pending.Length > 65536) { SawDiscontinuity = true; cursor.Pending = ""; }
                }
                cursor.Anchor = ReadAnchor(stream, cursor.Offset);
            }
            catch (FileNotFoundException) { SawDiscontinuity = true; }
            catch (DirectoryNotFoundException) { SawDiscontinuity = true; }
        }
        return lines;
    }
    private static byte[] ReadAnchor(FileStream stream, long offset)
    {
        var count = (int)Math.Min(64, offset);
        var bytes = new byte[count];
        stream.Seek(offset - count, SeekOrigin.Begin);
        var read = stream.Read(bytes, 0, count);
        return bytes[..read];
    }
    public void Dispose() { }
}
