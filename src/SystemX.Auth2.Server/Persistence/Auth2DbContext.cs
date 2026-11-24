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
public class Auth2DbContext(DbContextOptions<Auth2DbContext> options, IApplicationScope scope)
    : IdentityDbContext<AppUser, AppRole, Guid, IdentityUserClaim<Guid>, IdentityUserRole<Guid>, AppUserLogin, IdentityRoleClaim<Guid>, AppUserToken, AppUserPasskey>(options),
      IDataProtectionKeyContext
{
    // Read by the query filters. EF turns them into per-context parameters.
    private Guid? CurrentApplicationId => scope.ApplicationId;
    private bool FiltersBypassed => scope.IsGlobalActive;

    /// <summary>Applications.</summary>
    public DbSet<Auth2Application> Applications => Set<Auth2Application>();
    /// <summary>User membership and per-application credentials.</summary>
    public DbSet<UserApplication> UserApplications => Set<UserApplication>();
    /// <summary>Recovery codes.</summary>
    public DbSet<UserApplicationRecoveryCode> RecoveryCodes => Set<UserApplicationRecoveryCode>();
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
            e.HasIndex(r => new { r.ApplicationId, r.NormalizedName }, "ix_roles_application_id_normalized_name")
                .IsUnique().AreNullsDistinct(false);
            e.HasOne<Auth2Application>().WithMany().HasForeignKey(r => r.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(r => FiltersBypassed || r.ApplicationId == null || r.ApplicationId == CurrentApplicationId);
        });
        b.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        b.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        b.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        b.Entity<AppUserLogin>(e =>
        {
            e.ToTable("user_logins");
            e.HasKey(x => new { x.ApplicationId, x.LoginProvider, x.ProviderKey });
            e.HasIndex(x => new { x.ApplicationId, x.UserId });
            e.Property(x => x.LoginProvider).HasMaxLength(64);
            e.Property(x => x.ProviderKey).HasMaxLength(512);
            e.HasOne<Auth2Application>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => FiltersBypassed || x.ApplicationId == CurrentApplicationId);
        });
        b.Entity<AppUserPasskey>(e =>
        {
            e.ToTable("user_passkeys");
            e.HasKey(x => new { x.ApplicationId, x.CredentialId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.CredentialId).HasMaxLength(1024);
            e.OwnsOne(x => x.Data).ToJson("data");
            e.HasIndex(x => new { x.ApplicationId, x.UserId });
            e.HasOne<Auth2Application>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => FiltersBypassed || x.ApplicationId == CurrentApplicationId);
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
            e.HasIndex(x => new { x.ApplicationId, x.ProviderName });
            e.HasOne<Auth2Application>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => FiltersBypassed || x.ApplicationId == CurrentApplicationId);
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

        // ---- Applications
        b.Entity<Auth2Application>(e =>
        {
            e.ToTable("applications");
            e.HasKey(a => a.Id);
            e.Property(a => a.ProjectKey).HasMaxLength(64).IsRequired();
            e.Property(a => a.Name).HasMaxLength(200).IsRequired();
            e.Property(a => a.ClientId).HasMaxLength(200).IsRequired();
            e.HasIndex(a => a.ProjectKey).IsUnique();
            e.HasIndex(a => a.ClientId).IsUnique();
            var converter = new ValueConverter<Domain.Settings.ApplicationSettings, string>(
                v => SettingsJson.Serialize(v), v => SettingsJson.Deserialize(v));
            var comparer = new ValueComparer<Domain.Settings.ApplicationSettings>(
                (x, y) => SettingsJson.Serialize(x!) == SettingsJson.Serialize(y!),
                v => SettingsJson.Serialize(v).GetHashCode(),
                v => SettingsJson.Clone(v));
            e.Property(a => a.Settings).HasColumnName("settings_json").HasColumnType("jsonb")
                .HasConversion(converter, comparer);
        });

        b.Entity<UserApplication>(e =>
        {
            e.ToTable("user_applications");
            e.HasKey(x => new { x.UserId, x.ApplicationId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Auth2Application>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.HasQueryFilter(x => FiltersBypassed || x.ApplicationId == CurrentApplicationId);
        });
        b.Entity<UserApplicationRecoveryCode>(e =>
        {
            e.ToTable("user_application_recovery_codes");
            e.HasIndex(x => new { x.UserId, x.ApplicationId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Auth2Application>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => FiltersBypassed || x.ApplicationId == CurrentApplicationId);
        });

        // ---- Access model
        b.Entity<Permission>(e =>
        {
            e.ToTable("permissions");
            e.Property(p => p.Key).HasMaxLength(100).IsRequired();
            e.Property(p => p.Source).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(p => new { p.ApplicationId, p.Key }).IsUnique();
            e.HasOne<Auth2Application>().WithMany().HasForeignKey(p => p.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(p => FiltersBypassed || p.ApplicationId == CurrentApplicationId);
        });
        b.Entity<UserPermission>(e =>
        {
            e.ToTable("user_permissions");
            e.HasKey(x => new { x.UserId, x.PermissionId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => FiltersBypassed || x.ApplicationId == CurrentApplicationId);
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
            e.HasIndex(g => new { g.ApplicationId, g.Name }).IsUnique();
            e.HasOne<Auth2Application>().WithMany().HasForeignKey(g => g.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(g => FiltersBypassed || g.ApplicationId == CurrentApplicationId);
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
            e.HasIndex(x => new { x.ApplicationId, x.OccurredAtUtc });
            e.HasQueryFilter(x => FiltersBypassed || x.ApplicationId == CurrentApplicationId);
        });
        b.Entity<OtpCode>(e =>
        {
            e.ToTable("otp_codes");
            e.Property(x => x.Purpose).HasMaxLength(50).IsRequired();
            e.HasIndex(x => new { x.UserId, x.ApplicationId, x.Purpose });
            e.HasIndex(x => x.ExpiresAtUtc);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => FiltersBypassed || x.ApplicationId == CurrentApplicationId);
        });
        b.Entity<KeyRingEntry>(e =>
        {
            e.ToTable("key_ring");
            e.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Thumbprint).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.Purpose, x.RetiredAtUtc });
        });
        b.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        ApplySnakeCase(b);
    }

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
