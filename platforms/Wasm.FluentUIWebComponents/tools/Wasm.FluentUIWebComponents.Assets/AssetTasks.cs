using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace MauiBrowser.Build;

public sealed class GenerateAssetCatalog : Microsoft.Build.Utilities.Task
{
    public ITaskItem[] Images { get; set; } = [];
    public ITaskItem[] Fonts { get; set; } = [];
    public ITaskItem[] Raw { get; set; } = [];
    [Required] public string ProjectDirectory { get; set; } = "";
    [Required] public string Application { get; set; } = "";
    [Required] public string StateDirectory { get; set; } = "";
    [Output] public ITaskItem[] Assets { get; set; } = [];
    [Output] public string CatalogFile { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            var outputs = new List<ITaskItem>();
            var paths = new AssetPaths();
            var images = Collect(Images, "images", [".png", ".jpg", ".jpeg", ".svg"], outputs, paths);
            var fonts = Collect(Fonts, "fonts", [".ttf", ".otf"], outputs, paths);
            var raw = Collect(Raw, "raw", null, outputs, paths);
            CatalogFile = Path.GetFullPath(Path.Combine(StateDirectory, "maui-assets.json"));
            using var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                json.WriteStartObject();
                json.WriteNumber("version", 1);
                json.WriteString("application", Application);
                WriteMap(json, "images", images);
                WriteMap(json, "fonts", fonts);
                WriteMap(json, "raw", raw);
                json.WriteEndObject();
            }
            AssetFiles.WriteIfChanged(CatalogFile, buffer.ToArray());
            outputs.Add(AssetFiles.Item(CatalogFile, "maui-assets.json"));
            Assets = outputs.ToArray();
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.LogError("MAUI browser assets: {0}", ex.Message);
            return false;
        }
    }

    private SortedDictionary<string, string> Collect(ITaskItem[] items, string kind, string[]? extensions,
        List<ITaskItem> outputs, AssetPaths paths)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.OrderBy(item => item.ItemSpec, StringComparer.Ordinal))
        {
            var source = Path.GetFullPath(item.GetMetadata("FullPath"));
            if (!File.Exists(source))
            {
                throw new IOException($"'{item.ItemSpec}' does not exist. Remove the item or supply the source file.");
            }
            foreach (var metadata in new[] { "BaseSize", "TintColor", "Color", "ForegroundFile", "ForegroundScale" })
            {
                if (!string.IsNullOrEmpty(item.GetMetadata(metadata)))
                {
                    throw new ArgumentException($"'{item.ItemSpec}' has unsupported {metadata} metadata. Browser assets are copied unchanged; remove transformation metadata.");
                }
            }
            var resize = item.GetMetadata("Resize");
            if (resize.Length != 0 && !string.Equals(resize, "false", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"'{item.ItemSpec}' has unsupported Resize='{resize}'. Omit Resize or use Resize=false for unchanged browser assets.");
            }
            var link = item.GetMetadata("Link");
            var logical = kind == "raw" ? RawName(item, link) :
                link.Length != 0 ? AssetPaths.Validate(link, item.ItemSpec).Split('/')[^1] : Path.GetFileName(source);
            logical = AssetPaths.Validate(logical, item.ItemSpec);
            if (extensions != null)
            {
                var extension = Path.GetExtension(logical);
                if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
                    !string.Equals(extension, Path.GetExtension(source), StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"'{item.ItemSpec}' has unsupported {kind} extension or changes extension through Link. Supported: {string.Join(", ", extensions)}; no format conversion is performed.");
                }
            }
            if (kind == "images" && string.Equals(Path.GetExtension(logical), ".png", StringComparison.OrdinalIgnoreCase) &&
                IsAnimatedPng(source))
            {
                throw new ArgumentException($"'{item.ItemSpec}' is an unsupported animated PNG (APNG). Supply a static PNG/JPEG/SVG; browser assets are copied unchanged.");
            }
            var relative = $"maui-assets/{kind}/{logical}";
            paths.Add(relative, item.ItemSpec);
            AddAlias(logical, relative, item.ItemSpec);
            if (kind == "images" && string.Equals(Path.GetExtension(logical), ".svg", StringComparison.OrdinalIgnoreCase))
            {
                AddAlias(Path.ChangeExtension(logical, ".png"), relative, item.ItemSpec);
            }
            outputs.Add(AssetFiles.Item(source, relative));
        }
        return map;

        void AddAlias(string alias, string relative, string source)
        {
            if (!aliases.TryAdd(alias, source))
            {
                throw new ArgumentException($"'{source}' collides with '{aliases[alias]}' at {kind} logical name/alias '{alias}' (ordinal-ignore-case).");
            }
            map.Add(alias, relative);
        }
    }

    private static bool IsAnimatedPng(string source)
    {
        // Only recognize PNG framing, not arbitrary fixture bytes. This is an animation
        // exclusion check, not a decoder, CRC validator or sanitizer for untrusted images.
        using var stream = File.OpenRead(source);
        Span<byte> header = stackalloc byte[8];
        if (stream.Length < 8)
        {
            return false;
        }
        stream.ReadExactly(header);
        if (!header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            return false;
        }
        var first = true;
        while (stream.Length - stream.Position >= 12)
        {
            stream.ReadExactly(header);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = header[4..];
            if (length > int.MaxValue || length > stream.Length - stream.Position - 4 ||
                first && (length != 13 || !type.SequenceEqual("IHDR"u8)))
            {
                return false;
            }
            foreach (var character in type)
            {
                if (!(character is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z'))
                {
                    return false;
                }
            }
            first = false;
            if (type.SequenceEqual("acTL"u8) && length == 8)
            {
                stream.ReadExactly(header);
                return BinaryPrimitives.ReadUInt32BigEndian(header) != 0;
            }
            if (type.SequenceEqual("IEND"u8))
            {
                return false;
            }
            stream.Seek(length + 4L, SeekOrigin.Current);
        }
        return false;
    }

    private string RawName(ITaskItem item, string link)
    {
        // MAUI 10.0.41 ProcessMauiAssets supplies LogicalName as Link only when Link
        // is absent. GetMauiAssetPath then uses Link or ItemSpec, preserving directories.
        // https://github.com/dotnet/maui/blob/10.0.41/src/SingleProject/Resizetizer/src/GetMauiAssetPath.cs
        var name = link.Length != 0 ? link : item.GetMetadata("LogicalName");
        if (name.Length != 0)
        {
            return name;
        }
        name = item.ItemSpec.Replace('\\', '/');
        if (Path.IsPathRooted(name))
        {
            name = Path.GetFullPath(name);
            foreach (var directory in new[] { item.GetMetadata("ProjectDirectory"), ProjectDirectory })
            {
                if (directory.Length == 0)
                {
                    continue;
                }
                var prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return name[prefix.Length..];
                }
            }
            return Path.GetFileName(name);
        }
        return name;
    }

    private static void WriteMap(Utf8JsonWriter json, string name, SortedDictionary<string, string> map)
    {
        json.WriteStartObject(name);
        foreach (var (key, value) in map)
        {
            json.WriteString(key, value);
        }
        json.WriteEndObject();
    }
}

