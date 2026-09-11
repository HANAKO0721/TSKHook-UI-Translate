using System.Text.Json;

namespace TSKHook.UI;

/// <summary>Reads editable project dictionaries and keeps runtime captures in the project.</summary>
internal sealed class WorkspaceSync
{
    private static readonly string[] ExistingTables = {
        "ui", "name", "skill", "quest", "data", "extra-data", "profile", "equipment",
        "sister", "mission", "stage", "enemy"
    };
    private readonly Action<string> log;
    private readonly string? projectPath;
    private readonly string? sourceDirectory;
    private Dictionary<string, (DateTime Modified, long Length)> observedFiles = new(StringComparer.OrdinalIgnoreCase);
    private string? lastCaptureError;

    internal string InstalledDirectory { get; }

    internal WorkspaceSync(string installedDirectory, Action<string> log)
    {
        InstalledDirectory = installedDirectory;
        this.log = log;
        Directory.CreateDirectory(installedDirectory);
        var configPath = Path.Combine(installedDirectory, "workspace.json");
        if (!File.Exists(configPath)) return;
        try
        {
            var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(configPath));
            if (string.IsNullOrWhiteSpace(config?.ProjectPath)) return;
            if (!Path.IsPathFullyQualified(config.ProjectPath))
                throw new IOException("ProjectPath must be an absolute project directory.");
            var project = Path.GetFullPath(config.ProjectPath);
            var source = Path.Combine(project, "src", "TSKHook.UI");
            if (!File.Exists(Path.Combine(source, "ui-translations.json")))
                throw new DirectoryNotFoundException("ProjectPath does not contain src/TSKHook.UI/ui-translations.json.");
            projectPath = project;
            sourceDirectory = source;
            log("UI dictionaries linked to project: " + source);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            log("Workspace configuration could not be loaded; using installed UI files. " + error.Message);
        }
    }

    // Share the original plugin's config. A linked workspace only changes
    // dictionary/image sources, never the game's settings location.
    internal string ConfigurationFile()
        => Path.GetFullPath(Path.Combine(InstalledDirectory, "..", "config.json"));

    internal string TranslationFile(string name)
    {
        var projectFile = sourceDirectory == null ? null : Path.Combine(sourceDirectory, name);
        return projectFile != null && File.Exists(projectFile)
            ? projectFile : Path.Combine(InstalledDirectory, name);
    }

    internal string[] TranslationFiles()
    {
        var existing = ExistingTables.Select(name => name + "-translations.json").ToArray();
        var directories = new List<string> { InstalledDirectory };
        if (sourceDirectory != null && Directory.Exists(sourceDirectory)) directories.Add(sourceDirectory);
        var additions = directories.SelectMany(directory => Directory.EnumerateFiles(directory, "*-translations.json"))
            .Select(Path.GetFileName).Cast<string>()
            .Where(name => !existing.Contains(name, StringComparer.OrdinalIgnoreCase)
                && !name.Equals("web-translations.json", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.Ordinal);
        return existing.Concat(additions).Select(TranslationFile).ToArray();
    }

    // File metadata avoids re-reading every dictionary on each capture flush.
    // A changed file set requests one reload, including a newly added translation table.
    internal bool ConsumeTranslationChanges()
    {
        var files = TranslationFiles().Append(TranslationFile("sprite-labels.json"))
            .Append(TranslationFile("web-translations.json")).Append(ConfigurationFile());
        var current = files.ToDictionary(path => path, path => {
            var file = new FileInfo(path);
            return file.Exists ? (file.LastWriteTimeUtc, file.Length) : (DateTime.MinValue, -1L);
        }, StringComparer.OrdinalIgnoreCase);
        var changed = current.Count != observedFiles.Count || current.Any(pair =>
            !observedFiles.TryGetValue(pair.Key, out var previous) || previous != pair.Value);
        observedFiles = current;
        return changed;
    }

    internal void WriteCapture(string fileName, string contents)
        => WriteCapture(fileName, path => File.WriteAllText(path, contents));

    internal void WriteCapture(string fileName, byte[] contents)
        => WriteCapture(fileName, path =>
        {
            var temporaryPath = path + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, contents);
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally { File.Delete(temporaryPath); }
        });

    private void WriteCapture(string fileName, Action<string> write)
    {
        var installedFile = Path.Combine(InstalledDirectory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(installedFile)!);
        write(installedFile);
        if (projectPath == null) return;
        try
        {
            if (!Directory.Exists(projectPath)) throw new DirectoryNotFoundException("The configured project has moved.");
            var output = Path.Combine(projectPath, "captures", "runtime");
            var projectFile = Path.Combine(output, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(projectFile)!);
            write(projectFile);
            lastCaptureError = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (error.Message != lastCaptureError)
                log("Project capture could not be written; the installed copy was saved. " + error.Message);
            lastCaptureError = error.Message;
        }
    }

    private sealed class Configuration
    {
        public string? ProjectPath { get; set; }
    }
}
