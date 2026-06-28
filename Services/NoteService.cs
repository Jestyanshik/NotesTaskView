using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Text.Json;
using NotesTaskView.Models;

namespace NotesTaskView.Services;

public sealed class NoteService : IDisposable
{
    private const string TrashFolderName = ".trash";
    private const string TrashFoldersFolderName = "folders";
    private const string OrderFileName = ".notes-order.json";
    private static readonly Encoding Utf8Encoding = new UTF8Encoding(false);
    private readonly AppConfig _config;
    private readonly object _watcherLock = new();
    private FileSystemWatcher? _watcher;

    public NoteService(AppConfig config)
    {
        _config = config;
        EnsureWatcherIfPossible();
    }

    public event EventHandler? NotesChanged;

    public string NotesFolderPath => _config.NotesFolderPath;

    public string TrashFolderPath => Path.Combine(_config.NotesFolderPath, TrashFolderName);

    public string TrashFoldersPath => Path.Combine(TrashFolderPath, TrashFoldersFolderName);

    public void UpdateNotesFolder(string notesFolderPath)
    {
        _config.NotesFolderPath = Environment.ExpandEnvironmentVariables(notesFolderPath.Trim());
        EnsureWatcherIfPossible();
        RaiseNotesChanged();
    }

    public Task<IReadOnlyList<object>> GetItemsAsync(string? folderPath)
    {
        return Task.Run<IReadOnlyList<object>>(() =>
        {
            var currentFolder = ResolveFolderOrRoot(folderPath);
            if (!Directory.Exists(currentFolder))
            {
                return Array.Empty<object>();
            }

            var items = new List<object>();

            try
            {
                foreach (var directoryPath in Directory.EnumerateDirectories(currentFolder))
                {
                    var directoryInfo = new DirectoryInfo(directoryPath);
                    if (IsTrashPath(directoryInfo.FullName))
                    {
                        continue;
                    }

                    items.Add(new FolderItem
                    {
                        Name = directoryInfo.Name,
                        FullPath = directoryInfo.FullName,
                        RelativePath = GetRelativePath(directoryInfo.FullName),
                        LastModified = directoryInfo.LastWriteTime,
                        ItemCount = CountVisibleItems(directoryInfo.FullName)
                    });
                }

                foreach (var filePath in Directory.EnumerateFiles(currentFolder, "*.txt", SearchOption.TopDirectoryOnly))
                {
                    var fileInfo = new FileInfo(filePath);
                    if (!fileInfo.Exists || IsTrashPath(fileInfo.FullName))
                    {
                        continue;
                    }

                    items.Add(new NoteItem
                    {
                        Title = Path.GetFileNameWithoutExtension(fileInfo.Name),
                        FullPath = fileInfo.FullName,
                        LastModified = fileInfo.LastWriteTime,
                        SizeBytes = fileInfo.Length
                    });
                }
            }
            catch
            {
                return Array.Empty<object>();
            }

            var order = ReadFolderOrder(currentFolder);
            return items
                .OrderBy(item => order.TryGetValue(GetOrderKey(item), out var index) ? index : int.MaxValue)
                .ThenBy(GetOrderKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
        });
    }

    public async Task<NoteOperationResult> SaveFolderOrderAsync(string folderPath, IEnumerable<string> orderedPaths)
    {
        try
        {
            var folder = ResolveFolderOrRoot(folderPath);
            var order = orderedPaths
                .Where(path => IsDirectChild(folder, path))
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            await File.WriteAllTextAsync(
                Path.Combine(folder, OrderFileName),
                JsonSerializer.Serialize(order, new JsonSerializerOptions { WriteIndented = true }),
                Utf8Encoding);
            RaiseNotesChanged();
            return new NoteOperationResult(true, "Порядок плиток сохранён.");
        }
        catch (Exception ex)
        {
            return new NoteOperationResult(false, $"Не удалось сохранить порядок плиток: {ex.Message}");
        }
    }

    public string ResolveFolderOrRoot(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return Path.GetFullPath(_config.NotesFolderPath);
        }

        var fullPath = Path.GetFullPath(folderPath);
        return IsInsideNotesRoot(fullPath) && !IsTrashPath(fullPath)
            ? fullPath
            : Path.GetFullPath(_config.NotesFolderPath);
    }