// Journals are private build state, not a runtime dependency or a deployed manifest.
// Per-project journals are shared across configurations, but each destination records
// its active configuration/TFM/RID. Only an explicit copy to that destination transfers
// ownership; generating or cleaning an older configuration must leave it alone.
public sealed class AssetOwnership : Microsoft.Build.Utilities.Task
{
    [Required] public string StateDirectory { get; set; } = "";
    [Required] public string ProjectFullPath { get; set; } = "";
    public string Configuration { get; set; } = "";
    public string TargetFramework { get; set; } = "";
    public string RuntimeIdentifier { get; set; } = "";
    public string Destination { get; set; } = "";
    public ITaskItem[] Assets { get; set; } = [];
    public ITaskItem[] OtherItems { get; set; } = [];
    public string OtherItemPathMetadata { get; set; } = "TargetPath";
    public string CatalogFile { get; set; } = "";
    public bool Clean { get; set; }

    private sealed record Owner(string Project, string Configuration, string TargetFramework, string RuntimeIdentifier);
    private sealed record Journal(string Path, string Destination, HashSet<string> Files, Owner Owner);

    public override bool Execute()
    {
        try
        {
            if (OtherItemPathMetadata is not ("TargetPath" or "RelativePath"))
            {
                throw new ArgumentException("OtherItemPathMetadata must be TargetPath (build) or RelativePath (publish).");
            }
            var owner = new Owner(Path.GetFullPath(ProjectFullPath), Configuration, TargetFramework, RuntimeIdentifier);
            var current = Assets.Select(item => AssetPaths.Owned(item.GetMetadata("TargetPath")))
                .ToHashSet(StringComparer.Ordinal);
            var journalDirectory = Path.GetFullPath(Path.Combine(StateDirectory, "ownership", Key(owner.Project)));
            var destination = Destination.Length == 0 ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(Destination));
            var previous = new HashSet<string>(StringComparer.Ordinal);
            var catalog = CatalogFile.Length != 0 ? CatalogFile :
                Assets.FirstOrDefault(item => item.GetMetadata("TargetPath") == "maui-assets.json")?.ItemSpec;
            // Check ancestors before even enumerating/reading private state. Validate all
            // affected output paths before deleting anything, including retired catalogs.
            AssetFiles.RejectLinks(journalDirectory);
            if (destination != null)
            {
                AssetFiles.RejectLinks(destination);
            }
            if (catalog != null)
            {
                AssetFiles.RejectLinks(catalog);
            }
            var journals = new List<Journal>();
            if (Directory.Exists(journalDirectory))
            {
                foreach (var path in Directory.GetFiles(journalDirectory, "*.json"))
                {
                    using var document = JsonDocument.Parse(AssetFiles.ReadAllBytes(path));
                    var root = document.RootElement.GetProperty("destination").GetString()!;
                    if (!Path.IsPathFullyQualified(root) || root != Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
                    {
                        throw new ArgumentException($"Invalid ownership destination '{root}'.");
                    }
                    var provenance = document.RootElement.GetProperty("owner");
                    var journalOwner = new Owner(provenance.GetProperty("project").GetString()!,
                        provenance.GetProperty("configuration").GetString()!,
                        provenance.GetProperty("targetFramework").GetString()!,
                        provenance.GetProperty("runtimeIdentifier").GetString()!);
                    if (journalOwner.Project != owner.Project)
                    {
                        throw new ArgumentException($"Ownership journal '{path}' belongs to a different project.");
                    }
                    if (journalOwner != owner && (Clean || destination != root))
                    {
                        continue;
                    }
                    var owned = document.RootElement.GetProperty("files").EnumerateArray()
                        .Select(value => AssetPaths.Owned(value.GetString()!)).ToHashSet(StringComparer.Ordinal);
                    AssetFiles.RejectLinks(root);
                    foreach (var relative in owned)
                    {
                        AssetFiles.RejectLinks(Path.Combine(root, relative));
                    }
                    journals.Add(new Journal(path, root, owned, journalOwner));
                    if (destination == root)
                    {
                        previous = owned;
                    }
                }
            }

            foreach (var journal in journals)
            {
                foreach (var relative in journal.Files.Where(relative => Clean || !current.Contains(relative)))
                {
                    AssetFiles.DeleteOwned(journal.Destination, relative);
                }
                // Retired destinations must not advertise removed/renamed assets. Only
                // the active SDK copy restores the catalog alongside its new files.
                var oldCatalog = Path.Combine(journal.Destination, "maui-assets.json");
                if (!Clean && catalog != null && journal.Files.Contains("maui-assets.json") && File.Exists(oldCatalog) &&
                    !AssetFiles.ReadAllBytes(oldCatalog).AsSpan().SequenceEqual(AssetFiles.ReadAllBytes(catalog)))
                {
                    AssetFiles.DeleteOwned(journal.Destination, "maui-assets.json");
                }
                if (Clean)
                {
                    AssetFiles.RejectLinks(journal.Path);
                    File.Delete(journal.Path);
                }
                else
                {
                    WriteJournal(journal.Path, journal.Destination,
                        journal.Files.Intersect(current, StringComparer.Ordinal), journal.Owner);
                }
            }
            if (Clean || destination == null)
            {
                return true;
            }

            var paths = new AssetPaths();
            foreach (var asset in Assets)
            {
                paths.Add(asset.GetMetadata("TargetPath"), asset.ItemSpec);
            }
            foreach (var other in OtherItems)
            {
                // ResolvedFileToPublish may retain an obsolete TargetPath from build.
                // Use exactly the metadata used by the corresponding SDK copy operation.
                var relative = other.GetMetadata(OtherItemPathMetadata);
                relative = relative.Replace('\\', '/');
                var top = relative.Split('/')[0];
                if (!string.Equals(top, "maui-assets", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(top, "maui-assets.json", StringComparison.OrdinalIgnoreCase))
                {
                    // Leave unrelated SDK output validation to the SDK itself.
                    continue;
                }
                if (relative.Length == 0 || Assets.Any(asset =>
                    asset.GetMetadata("FullPath") == other.GetMetadata("FullPath") &&
                    asset.GetMetadata("TargetPath") == relative))
                {
                    continue;
                }
                // Check other SDK destinations too, including file/directory and case aliases.
                paths.Add(relative, other.ItemSpec);
            }
            foreach (var relative in current)
            {
                AssetFiles.CheckDestination(destination, relative, previous.Contains(relative));
            }
            WriteJournal(Path.Combine(journalDirectory, Key(destination) + ".json"), destination, current, owner);
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or KeyNotFoundException)
        {
            Log.LogError("MAUI browser assets ownership: {0}", ex.Message);
            return false;
        }
    }

    private static string Key(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void WriteJournal(string path, string destination, IEnumerable<string> files, Owner owner)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("destination", destination);
            json.WriteStartObject("owner");
            json.WriteString("project", owner.Project);
            json.WriteString("configuration", owner.Configuration);
            json.WriteString("targetFramework", owner.TargetFramework);
            json.WriteString("runtimeIdentifier", owner.RuntimeIdentifier);
            json.WriteEndObject();
            json.WriteStartArray("files");
            foreach (var file in files.Order(StringComparer.Ordinal))
            {
                json.WriteStringValue(file);
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        AssetFiles.WriteIfChanged(path, buffer.ToArray());
    }
}

internal sealed class AssetPaths
{
    private readonly Dictionary<string, (string Path, bool Directory, string Source)> entries = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string path, string source)
    {
        path = Validate(path, source);
        var segments = path.Split('/');
        for (var index = 1; index <= segments.Length; index++)
        {
            var part = string.Join('/', segments.Take(index));
            var directory = index < segments.Length;
            if (entries.TryGetValue(part, out var existing))
            {
                if (existing.Path != part || !existing.Directory || !directory)
                {
                    throw new ArgumentException($"'{source}' collides with '{existing.Source}' at '{part}' (case or file/directory collision).");
                }
            }
            else
            {
                entries.Add(part, (part, directory, source));
            }
        }
    }

    public static string Owned(string path)
    {
        path = Validate(path, "ownership journal");
        if (path != "maui-assets.json" && !path.StartsWith("maui-assets/images/", StringComparison.Ordinal) &&
            !path.StartsWith("maui-assets/fonts/", StringComparison.Ordinal) && !path.StartsWith("maui-assets/raw/", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid owned asset path '{path}'.");
        }
        return path;
    }

    public static string Validate(string path, string source)
    {
        path = path.Replace('\\', '/');
        foreach (var part in path.Split('/'))
        {
            var stem = part.Split('.')[0];
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(character => char.IsControl(character) || "<>:\"|?*".Contains(character)) ||
                new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"'{source}' has invalid logical/output path '{path}'. Use a portable relative path without empty, absolute, traversal or reserved segments.");
            }
        }
        return path;
    }
}

