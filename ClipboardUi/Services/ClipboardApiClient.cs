using System;
using System.Net.Http.Json;

namespace ClipboardUi.Services;

public sealed class ClipboardApiClient
{
    private readonly HttpClient _http;

    public ClipboardApiClient(HttpClient http)
    {
        _http = http;
    }

    public Uri BaseAddress => _http.BaseAddress ?? throw new InvalidOperationException("Api base address missing.");

    public async Task<List<TextEntry>> GetRecentTextAsync(int limit)
    {
        var items = await _http.GetFromJsonAsync<List<TextEntry>>($"/api/text?limit={limit}");
        return items ?? [];
    }

    public async Task<List<ImageEntry>> GetRecentImagesAsync(int limit)
    {
        var items = await _http.GetFromJsonAsync<List<ImageEntry>>($"/api/images?limit={limit}");
        return items ?? [];
    }

    public async Task<List<HierarchyEntry>> GetHierarchyAsync(int limit = 1000)
    {
        var items = await _http.GetFromJsonAsync<List<HierarchyEntry>>($"/api/hierarchy?limit={limit}");
        return items ?? [];
    }

    public async Task<HierarchyEntry> CreateHierarchyAsync(CreateHierarchyRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/hierarchy", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<HierarchyEntry>())!;
    }

    public async Task UpdateHierarchyAsync(int id, UpdateHierarchyRequest request)
    {
        var response = await _http.PutAsJsonAsync($"/api/hierarchy/{id}", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteHierarchyAsync(int id)
    {
        var response = await _http.DeleteAsync($"/api/hierarchy/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<StoredTextEntry>> GetStoredTextEntriesAsync(int limit = 200, int? hierarchyId = null)
    {
        var query = hierarchyId is null
            ? $"/api/stored-text?limit={limit}"
            : $"/api/stored-text?limit={limit}&hierarchyId={hierarchyId.Value}";
        var items = await _http.GetFromJsonAsync<List<StoredTextEntry>>(query);
        return items ?? [];
    }

    public async Task<StoredTextEntry?> GetStoredTextEntryAsync(int id)
    {
        return await _http.GetFromJsonAsync<StoredTextEntry>($"/api/stored-text/{id}");
    }

    public async Task<StoredTextEntry> CreateStoredFromClipboardAsync(CreateStoredFromClipboardRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/stored-text/from-clipboard", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StoredTextEntry>())!;
    }

    public async Task UpdateStoredTextAsync(int id, UpdateStoredTextRequest request)
    {
        var response = await _http.PutAsJsonAsync($"/api/stored-text/{id}", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteStoredTextAsync(int id)
    {
        var response = await _http.DeleteAsync($"/api/stored-text/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task SetClipboardTextAsync(string text)
    {
        var response = await _http.PostAsJsonAsync("/api/clipboard/set-text", new SetClipboardTextRequest(text));
        response.EnsureSuccessStatusCode();
    }

    public async Task SetClipboardStoredTextAsync(int id)
    {
        var response = await _http.PostAsJsonAsync("/api/clipboard/set-stored-text", new SetStoredClipboardRequest(id));
        response.EnsureSuccessStatusCode();
    }
}

public sealed record TextEntry(int Id, string Content, DateTimeOffset CreatedAt, string Language);

public sealed record ImageEntry(int Id, DateTimeOffset CreatedAt, string Base64Data);

public sealed record HierarchyEntry(int Id, int? ParentId, string Name, DateTimeOffset CreatedAt);

public sealed record StoredTextEntry(int Id, int? HierarchyId, string Name, string Content, DateTimeOffset CreatedAt, string Language);

public sealed record CreateStoredFromClipboardRequest(int TextEntryId, string Name, int? HierarchyId);

public sealed record UpdateStoredTextRequest(int? HierarchyId, string Name, string Content, string Language);

public sealed record CreateHierarchyRequest(int? ParentId, string Name);

public sealed record UpdateHierarchyRequest(int? ParentId, string Name);

public sealed record SetClipboardTextRequest(string Text);

public sealed record SetStoredClipboardRequest(int Id);
