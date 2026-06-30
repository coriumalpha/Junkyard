using Inventario.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Data;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Box> Boxes => Set<Box>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ItemCondition> ItemConditions => Set<ItemCondition>();
    public DbSet<ItemTag> ItemTags => Set<ItemTag>();
    public DbSet<InventoryAction> InventoryActions => Set<InventoryAction>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<PhotoInbox> PhotoInboxes => Set<PhotoInbox>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Location>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Box>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.ContainerType).HasMaxLength(24).HasDefaultValue(Box.DefaultContainerType).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.HasQueryFilter(x => x.ArchivedAt == null);
            entity.HasIndex(x => x.Code)
                .IsUnique()
                .HasFilter("\"ArchivedAt\" IS NULL");
            entity.HasOne(x => x.Location).WithMany(x => x.Boxes).HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ParentBox).WithMany(x => x.ChildBoxes).HasForeignKey(x => x.ParentBoxId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Item>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.MinQuantity).HasPrecision(18, 3);
            entity.HasQueryFilter(x => x.ArchivedAt == null);
            entity.HasOne(x => x.Box).WithMany(x => x.Items).HasForeignKey(x => x.BoxId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => x.Code)
                .IsUnique()
                .HasFilter("\"ArchivedAt\" IS NULL");
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => x.Category);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Color).HasMaxLength(16).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<ItemCondition>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Color).HasMaxLength(16).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<ItemTag>(entity =>
        {
            entity.HasKey(x => new { x.ItemId, x.TagId });
            entity.HasQueryFilter(x => x.Item.ArchivedAt == null);
            entity.HasOne(x => x.Item).WithMany(x => x.ItemTags).HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Tag).WithMany(x => x.ItemTags).HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.TagId);
        });

        modelBuilder.Entity<InventoryAction>(entity =>
        {
            entity.ToTable(tb => tb.HasCheckConstraint("CK_InventoryActions_Priority", "\"Priority\" BETWEEN 1 AND 5"));
            entity.Property(x => x.Title).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.LinkedEntityType).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Priority).IsRequired();
            entity.HasIndex(x => x.Kind);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => new { x.LinkedEntityType, x.LinkedEntityId });
            entity.HasIndex(x => new { x.Priority, x.CreatedAt });
        });

        modelBuilder.Entity<Photo>(entity =>
        {
            entity.Property(x => x.EntityType).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Filename).HasMaxLength(260).IsRequired();
            entity.Property(x => x.RotationDegrees).HasDefaultValue(0);
            entity.HasQueryFilter(x => x.Status == PhotoStatus.Active);
            entity.HasIndex(x => new { x.EntityType, x.EntityId });
            entity.HasIndex(x => x.SourceInboxId);
        });

        modelBuilder.Entity<PhotoInbox>(entity =>
        {
            entity.Property(x => x.Filename).HasMaxLength(260).IsRequired();
            entity.Property(x => x.OriginalFilename).HasMaxLength(260).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.RotationDegrees).HasDefaultValue(0);
            entity.HasIndex(x => x.Status);
            entity.HasOne(x => x.SourceBox).WithMany().HasForeignKey(x => x.SourceBoxId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    public override int SaveChanges()
    {
        StampUpdatedEntities();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampUpdatedEntities();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampUpdatedEntities()
    {
        AssignItemCodes();

        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.Metadata.FindProperty("UpdatedAt") is not null)
            {
                entry.Property("UpdatedAt").CurrentValue = now;
            }

            if (entry.State == EntityState.Added && entry.Metadata.FindProperty("CreatedAt") is not null)
            {
                entry.Property("CreatedAt").CurrentValue = now;
            }
        }
    }

    private void AssignItemCodes()
    {
        var itemEntries = ChangeTracker.Entries<Item>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .ToList();
        if (itemEntries.Count == 0)
        {
            return;
        }

        foreach (var entry in itemEntries.Where(entry => !string.IsNullOrWhiteSpace(entry.Entity.Code)))
        {
            entry.Entity.Code = Item.NormalizePublicCode(entry.Entity.Code);
        }

        var pending = itemEntries
            .Where(entry => entry.State == EntityState.Added && string.IsNullOrWhiteSpace(entry.Entity.Code))
            .Select(entry => entry.Entity)
            .ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var maxSequence = Items
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(item => item.Code)
            .AsEnumerable()
            .Select(code => Item.TryParseItSequence(code, out var sequence) ? sequence : 0)
            .DefaultIfEmpty(0)
            .Max();

        foreach (var item in pending)
        {
            item.Code = Item.FormatItCode(++maxSequence);
        }
    }
}