internal static class AssetFiles
{
    public static ITaskItem Item(string source, string relative)
    {
        var item = new TaskItem();
        ((ITaskItem2)item).EvaluatedIncludeEscaped = Escape(source);
        ((ITaskItem2)item).SetMetadataValueLiteral("TargetPath", relative);
        ((ITaskItem2)item).SetMetadataValueLiteral("Link", relative);
        return item;
    }

    private static string Escape(string value)
    {
        foreach (var character in new[] { '%', ';', '$', '@', '\'', '(', ')', '*', '?' })
        {
            value = value.Replace(character.ToString(), $"%{(int)character:x2}");
        }
        return value;
    }

    public static void WriteIfChanged(string path, byte[] bytes)
    {
        path = Path.GetFullPath(path);
        RejectLinks(path);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
        {
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static void CheckDestination(string root, string relative, bool owned)
    {
        RejectLinks(Path.Combine(root, relative));
        var path = root;
        var parts = relative.Split('/');
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (Directory.Exists(path))
            {
                var collision = Directory.EnumerateFileSystemEntries(path).FirstOrDefault(entry =>
                    string.Equals(Path.GetFileName(entry), part, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(entry) != part);
                if (collision != null)
                {
                    throw new IOException($"Asset '{relative}' collides with existing path '{collision}'.");
                }
            }
            path = Path.Combine(path, part);
            RejectLink(path);
            if (index < parts.Length - 1 && File.Exists(path) || index == parts.Length - 1 &&
                (Directory.Exists(path) || File.Exists(path) && !owned))
            {
                throw new IOException($"Asset '{relative}' would overwrite an unowned file/directory at '{path}'.");
            }
        }
    }

    public static void DeleteOwned(string root, string relative)
    {
        RejectLinks(Path.Combine(root, relative));
        var path = root;
        foreach (var part in relative.Split('/'))
        {
            path = Path.Combine(path, part);
            RejectLink(path);
        }
        if (Directory.Exists(path))
        {
            throw new IOException($"Owned asset '{path}' was replaced by a directory; refusing recursive deletion.");
        }
        File.Delete(path);
        var directory = Path.GetDirectoryName(path);
        while (directory != null && directory != root && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    public static byte[] ReadAllBytes(string path)
    {
        RejectLinks(path);
        return File.ReadAllBytes(path);
    }

    public static void RejectLinks(string path)
    {
        // Walk from the filesystem root, not the destination root: either that root
        // or one of its ancestors can redirect reads, writes and cleanup elsewhere.
        // This is a preflight guard for build inputs, not a race-proof filesystem API.
        var fullPath = Path.GetFullPath(path);
        var current = Path.GetPathRoot(fullPath)!;
        RejectLink(current);
        foreach (var part in fullPath[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            RejectLink(current);
        }
    }

    private static void RejectLink(string path)
    {
        if (new FileInfo(path).LinkTarget != null || new DirectoryInfo(path).LinkTarget != null ||
            (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Asset output '{path}' is a symbolic link; refusing to follow it.");
        }
    }
}