    public string? GetParentFolder(string folderPath)
    {
        var current = ResolveFolderOrRoot(folderPath);
        var root = Path.GetFullPath(_config.NotesFolderPath).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(current.TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Directory.GetParent(current)?.FullName;
    }

    public bool IsRootFolder(string folderPath)
    {
        var root = Path.GetFullPath(_config.NotesFolderPath).TrimEnd(Path.DirectorySeparatorChar);
        var current = ResolveFolderOrRoot(folderPath).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(root, current, StringComparison.OrdinalIgnoreCase);
    }

    public string GetRelativePath(string folderPath)
    {
        var root = Path.GetFullPath(_config.NotesFolderPath);
        var relativePath = Path.GetRelativePath(root, Path.GetFullPath(folderPath));
        return relativePath == "." ? string.Empty : relativePath;
    }

    public async Task<NoteTextResult> ReadNoteAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                RaiseNotesChanged();
                return new NoteTextResult(false, "Файл не найден.");
            }

            var content = await File.ReadAllTextAsync(filePath, Utf8Encoding);
            return new NoteTextResult(true, "Файл открыт.", content);
        }
        catch (Exception ex)
        {
            return new NoteTextResult(false, $"Не удалось прочитать заметку: {ex.Message}");
        }
    }

    public async Task<NoteOperationResult> SaveNoteAsync(string filePath, string content)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                RaiseNotesChanged();
                return new NoteOperationResult(false, "Файл был удалён или перемещён.");
            }

            await File.WriteAllTextAsync(filePath, content, Utf8Encoding);
            RaiseNotesChanged();
            return new NoteOperationResult(true, "Изменения сохранены.", filePath);
        }
        catch (Exception ex)
        {
            return new NoteOperationResult(false, $"Не удалось сохранить заметку: {ex.Message}");
        }
    }

    public async Task<NoteOperationResult> CreateNoteAsync(string? requestedTitle, string? folderPath = null)
    {
        try
        {
            var targetFolder = ResolveFolderOrRoot(folderPath);
            Directory.CreateDirectory(targetFolder);
            EnsureWatcherIfPossible();

            var baseFileName = BuildFileName(requestedTitle);
            var finalPath = ResolveUniquePath(targetFolder, baseFileName);
            await File.WriteAllTextAsync(finalPath, string.Empty, Utf8Encoding);
            RaiseNotesChanged();

            return new NoteOperationResult(true, "Заметка создана.", finalPath);
        }
        catch (Exception ex)
        {
            return new NoteOperationResult(false, $"Не удалось создать заметку: {ex.Message}");
        }
    }

    public Task<NoteOperationResult> CreateFolderAsync(string? requestedName, string? parentFolderPath)
    {
        return Task.Run(() =>
        {
            try
            {
                var parentFolder = ResolveFolderOrRoot(parentFolderPath);
                var folderName = BuildFolderName(requestedName);
                var targetPath = ResolveUniqueFolderPath(parentFolder, folderName);
                Directory.CreateDirectory(targetPath);
                RaiseNotesChanged();
                return new NoteOperationResult(true, "Папка создана.", targetPath);
            }
            catch (Exception ex)
            {
                return new NoteOperationResult(false, $"Не удалось создать папку: {ex.Message}");
            }
        });
    }

    public Task<NoteOperationResult> RenameFolderAsync(string folderPath, string newName)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    RaiseNotesChanged();
                    return new NoteOperationResult(false, "Папка не найдена.");
                }

                var parent = Directory.GetParent(folderPath)?.FullName;
                if (string.IsNullOrWhiteSpace(parent))
                {
                    return new NoteOperationResult(false, "Не удалось определить родительскую папку.");
                }

                var targetPath = ResolveUniqueFolderPath(parent, BuildFolderName(newName));
                Directory.Move(folderPath, targetPath);
                RaiseNotesChanged();
                return new NoteOperationResult(true, "Папка переименована.", targetPath);
            }
            catch (Exception ex)
            {
                return new NoteOperationResult(false, $"Не удалось переименовать папку: {ex.Message}");
            }
        });
    }

    public Task<NoteOperationResult> DeleteFolderIfEmptyAsync(string folderPath)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    RaiseNotesChanged();
                    return new NoteOperationResult(false, "Папка не найдена.");
                }

                if (Directory.EnumerateFileSystemEntries(folderPath).Any())
                {
                    return new NoteOperationResult(false, "Папка не пуста.");
                }

                Directory.Delete(folderPath);
                RaiseNotesChanged();
                return new NoteOperationResult(true, "Папка удалена.");
            }
            catch (Exception ex)
            {
                return new NoteOperationResult(false, $"Не удалось удалить папку: {ex.Message}");
            }
        });
    }

    public async Task<NoteOperationResult> MoveFolderToTrashAsync(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath))
            {
                RaiseNotesChanged();
                return new NoteOperationResult(false, "Папка не найдена.");
            }

            var source = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
            if (IsRootFolder(source))
            {
                return new NoteOperationResult(false, "Корневую папку заметок нельзя удалить.");
            }

            Directory.CreateDirectory(TrashFoldersPath);
            var folderName = Path.GetFileName(source);
            var trashName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}__{folderName}";
            var trashPath = Path.Combine(TrashFoldersPath, trashName);
            Directory.Move(source, trashPath);

            var metadata = new TrashMetadata
            {
                OriginalPath = source,
                OriginalName = folderName,
                DeletedAt = DateTime.Now
            };
            await File.WriteAllTextAsync($"{trashPath}.meta.json", JsonSerializer.Serialize(metadata, new JsonSerializerOptions
            {
                WriteIndented = true
            }), Utf8Encoding);

            RaiseNotesChanged();
            return new NoteOperationResult(true, "Папка перемещена в мусорку.", trashPath);
        }
        catch (Exception ex)
        {
            return new NoteOperationResult(false, $"Не удалось переместить папку в мусорку: {ex.Message}");
        }
    }

    public async Task<NoteOperationResult> MoveNoteToTrashAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                RaiseNotesChanged();
                return new NoteOperationResult(false, "Файл уже был удалён.");
            }

            Directory.CreateDirectory(TrashFolderPath);
            var fileName = Path.GetFileName(filePath);
            var trashName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}__{fileName}";
            var trashPath = Path.Combine(TrashFolderPath, trashName);
            File.Move(filePath, trashPath);

            var metadata = new TrashMetadata
            {
                OriginalPath = filePath,
                OriginalName = fileName,
                DeletedAt = DateTime.Now
            };
            await File.WriteAllTextAsync($"{trashPath}.meta.json", JsonSerializer.Serialize(metadata, new JsonSerializerOptions
            {
                WriteIndented = true
            }), Utf8Encoding);

            RaiseNotesChanged();
            return new NoteOperationResult(true, "Заметка перемещена в мусорку.", trashPath);
        }
        catch (Exception ex)
        {
            return new NoteOperationResult(false, $"Не удалось переместить заметку в мусорку: {ex.Message}");
        }
    }

    public Task<NoteOperationResult> MoveNoteToFolderAsync(string filePath, string targetFolder)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    RaiseNotesChanged();
                    return new NoteOperationResult(false, "Файл не найден.");
                }

                var folder = ResolveFolderOrRoot(targetFolder);
                Directory.CreateDirectory(folder);
                var targetPath = ResolveUniquePath(folder, Path.GetFileName(filePath));
                File.Move(filePath, targetPath);
                RaiseNotesChanged();
                return new NoteOperationResult(true, "Заметка перемещена.", targetPath);
            }
            catch (Exception ex)
            {
                return new NoteOperationResult(false, $"Не удалось переместить заметку: {ex.Message}");
            }
        });
    }

    public Task<NoteOperationResult> MoveFolderToFolderAsync(string folderPath, string targetFolder)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    RaiseNotesChanged();
                    return new NoteOperationResult(false, "Папка не найдена.");
                }

                var source = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
                var target = ResolveFolderOrRoot(targetFolder).TrimEnd(Path.DirectorySeparatorChar);

                if (IsRootFolder(source))
                {
                    return new NoteOperationResult(false, "Корневую папку заметок нельзя перемещать.");
                }

                if (!CanMoveFolderToFolder(source, target))
                {
                    return new NoteOperationResult(false, "Нельзя переместить папку в саму себя или вложенную папку.");
                }

                Directory.CreateDirectory(target);
                var targetPath = ResolveUniqueFolderPath(target, Path.GetFileName(source));
                Directory.Move(source, targetPath);
                RaiseNotesChanged();
                return new NoteOperationResult(true, "Папка перемещена.", targetPath);
            }
            catch (Exception ex)
            {
                return new NoteOperationResult(false, $"Не удалось переместить папку: {ex.Message}");
            }
        });
    }

    public bool CanMoveFolderToFolder(string folderPath, string targetFolder)
    {
        var source = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
        var target = ResolveFolderOrRoot(targetFolder).TrimEnd(Path.DirectorySeparatorChar);
        var sourceWithSeparator = source + Path.DirectorySeparatorChar;
        var targetWithSeparator = target + Path.DirectorySeparatorChar;

        return !string.Equals(source, target, StringComparison.OrdinalIgnoreCase) &&
               !targetWithSeparator.StartsWith(sourceWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    public Task<IReadOnlyList<FolderItem>> GetFoldersAsync()
    {
        return Task.Run<IReadOnlyList<FolderItem>>(() =>
        {
            var folders = new List<FolderItem>();
            if (!Directory.Exists(_config.NotesFolderPath))
            {
                return folders;
            }

            folders.Add(new FolderItem
            {
                Name = "В корень",
                FullPath = Path.GetFullPath(_config.NotesFolderPath),
                RelativePath = string.Empty,
                LastModified = Directory.GetLastWriteTime(_config.NotesFolderPath),
                ItemCount = CountVisibleItems(_config.NotesFolderPath)
            });

            foreach (var folderPath in Directory.EnumerateDirectories(_config.NotesFolderPath, "*", SearchOption.AllDirectories))
            {
                if (IsTrashPath(folderPath))
                {
                    continue;
                }

                var info = new DirectoryInfo(folderPath);
                folders.Add(new FolderItem
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    RelativePath = GetRelativePath(info.FullName),
                    LastModified = info.LastWriteTime,
                    ItemCount = CountVisibleItems(info.FullName)
                });
            }

            return folders.OrderBy(folder => folder.RelativePath).ToList();
        });
    }

    public async Task<IReadOnlyList<TrashItem>> GetTrashItemsAsync()
    {
        return await Task.Run(async () =>
        {
            if (!Directory.Exists(TrashFolderPath))
            {
                return Array.Empty<TrashItem>();
            }

            var items = new List<TrashItem>();
            foreach (var filePath in Directory.EnumerateFiles(TrashFolderPath, "*.txt", SearchOption.TopDirectoryOnly))
            {
                var metadataPath = $"{filePath}.meta.json";
                TrashMetadata? metadata = null;
                if (File.Exists(metadataPath))
                {
                    try
                    {
                        metadata = JsonSerializer.Deserialize<TrashMetadata>(await File.ReadAllTextAsync(metadataPath, Utf8Encoding));
                    }
                    catch
                    {
                        metadata = null;
                    }
                }

                var info = new FileInfo(filePath);
                items.Add(new TrashItem
                {
                    DisplayName = metadata?.OriginalName ?? info.Name,
                    TrashPath = info.FullName,
                    MetadataPath = File.Exists(metadataPath) ? metadataPath : null,
                    OriginalPath = metadata?.OriginalPath ?? Path.Combine(_config.NotesFolderPath, info.Name),
                    DeletedAt = metadata?.DeletedAt ?? info.LastWriteTime
                });
            }

            if (Directory.Exists(TrashFoldersPath))
            {
                foreach (var folderPath in Directory.EnumerateDirectories(TrashFoldersPath, "*", SearchOption.TopDirectoryOnly))
                {
                    var metadataPath = $"{folderPath}.meta.json";
                    TrashMetadata? metadata = null;
                    if (File.Exists(metadataPath))
                    {
                        try
                        {
                            metadata = JsonSerializer.Deserialize<TrashMetadata>(await File.ReadAllTextAsync(metadataPath, Utf8Encoding));
                        }
                        catch
                        {
                            metadata = null;
                        }
                    }

                    var info = new DirectoryInfo(folderPath);
                    items.Add(new TrashItem
                    {
                        DisplayName = metadata?.OriginalName ?? info.Name,
                        TrashPath = info.FullName,
                        MetadataPath = File.Exists(metadataPath) ? metadataPath : null,
                        OriginalPath = metadata?.OriginalPath ?? Path.Combine(_config.NotesFolderPath, info.Name),
                        DeletedAt = metadata?.DeletedAt ?? info.LastWriteTime
                    });
                }
            }

            return (IReadOnlyList<TrashItem>)items.OrderByDescending(item => item.DeletedAt).ToList();
        });
    }

    public Task<NoteOperationResult> RestoreTrashItemAsync(TrashItem item)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!File.Exists(item.TrashPath) && !Directory.Exists(item.TrashPath))
                {
                    return new NoteOperationResult(false, "Элемент в мусорке не найден.");
                }

                var targetFolder = Directory.Exists(Path.GetDirectoryName(item.OriginalPath))
                    ? Path.GetDirectoryName(item.OriginalPath)!
                    : _config.NotesFolderPath;
                Directory.CreateDirectory(targetFolder);
                var targetPath = File.Exists(item.TrashPath)
                    ? ResolveUniquePath(targetFolder, Path.GetFileName(item.OriginalPath))
                    : ResolveUniqueFolderPath(targetFolder, Path.GetFileName(item.OriginalPath));

                if (File.Exists(item.TrashPath))
                {
                    File.Move(item.TrashPath, targetPath);
                }
                else
                {
                    Directory.Move(item.TrashPath, targetPath);
                }

                DeleteMetadata(item.MetadataPath);
                RaiseNotesChanged();
                return new NoteOperationResult(true, "Элемент восстановлен.", targetPath);
            }
            catch (Exception ex)
            {
                return new NoteOperationResult(false, $"Не удалось восстановить заметку: {ex.Message}");
            }
        });
    }

    public Task<NoteOperationResult> DeleteTrashItemPermanentlyAsync(TrashItem item)
    {
        return Task.Run(() =>
        {
            try
            {
                if (File.Exists(item.TrashPath))
                {
                    File.Delete(item.TrashPath);
                }
                else if (Directory.Exists(item.TrashPath))
                {
                    Directory.Delete(item.TrashPath, true);
                }

                DeleteMetadata(item.MetadataPath);
                return new NoteOperationResult(true, "Элемент удалён навсегда.");
            }
            catch (Exception ex)
            {
                return new NoteOperationResult(false, $"Не удалось удалить заметку навсегда: {ex.Message}");
            }
        });
    }

    public Task<NoteOperationResult> EmptyTrashAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(TrashFolderPath))
                {
                    Directory.Delete(TrashFolderPath, true);
                }

                return new NoteOperationResult(true, "Мусорка очищена.");
            }
            catch (Exception ex)
            {
                return new NoteOperationResult(false, $"Не удалось очистить мусорку: {ex.Message}");
            }
        });
    }

    public NoteOperationResult CopyFileToClipboard(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new NoteOperationResult(false, "Файл не найден.");
            }

            var files = new StringCollection { filePath };
            System.Windows.Clipboard.SetFileDropList(files);
            return new NoteOperationResult(true, "Файл скопирован в буфер обмена.");
        }
        catch (Exception ex)
        {
            return new NoteOperationResult(false, $"Не удалось скопировать файл: {ex.Message}");
        }
    }

    public async Task<NoteOperationResult> CopyContentToClipboardAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new NoteOperationResult(false, "Файл не найден.");
            }

            System.Windows.Clipboard.SetText(await File.ReadAllTextAsync(filePath, Utf8Encoding));
            return new NoteOperationResult(true, "Содержимое скопировано в буфер обмена.");
        }
        catch (Exception ex)
        {
            return new NoteOperationResult(false, $"Не удалось скопировать содержимое: {ex.Message}");
        }
    }

    public void Dispose()
    {
        lock (_watcherLock)
        {
            if (_watcher is null)
            {
                return;
            }

            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnWatchedFilesChanged;
            _watcher.Changed -= OnWatchedFilesChanged;
            _watcher.Deleted -= OnWatchedFilesChanged;
            _watcher.Renamed -= OnWatchedFilesChanged;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private string BuildFileName(string? requestedTitle)
    {
        var title = BuildSafeName(requestedTitle, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        return $"{title}.txt";
    }

    private string BuildFolderName(string? requestedName)
    {
        return BuildSafeName(requestedName, "Новая папка");
    }

    private string BuildSafeName(string? value, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private string ResolveUniquePath(string folder, string fileName)
    {
        var candidatePath = Path.Combine(folder, fileName);
        if (!File.Exists(candidatePath))
        {
            return candidatePath;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        while (true)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
            candidatePath = Path.Combine(folder, $"{baseName}_{timestamp}{extension}");

            if (!File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }
    }

    private string ResolveUniqueFolderPath(string parentFolder, string folderName)
    {
        var candidatePath = Path.Combine(parentFolder, folderName);
        if (!Directory.Exists(candidatePath))
        {
            return candidatePath;
        }

        while (true)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
            candidatePath = Path.Combine(parentFolder, $"{folderName}_{timestamp}");

            if (!Directory.Exists(candidatePath))
            {
                return candidatePath;
            }
        }
    }

    private int CountVisibleItems(string folderPath)
    {
        try
        {
            var folderCount = Directory.EnumerateDirectories(folderPath, "*", SearchOption.TopDirectoryOnly)
                .Count(path => !IsTrashPath(path) && !IsHiddenOrSystem(path));
            var noteCount = Directory.EnumerateFiles(folderPath, "*.txt", SearchOption.TopDirectoryOnly)
                .Count(path => !IsTrashPath(path) && !IsHiddenOrSystem(path));
            return folderCount + noteCount;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsHiddenOrSystem(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System);
        }
        catch
        {
            return true;
        }
    }

    private Dictionary<string, int> ReadFolderOrder(string folderPath)
    {
        var orderPath = Path.Combine(folderPath, OrderFileName);
        if (!File.Exists(orderPath))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var names = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(orderPath, Utf8Encoding)) ?? [];
            return names
                .Select((name, index) => new { name, index })
                .GroupBy(item => item.name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string GetOrderKey(object item)
    {
        return item switch
        {
            FolderItem folder => Path.GetFileName(folder.FullPath),
            NoteItem note => Path.GetFileName(note.FullPath),
            _ => string.Empty
        };
    }

    private static bool IsDirectChild(string folder, string path)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        return string.Equals(
            Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar),
            parent?.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInsideNotesRoot(string path)
    {
        var root = Path.GetFullPath(_config.NotesFolderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsTrashPath(string path)
    {
        var trash = Path.GetFullPath(TrashFolderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(trash, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), trash.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteMetadata(string? metadataPath)
    {
        if (!string.IsNullOrWhiteSpace(metadataPath) && File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }
    }

    private void EnsureWatcherIfPossible()
    {
        if (!Directory.Exists(_config.NotesFolderPath))
        {
            Dispose();
            return;
        }

        lock (_watcherLock)
        {
            if (_watcher is not null &&
                string.Equals(_watcher.Path, _config.NotesFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Dispose();
            _watcher = new FileSystemWatcher(_config.NotesFolderPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnWatchedFilesChanged;
            _watcher.Changed += OnWatchedFilesChanged;
            _watcher.Deleted += OnWatchedFilesChanged;
            _watcher.Renamed += OnWatchedFilesChanged;
        }
    }

    private void OnWatchedFilesChanged(object sender, FileSystemEventArgs e)
    {
        if (IsTrashPath(e.FullPath))
        {
            return;
        }

        RaiseNotesChanged();
    }

    private void RaiseNotesChanged()
    {
        NotesChanged?.Invoke(this, EventArgs.Empty);
    }
}
