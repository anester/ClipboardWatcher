using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace ClipboardWatcher;

public record ClipboardTextEntry(int Id, string Content, DateTimeOffset CreatedAt, string Language);
public record ClipboardImageEntry(int Id, byte[] Data, DateTimeOffset CreatedAt);
public record HierarchyEntry(int Id, int? ParentId, string Name, DateTimeOffset CreatedAt);
public record StoredTextEntry(int Id, int? HierarchyId, string Name, string Content, DateTimeOffset CreatedAt, string Language);
public record NoteDayEntry(int Id, DateTimeOffset CreatedAt, long CreatedAtTicks);
public record NoteEntry(int Id, DateTimeOffset CreatedAt, long CreatedAtTicks, string MarkDownContents, string CompiledHtml);

public class ClipboardStore
{
    private readonly string _connectionString;

    public ClipboardStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = $"Data Source={databasePath}";

        Initialize();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync();
        return connection;
    }

    private void Initialize()
    {
        using var connection = OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS TextEntries(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Content TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                Language TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ImageEntries(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Data BLOB NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Hierarchy(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ParentId INTEGER NULL,
                Name TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (ParentId) REFERENCES Hierarchy(Id) ON DELETE SET NULL
            );
            CREATE TABLE IF NOT EXISTS StoredTextEntries(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                HierarchyId INTEGER NULL,
                Name TEXT NOT NULL,
                Content TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                Language TEXT NOT NULL,
                FOREIGN KEY (HierarchyId) REFERENCES Hierarchy(Id) ON DELETE SET NULL
            );
            CREATE TABLE IF NOT EXISTS Notes(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CreatedAt TEXT NOT NULL,
                CreatedAtTicks INTEGER NOT NULL,
                MarkDownContents TEXT NOT NULL,
                CompiledHtml TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_TextEntries_CreatedAt ON TextEntries(CreatedAt DESC);
            CREATE INDEX IF NOT EXISTS IX_ImageEntries_CreatedAt ON ImageEntries(CreatedAt DESC);
            CREATE INDEX IF NOT EXISTS IX_Hierarchy_ParentId ON Hierarchy(ParentId);
            CREATE INDEX IF NOT EXISTS IX_Hierarchy_CreatedAt ON Hierarchy(CreatedAt DESC);
            CREATE INDEX IF NOT EXISTS IX_StoredTextEntries_HierarchyId ON StoredTextEntries(HierarchyId);
            CREATE INDEX IF NOT EXISTS IX_StoredTextEntries_CreatedAt ON StoredTextEntries(CreatedAt DESC);
            CREATE INDEX IF NOT EXISTS IX_Notes_CreatedAtTicks ON Notes(CreatedAtTicks DESC);
            """;
        command.ExecuteNonQuery();

        EnsureTextEntriesLanguageColumn(connection);
        EnsureStoredTextEntriesLanguageColumn(connection);
    }

    private static void EnsureTextEntriesLanguageColumn(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(TextEntries);";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), "Language", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE TextEntries ADD COLUMN Language TEXT NOT NULL DEFAULT 'Text';";
        alter.ExecuteNonQuery();
    }

    private static void EnsureStoredTextEntriesLanguageColumn(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(StoredTextEntries);";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), "Language", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE StoredTextEntries ADD COLUMN Language TEXT NOT NULL DEFAULT 'Text';";
        alter.ExecuteNonQuery();
    }

    public async Task<ClipboardTextEntry?> SaveTextAsync(string content, string language)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        await using var connection = await OpenConnectionAsync();

        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var createdAt = DateTimeOffset.UtcNow;
        var normalizedLanguage = string.IsNullOrWhiteSpace(language)
            ? ClipboardLanguageDetector.Text
            : language;
        int id;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText =
                """
                INSERT INTO TextEntries(Content, CreatedAt, Language)
                VALUES ($content, $createdAt, $language);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$createdAt", createdAt.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$language", normalizedLanguage);
            id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        await using (var trimCmd = connection.CreateCommand())
        {
            trimCmd.Transaction = transaction;
            trimCmd.CommandText = "DELETE FROM TextEntries WHERE Id NOT IN (SELECT Id FROM TextEntries ORDER BY Id DESC LIMIT 500);";
            await trimCmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return new ClipboardTextEntry(id, content, createdAt, normalizedLanguage);
    }

    public async Task<ClipboardImageEntry?> SaveImageAsync(byte[] data)
    {
        if (data is not { Length: > 0 })
        {
            return null;
        }

        await using var connection = await OpenConnectionAsync();

        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var createdAt = DateTimeOffset.UtcNow;
        int id;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText =
                """
                INSERT INTO ImageEntries(Data, CreatedAt)
                VALUES ($data, $createdAt);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$data", data);
            cmd.Parameters.AddWithValue("$createdAt", createdAt.ToString("O", CultureInfo.InvariantCulture));
            id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        await using (var trimCmd = connection.CreateCommand())
        {
            trimCmd.Transaction = transaction;
            trimCmd.CommandText = "DELETE FROM ImageEntries WHERE Id NOT IN (SELECT Id FROM ImageEntries ORDER BY Id DESC LIMIT 10);";
            await trimCmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return new ClipboardImageEntry(id, data, createdAt);
    }

    public async Task<ClipboardTextEntry?> GetLatestTextAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Content, CreatedAt, Language FROM TextEntries ORDER BY Id DESC LIMIT 1;";

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ClipboardTextEntry(
                reader.GetInt32(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(3) ? ClipboardLanguageDetector.Text : reader.GetString(3));
        }

        return null;
    }

    public async Task<IReadOnlyList<ClipboardTextEntry>> GetRecentTextAsync(int limit = 500)
    {
        var results = new List<ClipboardTextEntry>(limit);

        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Content, CreatedAt, Language FROM TextEntries ORDER BY Id DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ClipboardTextEntry(
                reader.GetInt32(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(3) ? ClipboardLanguageDetector.Text : reader.GetString(3)));
        }

        return results;
    }

    public async Task<ClipboardImageEntry?> GetLatestImageAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Data, CreatedAt FROM ImageEntries ORDER BY Id DESC LIMIT 1;";

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ClipboardImageEntry(
                reader.GetInt32(0),
                (byte[])reader["Data"],
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }

        return null;
    }

    public async Task<IReadOnlyList<ClipboardImageEntry>> GetRecentImagesAsync(int limit = 10)
    {
        var results = new List<ClipboardImageEntry>(limit);

        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Data, CreatedAt FROM ImageEntries ORDER BY Id DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ClipboardImageEntry(
                reader.GetInt32(0),
                (byte[])reader["Data"],
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return results;
    }

    public async Task<ClipboardTextEntry?> GetTextByIdAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Content, CreatedAt, Language FROM TextEntries WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ClipboardTextEntry(
                reader.GetInt32(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(3) ? ClipboardLanguageDetector.Text : reader.GetString(3));
        }

        return null;
    }

    public async Task<bool> HierarchyExistsAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Hierarchy WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    public async Task<HierarchyEntry> CreateHierarchyAsync(int? parentId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        var createdAt = DateTimeOffset.UtcNow;
        cmd.CommandText =
            """
            INSERT INTO Hierarchy(ParentId, Name, CreatedAt)
            VALUES ($parentId, $name, $createdAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$parentId", (object?)parentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$createdAt", createdAt.ToString("O", CultureInfo.InvariantCulture));

        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return new HierarchyEntry(id, parentId, name, createdAt);
    }

    public async Task<HierarchyEntry?> GetHierarchyAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, ParentId, Name, CreatedAt FROM Hierarchy WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new HierarchyEntry(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }

        return null;
    }

    public async Task<IReadOnlyList<HierarchyEntry>> ListHierarchyAsync(int limit = 1000)
    {
        var results = new List<HierarchyEntry>(Math.Min(limit, 1000));
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, ParentId, Name, CreatedAt FROM Hierarchy ORDER BY Id ASC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new HierarchyEntry(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return results;
    }

    public async Task<bool> UpdateHierarchyAsync(int id, int? parentId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (parentId == id)
        {
            throw new InvalidOperationException("ParentId cannot be the same as Id.");
        }

        if (parentId.HasValue && await IsHierarchyDescendantAsync(descendantId: parentId.Value, ancestorId: id))
        {
            throw new InvalidOperationException("ParentId cannot be a descendant of the hierarchy being moved.");
        }

        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            UPDATE Hierarchy
            SET ParentId = $parentId,
                Name = $name
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$parentId", (object?)parentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", name);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<bool> DeleteHierarchyAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var moveStored = connection.CreateCommand())
        {
            moveStored.Transaction = transaction;
            moveStored.CommandText = "UPDATE StoredTextEntries SET HierarchyId = NULL WHERE HierarchyId = $id;";
            moveStored.Parameters.AddWithValue("$id", id);
            await moveStored.ExecuteNonQueryAsync();
        }

        await using (var moveChildren = connection.CreateCommand())
        {
            moveChildren.Transaction = transaction;
            moveChildren.CommandText = "UPDATE Hierarchy SET ParentId = NULL WHERE ParentId = $id;";
            moveChildren.Parameters.AddWithValue("$id", id);
            await moveChildren.ExecuteNonQueryAsync();
        }

        int rows;
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Hierarchy WHERE Id = $id;";
            delete.Parameters.AddWithValue("$id", id);
            rows = await delete.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return rows > 0;
    }

    public async Task<bool> IsHierarchyDescendantAsync(int descendantId, int ancestorId)
    {
        if (descendantId == ancestorId)
        {
            return true;
        }

        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            WITH RECURSIVE ancestors(Id) AS (
                SELECT ParentId FROM Hierarchy WHERE Id = $startId
                UNION ALL
                SELECT Hierarchy.ParentId
                FROM Hierarchy
                JOIN ancestors ON Hierarchy.Id = ancestors.Id
                WHERE ancestors.Id IS NOT NULL
            )
            SELECT 1 FROM ancestors WHERE Id = $ancestorId LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$startId", descendantId);
        cmd.Parameters.AddWithValue("$ancestorId", ancestorId);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    public async Task<StoredTextEntry> CreateStoredTextEntryFromClipboardAsync(int textEntryId, string name, int? hierarchyId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (hierarchyId.HasValue && !await HierarchyExistsAsync(hierarchyId.Value))
        {
            throw new InvalidOperationException($"HierarchyId {hierarchyId.Value} does not exist.");
        }

        var clipboard = await GetTextByIdAsync(textEntryId);
        if (clipboard is null)
        {
            throw new KeyNotFoundException($"TextEntries Id {textEntryId} not found.");
        }

        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        var createdAt = DateTimeOffset.UtcNow;
        var language = string.IsNullOrWhiteSpace(clipboard.Language) ? ClipboardLanguageDetector.Text : clipboard.Language;
        cmd.CommandText =
            """
            INSERT INTO StoredTextEntries(HierarchyId, Name, Content, CreatedAt, Language)
            VALUES ($hierarchyId, $name, $content, $createdAt, $language);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$hierarchyId", (object?)hierarchyId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$content", clipboard.Content);
        cmd.Parameters.AddWithValue("$createdAt", createdAt.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$language", language);

        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return new StoredTextEntry(id, hierarchyId, name, clipboard.Content, createdAt, language);
    }

    public async Task<StoredTextEntry?> GetStoredTextEntryAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, HierarchyId, Name, Content, CreatedAt, Language FROM StoredTextEntries WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new StoredTextEntry(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(5) ? ClipboardLanguageDetector.Text : reader.GetString(5));
        }

        return null;
    }

    public async Task<IReadOnlyList<StoredTextEntry>> ListStoredTextEntriesAsync(int limit = 100, int? hierarchyId = null)
    {
        var results = new List<StoredTextEntry>(limit);
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT Id, HierarchyId, Name, Content, CreatedAt, Language
            FROM StoredTextEntries
            WHERE ($hierarchyId IS NULL OR HierarchyId = $hierarchyId)
            ORDER BY Id DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$hierarchyId", (object?)hierarchyId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new StoredTextEntry(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(5) ? ClipboardLanguageDetector.Text : reader.GetString(5)));
        }

        return results;
    }

    public async Task<bool> UpdateStoredTextEntryAsync(int id, int? hierarchyId, string name, string content, string language)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content is required.", nameof(content));
        }

        if (hierarchyId.HasValue && !await HierarchyExistsAsync(hierarchyId.Value))
        {
            throw new InvalidOperationException($"HierarchyId {hierarchyId.Value} does not exist.");
        }

        var normalizedLanguage = string.IsNullOrWhiteSpace(language)
            ? ClipboardLanguageDetector.Text
            : language;

        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            UPDATE StoredTextEntries
            SET HierarchyId = $hierarchyId,
                Name = $name,
                Content = $content,
                Language = $language
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$hierarchyId", (object?)hierarchyId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.Parameters.AddWithValue("$language", normalizedLanguage);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<bool> DeleteStoredTextEntryAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM StoredTextEntries WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<IReadOnlyList<NoteDayEntry>> ListNoteDaysAsync()
    {
        var results = new List<NoteDayEntry>();
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, CreatedAt, CreatedAtTicks FROM Notes ORDER BY CreatedAtTicks DESC;";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new NoteDayEntry(
                reader.GetInt32(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetInt64(2)));
        }

        return results;
    }

    public async Task<NoteEntry?> GetNoteAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, CreatedAt, CreatedAtTicks, MarkDownContents, CompiledHtml FROM Notes WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new NoteEntry(
                reader.GetInt32(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4));
        }

        return null;
    }

    public async Task<NoteEntry> SaveNoteAsync(int id, DateTimeOffset createdAt, long createdAtTicks, string markDownContents, string compiledHtml)
    {
        await using var connection = await OpenConnectionAsync();
        var normalizedMarkdown = markDownContents ?? string.Empty;
        var normalizedHtml = compiledHtml ?? string.Empty;
        var createdAtValue = createdAt.ToString("O", CultureInfo.InvariantCulture);

        if (id > 0)
        {
            await using var update = connection.CreateCommand();
            update.CommandText =
                """
                UPDATE Notes
                SET CreatedAt = $createdAt,
                    CreatedAtTicks = $createdAtTicks,
                    MarkDownContents = $markdown,
                    CompiledHtml = $html
                WHERE Id = $id;
                """;
            update.Parameters.AddWithValue("$createdAt", createdAtValue);
            update.Parameters.AddWithValue("$createdAtTicks", createdAtTicks);
            update.Parameters.AddWithValue("$markdown", normalizedMarkdown);
            update.Parameters.AddWithValue("$html", normalizedHtml);
            update.Parameters.AddWithValue("$id", id);
            var rows = await update.ExecuteNonQueryAsync();
            if (rows > 0)
            {
                return new NoteEntry(id, createdAt, createdAtTicks, normalizedMarkdown, normalizedHtml);
            }
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO Notes(CreatedAt, CreatedAtTicks, MarkDownContents, CompiledHtml)
            VALUES ($createdAt, $createdAtTicks, $markdown, $html);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$createdAt", createdAtValue);
        insert.Parameters.AddWithValue("$createdAtTicks", createdAtTicks);
        insert.Parameters.AddWithValue("$markdown", normalizedMarkdown);
        insert.Parameters.AddWithValue("$html", normalizedHtml);
        var newId = Convert.ToInt32(await insert.ExecuteScalarAsync());

        return new NoteEntry(newId, createdAt, createdAtTicks, normalizedMarkdown, normalizedHtml);
    }
}
