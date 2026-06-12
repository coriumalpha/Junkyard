using Microsoft.EntityFrameworkCore;

namespace Inventario.Data;

#pragma warning disable EF1002
public static class SchemaUpgrader
{
    public static void Apply(InventoryDbContext db)
    {
        AddColumn(db, "Boxes", "ArchivedAt", "TEXT NULL");
        AddColumn(db, "Boxes", "ParentBoxId", "INTEGER NULL");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Boxes_ParentBoxId" ON "Boxes" ("ParentBoxId");""");
        AddColumn(db, "Photos", "Status", "TEXT NOT NULL DEFAULT 'Active'");
        AddColumn(db, "Photos", "RotationDegrees", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(db, "Photos", "ArchivedAt", "TEXT NULL");
        AddColumn(db, "Photos", "UpdatedAt", "TEXT NOT NULL DEFAULT '1970-01-01T00:00:00Z'");
        AddColumn(db, "Photos", "SourceInboxId", "INTEGER NULL");
        AddColumn(db, "Items", "ArchivedAt", "TEXT NULL");
        EnsureNullableItemBoxId(db);
        EnsurePhotoInbox(db);
        AddColumn(db, "PhotoInboxes", "RotationDegrees", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(db, "PhotoInboxes", "UpdatedAt", "TEXT NOT NULL DEFAULT '1970-01-01T00:00:00Z'");
        AddColumn(db, "PhotoInboxes", "ProcessedAt", "TEXT NULL");
        BackfillTimestamps(db);
    }

    private static void AddColumn(InventoryDbContext db, string table, string column, string definition)
    {
        var exists = db.Database.SqlQueryRaw<int>(
            $"SELECT COUNT(*) AS Value FROM pragma_table_info('{table}') WHERE name = '{column}'").AsEnumerable().First();
        if (exists == 0)
        {
            db.Database.ExecuteSqlRaw($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
        }
    }

    private static void EnsurePhotoInbox(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "PhotoInboxes" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_PhotoInboxes" PRIMARY KEY AUTOINCREMENT,
                "Filename" TEXT NOT NULL,
                "OriginalFilename" TEXT NOT NULL,
                "ImportedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                "ProcessedAt" TEXT NULL,
                "Status" TEXT NOT NULL,
                "RotationDegrees" INTEGER NOT NULL DEFAULT 0,
                "SourceBoxId" INTEGER NULL,
                "Notes" TEXT NULL,
                CONSTRAINT "FK_PhotoInboxes_Boxes_SourceBoxId" FOREIGN KEY ("SourceBoxId") REFERENCES "Boxes" ("Id") ON DELETE SET NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PhotoInboxes_Status" ON "PhotoInboxes" ("Status");""");
    }

    private static void EnsureNullableItemBoxId(InventoryDbContext db)
    {
        var notNull = db.Database.SqlQueryRaw<int>(
            "SELECT [notnull] AS Value FROM pragma_table_info('Items') WHERE name = 'BoxId'").AsEnumerable().FirstOrDefault();
        if (notNull == 0)
        {
            return;
        }

        db.Database.ExecuteSqlRaw("""
            PRAGMA foreign_keys=OFF;
            CREATE TABLE "Items_new" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Items" PRIMARY KEY AUTOINCREMENT,
                "BoxId" INTEGER NULL,
                "Name" TEXT NOT NULL,
                "Category" TEXT NOT NULL,
                "Quantity" TEXT NOT NULL,
                "Condition" TEXT NULL,
                "Retention" TEXT NULL,
                "Sentimental" INTEGER NOT NULL,
                "Obsolete" INTEGER NOT NULL,
                "Consumable" INTEGER NOT NULL,
                "MinQuantity" TEXT NULL,
                "Unit" TEXT NULL,
                "Notes" TEXT NULL,
                "CoverPhoto" TEXT NULL,
                "ArchivedAt" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_Items_Boxes_BoxId" FOREIGN KEY ("BoxId") REFERENCES "Boxes" ("Id") ON DELETE SET NULL
            );
            INSERT INTO "Items_new" ("Id","BoxId","Name","Category","Quantity","Condition","Retention","Sentimental","Obsolete","Consumable","MinQuantity","Unit","Notes","CoverPhoto","ArchivedAt","CreatedAt","UpdatedAt")
                SELECT "Id","BoxId","Name","Category","Quantity","Condition","Retention","Sentimental","Obsolete","Consumable","MinQuantity","Unit","Notes","CoverPhoto","ArchivedAt","CreatedAt","UpdatedAt" FROM "Items";
            DROP TABLE "Items";
            ALTER TABLE "Items_new" RENAME TO "Items";
            CREATE INDEX IF NOT EXISTS "IX_Items_BoxId" ON "Items" ("BoxId");
            CREATE INDEX IF NOT EXISTS "IX_Items_Category" ON "Items" ("Category");
            CREATE INDEX IF NOT EXISTS "IX_Items_Name" ON "Items" ("Name");
            PRAGMA foreign_keys=ON;
            """);
    }

    private static void BackfillTimestamps(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            UPDATE "Photos"
            SET "UpdatedAt" = COALESCE("CreatedAt", CURRENT_TIMESTAMP)
            WHERE "UpdatedAt" = '1970-01-01T00:00:00Z';
            """);
        db.Database.ExecuteSqlRaw("""
            UPDATE "PhotoInboxes"
            SET "UpdatedAt" = COALESCE("ImportedAt", CURRENT_TIMESTAMP)
            WHERE "UpdatedAt" = '1970-01-01T00:00:00Z';
            """);
        db.Database.ExecuteSqlRaw("""
            UPDATE "PhotoInboxes"
            SET "ProcessedAt" = COALESCE("ProcessedAt", "UpdatedAt")
            WHERE "Status" <> 'Pending' AND "ProcessedAt" IS NULL;
            """);
        db.Database.ExecuteSqlRaw("""
            UPDATE "Photos"
            SET "SourceInboxId" = (
                SELECT "PhotoInboxes"."Id"
                FROM "PhotoInboxes"
                WHERE "PhotoInboxes"."Filename" = "Photos"."Filename"
                LIMIT 1
            )
            WHERE "SourceInboxId" IS NULL
              AND EXISTS (
                  SELECT 1
                  FROM "PhotoInboxes"
                  WHERE "PhotoInboxes"."Filename" = "Photos"."Filename"
              );
            """);
    }
}
#pragma warning restore EF1002
