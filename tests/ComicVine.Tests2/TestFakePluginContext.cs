using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ComicRow.PluginSystem;

namespace ComicVine.Tests2
{
    public class TestFakePluginContext : IPluginContext
    {
        public Guid PluginId => Guid.NewGuid();
        public string PluginName => "ComicVineTest";
        public Dictionary<string, string> Values { get; } = new();
        public bool HasPermission(string permission) => true;
        public IReadOnlyList<string> GetGrantedPermissions() => new[] { "comic:read", "comic:metadata:write", "http:comicvine.gamespot.com" };
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Debug(string message) { }
        public Task<string?> GetValueAsync(string key)
        {
            Values.TryGetValue(key, out var v);
            return Task.FromResult<string?>(v);
        }
        public Task<string?> GetValueAsync(string category, string key) => Task.FromResult<string?>(null);
        public Task SetValueAsync(string key, string value) { Values[key] = value; return Task.CompletedTask; }
        public Task<bool> DeleteValueAsync(string key) => Task.FromResult(Values.Remove(key));
        public Task<bool> HasKeyAsync(string key) => Task.FromResult(Values.ContainsKey(key));
        public Task<ComicMetadata> GetComicMetadataAsync(Guid comicId) => Task.FromResult<ComicMetadata?>(null)!;
        public Task<IReadOnlyList<ComicMetadata>> GetComicsMetadataAsync(IEnumerable<Guid> comicIds) => Task.FromResult<IReadOnlyList<ComicMetadata>>(new List<ComicMetadata>());
        public Task<ComicFileInfo> GetComicFileInfoAsync(Guid comicId) => Task.FromResult<ComicFileInfo?>(null)!;
        public Task<Dictionary<string, string>> GetComicTagsAsync(Guid comicId) => Task.FromResult(new Dictionary<string,string>());
        public Task<string?> GetComicTagAsync(Guid comicId, string tagName) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<Guid>> GetComicsInSmartListAsync(string smartListNameOrId) => Task.FromResult<IReadOnlyList<Guid>>(new List<Guid>());
        public Task<IReadOnlyList<Guid>> GetAllComicIdsAsync() => Task.FromResult<IReadOnlyList<Guid>>(new List<Guid>());
        public Task<bool> UpdateComicMetadataAsync(Guid comicId, ComicMetadataUpdate update) => Task.FromResult(true);
        public Task<BatchUpdateResult> BatchUpdateMetadataAsync(IEnumerable<ComicMetadataUpdate> updates) => Task.FromResult(new BatchUpdateResult());
        public Task<MoveFileResult> RequestMoveFileAsync(Guid comicId, string newPath) => Task.FromResult(new MoveFileResult { Success = true, NewPath = newPath });
        public Task SetComicTagAsync(Guid comicId, string tagName, string value) => Task.CompletedTask;
        public Task RemoveComicTagAsync(Guid comicId, string tagName) => Task.CompletedTask;
        public Task<string> GetAsync(string url, Dictionary<string, string>? headers = null) => Task.FromResult(string.Empty);
        public Task<string> PostAsync(string url, string body, string contentType = "application/json", Dictionary<string, string>? headers = null) => Task.FromResult(string.Empty);
        public Task<byte[]> GetBytesAsync(string url, Dictionary<string, string>? headers = null) => Task.FromResult(new byte[0]);
        public Task<BackupResult> RequestCreateBackupAsync() => Task.FromResult(new BackupResult { Success = true });
        public Task<BackupResult> RequestRestoreBackupAsync(string backupPath) => Task.FromResult(new BackupResult { Success = true });
        public Task<IReadOnlyList<BackupInfo>> RequestListBackupsAsync() => Task.FromResult<IReadOnlyList<BackupInfo>>(new List<BackupInfo>());
        public Task<bool> RequestDeleteBackupAsync(string backupPath) => Task.FromResult(true);
    }
}
