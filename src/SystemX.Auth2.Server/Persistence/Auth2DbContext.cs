using System.Linq.Expressions;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Persistence;

/// <summary>
/// The database. Tenant tables have a global query filter by the current application.
/// Only <see cref="IGlobalScope"/> bypasses it. Table and column names use snake_case.
/// </summary>
public class Auth2DbContext(DbContextOptions<Auth2DbContext> options, IProjectScope scope)
    : IdentityDbContext<AppUser, AppRole, Guid, IdentityUserClaim<Guid>, IdentityUserRole<Guid>, AppUserLogin, IdentityRoleClaim<Guid>, AppUserToken, AppUserPasskey>(options),
      IDataProtectionKeyContext
{
    // Read by the query filters. EF turns them into per-context parameters.
    private Guid? CurrentProjectId => scope.ProjectId;
    private bool FiltersBypassed => scope.IsGlobalActive;

    /// <summary>Projects.</summary>
    public DbSet<Project> Projects => Set<Project>();
    /// <summary>User membership and per-application credentials.</summary>
    public DbSet<UserProject> UserProjects => Set<UserProject>();
    /// <summary>Recovery codes.</summary>
    public DbSet<UserProjectRecoveryCode> RecoveryCodes => Set<UserProjectRecoveryCode>();
    /// <summary>Signing and encryption keys.</summary>
    public DbSet<KeyRingEntry> KeyRing => Set<KeyRingEntry>();
    /// <summary>Permissions.</summary>
    public DbSet<Permission> Permissions => Set<Permission>();
    /// <summary>Direct user permissions.</summary>
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    /// <summary>Role permissions.</summary>
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    /// <summary>Groups.</summary>
    public DbSet<Group> Groups => Set<Group>();
    /// <summary>Group members.</summary>
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    /// <summary>Group roles.</summary>
    public DbSet<GroupRole> GroupRoles => Set<GroupRole>();
    /// <summary>Group permissions.</summary>
    public DbSet<GroupPermission> GroupPermissions => Set<GroupPermission>();
    /// <summary>Audit events.</summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    /// <summary>OTP codes.</summary>
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    /// <summary>Rules that turn external groups, claims and attributes into roles, groups and permissions.</summary>
    public DbSet<ExternalGroupMapping> ExternalGroupMappings => Set<ExternalGroupMapping>();
    /// <summary>Server-wide values changed at run time.</summary>
    public DbSet<ServerValue> ServerValues => Set<ServerValue>();
    /// <summary>One-time values (SAML ids, login nonces).</summary>
    public DbSet<ReplayEntry> ReplayEntries => Set<ReplayEntry>();
    /// <summary>Data Protection keys (scale-out).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    /// <summary>Maintenance job runs.</summary>
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    /// <summary>Branding assets.</summary>
    public DbSet<BrandingAsset> BrandingAssets => Set<BrandingAsset>();
    /// <summary>Email templates.</summary>
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType("timestamptz");
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ---- Identity tables: clean names
        b.Entity<AppUser>(e =>
        {
            e.ToTable("users");
            e.Property(u => u.Profile).HasColumnType("jsonb");
            e.Property(u => u.FirstName).HasMaxLength(100);
            e.Property(u => u.LastName).HasMaxLength(100);
            e.Property(u => u.DisplayName).HasMaxLength(200);
            e.Property(u => u.PreferredCulture).HasMaxLength(16);
            // Email is unique across the whole server (one person = one AppUser). Null emails are allowed (phone-only users).
            var oldEmailIndex = e.Metadata.GetIndexes().Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(AppUser.NormalizedEmail));
            e.Metadata.RemoveIndex(oldEmailIndex);
            e.HasIndex(u => u.NormalizedEmail, "EmailIndex").IsUnique();
        });
        b.Entity<AppRole>(e =>
        {
            e.ToTable("roles");
            // Replace the default unique index on NormalizedName: role names are unique per application.
            var old = e.Metadata.GetIndexes().Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(AppRole.NormalizedName));
            e.Metadata.RemoveIndex(old);
            e.HasIndex(r => new { r.ProjectId, r.NormalizedName }, "ix_roles_project_id_normalized_name")
                .IsUnique().AreNullsDistinct(false);
            e.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(r => FiltersBypassed || r.ProjectId == null || r.ProjectId == CurrentProjectId);
        });
        b.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        b.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        b.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        b.Entity<AppUserLogin>(e =>
        {
            e.ToTable("user_logins");
            e.HasKey(x => new { x.ProjectId, x.LoginProvider, x.ProviderKey });
            e.HasIndex(x => new { x.ProjectId, x.UserId });
            e.Property(x => x.LoginProvider).HasMaxLength(64);
            e.Property(x => x.ProviderKey).HasMaxLength(512);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => FiltersBypassed || x.ProjectId == CurrentProjectId);
        });
        b.Entity<AppUserPasskey>(e =>
        {
            e.ToTable("user_passkeys");
            e.HasKey(x => new { x.ProjectId, x.CredentialId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.CredentialId).HasMaxLength(1024);
            e.OwnsOne(x => x.Data).ToJson("data");
            e.HasIndex(x => new { x.ProjectId, x.UserId });
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => FiltersBypassed || x.ProjectId == CurrentProjectId);
        });
        b.Entity<ExternalGroupMapping>(e =>
        {
            e.ToTable("external_group_mappings");
            e.Property(x => x.ProviderName).HasMaxLength(64);
            e.Property(x => x.SourceName).HasMaxLength(200).IsRequired();
            e.Property(x => x.SourceValue).HasMaxLength(500).IsRequired();
            e.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.MatchType).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.TargetType).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => new { x.ProjectId, x.ProviderName });
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => FiltersBypassed || x.ProjectId == CurrentProjectId);
        });
        b.Entity<ServerValue>(e =>
        {
            e.ToTable("server_values");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
        });
        b.Entity<ReplayEntry>(e =>
        {
            e.ToTable("replay_entries");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(400);
            e.HasIndex(x => x.ExpiresAtUtc);
        });
        b.Entity<AppUserToken>().ToTable("user_tokens");

        // ---- Projects (tenants) and applications (their OpenIddict clients)
        b.Entity<Project>(e =>
        {
            e.ToTable("projects");
            e.HasKey(a => a.Id);
            e.Property(a => a.ProjectKey).HasMaxLength(64).IsRequired();
            e.Property(a => a.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(a => a.ProjectKey).IsUnique();
            e.Property(a => a.Version).IsRowVersion();
            e.Property(a => a.Settings).HasColumnName("settings_json").HasColumnType("jsonb")
                .HasConversion(SettingsConverter<Domain.Settings.ProjectSettings>(), SettingsComparer<Domain.Settings.ProjectSettings>());
        });

        // The application IS the OpenIddict application (one table). OpenIddict's own columns stay; these are the extra ones.
        b.Entity<Infrastructure.OpenIddict.Auth2Application>(e =>
        {
            e.ToTable("applications");
            e.Property(a => a.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(a => new { a.ProjectId, a.Name }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.Property(a => a.ClientSettings).HasColumnName("settings_json").HasColumnType("jsonb")
                .HasConversion(SettingsConverter<Domain.Settings.ClientApplicationSettings>(), SettingsComparer<Domain.Settings.ClientApplicationSettings>());
        });

        b.Entity<UserProject>(e =>
        {
            e.ToTable("user_projects");
            e.HasKey(x => new { x.UserId, x.ProjectId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.HasQueryFilter(x => FiltersBypassed || x.ProjectId == CurrentProjectId);
        });
        b.Entity<UserProjectRecoveryCode>(e =>
        {
            e.ToTable("user_project_recovery_codes");
            e.HasIndex(x => new { x.UserId, x.ProjectId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => FiltersBypassed || x.ProjectId == CurrentProjectId);
        });

        // ---- Access model
        b.Entity<Permission>(e =>
        {
            e.ToTable("permissions");
            e.Property(p => p.Key).HasMaxLength(100).IsRequired();
            e.Property(p => p.Source).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(p => new { p.ProjectId, p.Key }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(p => p.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(p => FiltersBypassed || p.ProjectId == CurrentProjectId);
        });
        b.Entity<UserPermission>(e =>
        {
            e.ToTable("user_permissions");
            e.HasKey(x => new { x.UserId, x.PermissionId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => FiltersBypassed || x.ProjectId == CurrentProjectId);
        });
        b.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions");
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne<AppRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Group>(e =>
        {
            e.ToTable("groups");
            e.Property(g => g.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(g => new { g.ProjectId, g.Name }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(g => g.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(g => FiltersBypassed || g.ProjectId == CurrentProjectId);
        });
        b.Entity<UserGroup>(e =>
        {
            e.ToTable("user_groups");
            e.HasKey(x => new { x.UserId, x.GroupId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<GroupRole>(e =>
        {
            e.ToTable("group_roles");
            e.HasKey(x => new { x.GroupId, x.RoleId });
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<GroupPermission>(e =>
        {
            e.ToTable("group_permissions");
            e.HasKey(x => new { x.GroupId, x.PermissionId });
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---- Operations
        b.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_events");
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.DataJson).HasColumnType("jsonb");
            e.HasIndex(x => x.OccurredAtUtc);
            e.HasIndex(x => new { x.ProjectId, x.OccurredAtUtc });
            e.HasQueryFilter(x => FiltersBypassed || x.ProjectId == CurrentProjectId);
        });
        b.Entity<OtpCode>(e =>
        {
            e.ToTable("otp_codes");
            e.Property(x => x.Purpose).HasMaxLength(50).IsRequired();
            e.HasIndex(x => new { x.UserId, x.ProjectId, x.Purpose });
            e.HasIndex(x => x.ExpiresAtUtc);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => FiltersBypassed || x.ProjectId == CurrentProjectId);
        });
        b.Entity<KeyRingEntry>(e =>
        {
            e.ToTable("key_ring");
            e.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Thumbprint).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.Purpose, x.RetiredAtUtc });
        });
        b.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        b.Entity<JobRun>(e =>
        {
            e.ToTable("job_runs");
            e.Property(x => x.JobName).HasMaxLength(100).IsRequired();
            e.Property(x => x.InstanceId).HasMaxLength(100);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.HasIndex(x => x.StartedAtUtc);
            e.HasIndex(x => new { x.JobName, x.StartedAtUtc });
        });
        b.Entity<BrandingAsset>(e =>
        {
            e.ToTable("branding_assets");
            e.Property(x => x.Kind).HasMaxLength(64).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.ProjectId, x.Kind }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => FiltersBypassed || x.ProjectId == CurrentProjectId);
        });
        b.Entity<EmailTemplate>(e =>
        {
            e.ToTable("email_templates");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Culture).HasMaxLength(16).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(256).IsRequired();
            e.HasIndex(x => new { x.ProjectId, x.Name, x.Culture }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => FiltersBypassed || x.ProjectId == CurrentProjectId);
        });

        ApplySnakeCase(b);
    }

    private static ValueConverter<T, string> SettingsConverter<T>() where T : class, new() =>
        new(v => SettingsJson.Serialize(v), v => SettingsJson.Deserialize<T>(v));

    private static ValueComparer<T> SettingsComparer<T>() where T : class, new() =>
        new((x, y) => SettingsJson.Serialize(x!) == SettingsJson.Serialize(y!), v => SettingsJson.Serialize(v).GetHashCode(), v => SettingsJson.Clone(v));

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Join tables point at filtered tables on purpose; the filters are tenant filters, not soft delete.
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
    }

    private static void ApplySnakeCase(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        {
            if (entity.IsMappedToJson()) continue; // owned JSON data keeps its own names
            var table = entity.GetTableName();
            if (table is null) continue;
            var schema = entity.GetSchema();
            var tableName = Snake(table).Replace("open_iddict_", "openiddict_", StringComparison.Ordinal);
            entity.SetTableName(tableName);

            var id = StoreObjectIdentifier.Table(tableName, schema);
            foreach (var p in entity.GetProperties())
                p.SetColumnName(Snake(p.GetColumnName(StoreObjectIdentifier.Table(table, schema)) ?? p.Name));
            foreach (var k in entity.GetKeys()) if (k.GetName() is { Length: > 0 } kn) k.SetName(Snake(kn));
            foreach (var fk in entity.GetForeignKeys()) fk.SetConstraintName(Snake(fk.GetConstraintName(id, StoreObjectIdentifier.Table(fk.PrincipalEntityType.GetTableName()!, fk.PrincipalEntityType.GetSchema())) ?? ""));
            foreach (var ix in entity.GetIndexes()) ix.SetDatabaseName(Snake(ix.GetDatabaseName(id) ?? ""));
        }
    }

    /// <summary>Converts <c>PascalCase</c> / <c>camelCase</c> to <c>snake_case</c>.</summary>
    internal static string Snake(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var prevLowerOrDigit = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                var nextLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (i > 0 && name[i - 1] != '_' && (prevLowerOrDigit || (char.IsUpper(name[i - 1]) && nextLower))) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
