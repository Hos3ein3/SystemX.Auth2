using SystemX.Auth2.Contracts.Constants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Server.Infrastructure.Branding;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Endpoints;

/// <summary>
/// Public endpoints to serve uploaded branding assets (logos, favicons, backgrounds) stored in PostgreSQL or wwwroot/projects/.
/// </summary>
public static class BrandingEndpoints
{
    private static string[] ResolveKinds(string kind) => kind.ToLowerInvariant() switch
    {
        "logo" or "logo_light" or "logo-light" => ["logo", "logo_light", "logo-light"],
        "logo_dark" or "logo-dark" => ["logo-dark", "logo_dark"],
        _ => [kind]
    };

    /// <summary>Maps branding asset endpoints.</summary>
    public static IEndpointRouteBuilder MapAuth2Branding(this IEndpointRouteBuilder routes)
    {
        // 1. By project ID and asset kind (public)
        routes.MapGet(Auth2Routes.BrandingAssetById, async (Guid projectId, string kind, Auth2DbContext db, IWebHostEnvironment env, CancellationToken ct) =>
        {
            var kinds = ResolveKinds(kind);
            var asset = await db.BrandingAssets.AsNoTracking().FirstOrDefaultAsync(a => a.ProjectId == projectId && kinds.Contains(a.Kind), ct);
            if (asset is null || asset.Data.Length == 0) return Results.NotFound();
            return Results.File(asset.Data, string.IsNullOrEmpty(asset.ContentType) ? "image/png" : asset.ContentType, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{asset.Sha256}\""));
        });

        // 2. By project slug/key and asset kind (public legacy/fallback endpoint)
        routes.MapGet(Auth2Routes.BrandingAssetByKey, async (string projectKey, string kind, Auth2DbContext db, IWebHostEnvironment env, CancellationToken ct) =>
        {
            var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectKey == projectKey, ct);
            if (project is null) return Results.NotFound();
            var kinds = ResolveKinds(kind);
            var asset = await db.BrandingAssets.AsNoTracking().FirstOrDefaultAsync(a => a.ProjectId == project.Id && kinds.Contains(a.Kind), ct);
            if (asset is null || asset.Data.Length == 0) return Results.NotFound();

            // Cache to disk under wwwroot/projects/{projectname}/
            var webRoot = env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            await ProjectAssetStorage.SaveAssetAsync(webRoot, project.ProjectKey, project.Name, asset.Kind, asset.ContentType, asset.Data);

            return Results.File(asset.Data, string.IsNullOrEmpty(asset.ContentType) ? "image/png" : asset.ContentType, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{asset.Sha256}\""));
        });

        // 3. Fallback for /projects/{projectKey}/{kind} without file extension
        routes.MapGet(Auth2Routes.ProjectBrandingAsset, async (string projectKey, string kind, Auth2DbContext db, IWebHostEnvironment env, CancellationToken ct) =>
        {
            var webRoot = env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var diskUrl = ProjectAssetStorage.FindAssetUrl(webRoot, projectKey, kind);
            if (!string.IsNullOrEmpty(diskUrl))
            {
                var cleanPath = diskUrl.Split('?')[0].TrimStart('/');
                var physicalPath = Path.Combine(webRoot, cleanPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(physicalPath))
                {
                    var ext = Path.GetExtension(physicalPath).ToLowerInvariant();
                    var contentType = ext switch
                    {
                        ".svg" => "image/svg+xml",
                        ".ico" => "image/x-icon",
                        ".webp" => "image/webp",
                        ".jpg" or ".jpeg" => "image/jpeg",
                        _ => "image/png"
                    };
                    return Results.File(physicalPath, contentType);
                }
            }

            var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectKey == projectKey, ct);
            if (project is null) return Results.NotFound();
            var kinds = ResolveKinds(kind);
            var asset = await db.BrandingAssets.AsNoTracking().FirstOrDefaultAsync(a => a.ProjectId == project.Id && kinds.Contains(a.Kind), ct);
            if (asset is null || asset.Data.Length == 0) return Results.NotFound();

            await ProjectAssetStorage.SaveAssetAsync(webRoot, project.ProjectKey, project.Name, asset.Kind, asset.ContentType, asset.Data);
            return Results.File(asset.Data, string.IsNullOrEmpty(asset.ContentType) ? "image/png" : asset.ContentType, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{asset.Sha256}\""));
        });

        return routes;
    }
}
