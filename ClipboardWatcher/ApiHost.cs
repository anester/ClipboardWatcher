using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace ClipboardWatcher;

public sealed class ApiHost
{
    private readonly ClipboardStore _store;
    private readonly int _port;

    public ApiHost(ClipboardStore store, int port)
    {
        _store = store;
        _port = port;
    }

    public Task RunAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy.SetIsOriginAllowed(origin =>
                    Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback)
                      .AllowAnyHeader()
                      .AllowAnyMethod());
        });
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(_port);
        });

        var app = builder.Build();
        app.UseCors();

        app.MapGet("/api/text/latest", async Task<IResult> () =>
        {
            var entry = await _store.GetLatestTextAsync();
            return entry is null
                ? Results.NoContent()
                : Results.Json(entry);
        });

        app.MapGet("/api/text", async Task<IResult> (int? limit) =>
        {
            var take = Math.Clamp(limit ?? 500, 1, 500);
            var items = await _store.GetRecentTextAsync(take);
            return Results.Json(items);
        });

        app.MapGet("/api/images/latest", async Task<IResult> () =>
        {
            var entry = await _store.GetLatestImageAsync();
            return entry is null
                ? Results.NoContent()
                : Results.Json(ToImageDto(entry));
        });

        app.MapGet("/api/images", async Task<IResult> (int? limit) =>
        {
            var take = Math.Clamp(limit ?? 10, 1, 10);
            var items = await _store.GetRecentImagesAsync(take);
            return Results.Json(items.Select(ToImageDto).ToList());
        });

        // Hierarchy CRUD
        app.MapGet("/api/hierarchy", async Task<IResult> (int? limit) =>
        {
            var items = await _store.ListHierarchyAsync(limit ?? 1000);
            return Results.Json(items);
        });

        app.MapGet("/api/hierarchy/{id:int}", async Task<IResult> (int id) =>
        {
            var item = await _store.GetHierarchyAsync(id);
            return item is null ? Results.NotFound() : Results.Json(item);
        });

        app.MapPost("/api/hierarchy", async Task<IResult> (CreateHierarchyRequest req) =>
        {
            try
            {
                if (req.ParentId.HasValue && !await _store.HierarchyExistsAsync(req.ParentId.Value))
                {
                    return Results.BadRequest(new { error = "ParentId does not exist." });
                }

                var created = await _store.CreateHierarchyAsync(req.ParentId, req.Name);
                return Results.Json(created, statusCode: StatusCodes.Status201Created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPut("/api/hierarchy/{id:int}", async Task<IResult> (int id, UpdateHierarchyRequest req) =>
        {
            try
            {
                if (req.ParentId.HasValue && !await _store.HierarchyExistsAsync(req.ParentId.Value))
                {
                    return Results.BadRequest(new { error = "ParentId does not exist." });
                }

                var ok = await _store.UpdateHierarchyAsync(id, req.ParentId, req.Name);
                return ok ? Results.NoContent() : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapDelete("/api/hierarchy/{id:int}", async Task<IResult> (int id) =>
        {
            var ok = await _store.DeleteHierarchyAsync(id);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        // Stored text entries
        app.MapGet("/api/stored-text", async Task<IResult> (int? limit, int? hierarchyId) =>
        {
            var items = await _store.ListStoredTextEntriesAsync(limit ?? 100, hierarchyId);
            return Results.Json(items);
        });

        app.MapGet("/api/stored-text/{id:int}", async Task<IResult> (int id) =>
        {
            var item = await _store.GetStoredTextEntryAsync(id);
            return item is null ? Results.NotFound() : Results.Json(item);
        });

        // Create from captured clipboard TextEntries row
        app.MapPost("/api/stored-text/from-clipboard", async Task<IResult> (CreateStoredFromClipboardRequest req) =>
        {
            try
            {
                var created = await _store.CreateStoredTextEntryFromClipboardAsync(req.TextEntryId, req.Name, req.HierarchyId);
                return Results.Json(created, statusCode: StatusCodes.Status201Created);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPut("/api/stored-text/{id:int}", async Task<IResult> (int id, UpdateStoredTextRequest req) =>
        {
            try
            {
                var ok = await _store.UpdateStoredTextEntryAsync(id, req.HierarchyId, req.Name, req.Content);
                return ok ? Results.NoContent() : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapDelete("/api/stored-text/{id:int}", async Task<IResult> (int id) =>
        {
            var ok = await _store.DeleteStoredTextEntryAsync(id);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        return app.RunAsync(cancellationToken);
    }

    private static ImageDto ToImageDto(ClipboardImageEntry entry) =>
        new(entry.Id, entry.CreatedAt, Convert.ToBase64String(entry.Data));

    private record ImageDto(int Id, DateTimeOffset CreatedAt, string Base64Data);

    private sealed record CreateHierarchyRequest(int? ParentId, string Name);
    private sealed record UpdateHierarchyRequest(int? ParentId, string Name);

    private sealed record CreateStoredFromClipboardRequest(int TextEntryId, string Name, int? HierarchyId);
    private sealed record UpdateStoredTextRequest(int? HierarchyId, string Name, string Content);
}
