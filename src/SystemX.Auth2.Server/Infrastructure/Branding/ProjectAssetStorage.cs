using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Branding;

/// <summary>
/// Manages static branding assets stored under wwwroot/projects/{projectname}/.
/// Guarantees that the three core assets (logo, logo-dark, favicon) are always replaced on disk when updated.
/// </summary>
public static class ProjectAssetStorage
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".svg", ".ico"
    };

    /// <summary>Normalizes asset kind to a canonical file prefix.</summary>
    public static string GetBaseName(string kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "logo" or "logo_light" or "logo-light" => "logo",
        "logo_dark" or "logo-dark" => "logo-dark",
        "favicon" => "favicon",
        "background" => "background",
        _ => kind?.Trim().ToLowerInvariant() ?? "asset"
    };

    /// <summary>Determines file extension based on MIME content type and filename.</summary>
    public static string DetectExtension(string contentType, string? fileName = null)
    {
        if (!string.IsNullOrEmpty(fileName))
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (AllowedExtensions.Contains(ext)) return ext;
        }

        return contentType?.ToLowerInvariant() switch
        {
            "image/svg+xml" => ".svg",
            "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
            "image/webp" => ".webp",
            "image/jpeg" => ".jpg",
            _ => ".png"
        };
    }

    /// <summary>Resolves the physical directory for a project under wwwroot.</summary>
    public static string GetProjectDirectory(string webRoot, string projectIdentifier)
    {
        return Path.Combine(webRoot, "projects", projectIdentifier);
    }

    /// <summary>
    /// Saves an asset to wwwroot/projects/{projectname}/, replacing any prior file for that asset.
    /// </summary>
    public static async Task<string> SaveAssetAsync(
        string webRoot,
        string projectKey,
        string? projectName,
        string kind,
        string contentType,
        byte[] data,
        string? originalFileName = null)
    {
        var baseName = GetBaseName(kind);
        var ext = DetectExtension(contentType, originalFileName);
        var fileName = $"{baseName}{ext}";

        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { projectKey };
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            identifiers.Add(projectName);
        }

        string primaryRelativePath = $"/projects/{projectKey}/{fileName}";

        foreach (var id in identifiers)
        {
            var dir = GetProjectDirectory(webRoot, id);
            Directory.CreateDirectory(dir);

            // Always replace existing asset file(s) for this kind (e.g. remove prior logo.png when uploading logo.svg)
            foreach (var existing in Directory.GetFiles(dir, $"{baseName}.*"))
            {
                try { File.Delete(existing); } catch { /* ignore concurrency */ }
            }

            var filePath = Path.Combine(dir, fileName);
            await File.WriteAllBytesAsync(filePath, data);
        }

        return primaryRelativePath;
    }

    /// <summary>
    /// Deletes an asset's file(s) from wwwroot/projects/{projectname}/.
    /// </summary>
    public static void DeleteAsset(string webRoot, string projectKey, string? projectName, string kind)
    {
        var baseName = GetBaseName(kind);
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { projectKey };
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            identifiers.Add(projectName);
        }

        foreach (var id in identifiers)
        {
            var dir = GetProjectDirectory(webRoot, id);
            if (!Directory.Exists(dir)) continue;

            foreach (var existing in Directory.GetFiles(dir, $"{baseName}.*"))
            {
                try { File.Delete(existing); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    /// Finds the existing asset URL from disk under wwwroot/projects/{projectKey}/ if present.
    /// </summary>
    public static string? FindAssetUrl(string webRoot, string projectKey, string kind)
    {
        if (string.IsNullOrWhiteSpace(webRoot) || string.IsNullOrWhiteSpace(projectKey)) return null;

        var baseName = GetBaseName(kind);
        var dir = GetProjectDirectory(webRoot, projectKey);
        if (!Directory.Exists(dir)) return null;

        var matching = Directory.GetFiles(dir, $"{baseName}.*");
        if (matching.Length == 0) return null;

        var fileName = Path.GetFileName(matching[0]);
        var lastWrite = File.GetLastWriteTimeUtc(matching[0]).Ticks;
        return $"/projects/{projectKey}/{fileName}?v={lastWrite}";
    }

    /// <summary>
    /// Re-hydrates any database-stored branding assets to disk under wwwroot/projects/{projectKey}/ if missing.
    /// </summary>
    public static async Task EnsureHydratedFromDbAsync(string webRoot, Auth2DbContext db, CancellationToken ct = default)
    {
        try
        {
            var projects = await db.Projects.AsNoTracking().ToListAsync(ct);
            var assets = await db.BrandingAssets.AsNoTracking().ToListAsync(ct);

            foreach (var project in projects)
            {
                var projectAssets = assets.Where(a => a.ProjectId == project.Id);
                foreach (var asset in projectAssets)
                {
                    if (asset.Data.Length == 0) continue;
                    var baseName = GetBaseName(asset.Kind);
                    var dir = GetProjectDirectory(webRoot, project.ProjectKey);

                    var exists = Directory.Exists(dir) && Directory.GetFiles(dir, $"{baseName}.*").Length > 0;
                    if (!exists)
                    {
                        await SaveAssetAsync(webRoot, project.ProjectKey, project.Name, asset.Kind, asset.ContentType, asset.Data);
                    }
                }
            }
        }
        catch
        {
            // Non-fatal if DB is not ready during early migrations
        }
    }
}
