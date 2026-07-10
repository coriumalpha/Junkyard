using Microsoft.EntityFrameworkCore;

namespace Inventario.Data;

#pragma warning disable EF1002
public static class SchemaUpgrader
{
    public static void Apply(InventoryDbContext db)
    {
        AddColumn(db, "Boxes", "ContainerType", $"TEXT NOT NULL DEFAULT '{Models.Box.DefaultContainerType}'");
        AddColumn(db, "Boxes", "ArchivedAt", "TEXT NULL");
        AddColumn(db, "Boxes", "ParentBoxId", "INTEGER NULL");
        db.Database.ExecuteSqlRaw("""DROP INDEX IF EXISTS "IX_Boxes_Code";""");
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Boxes_Code_Active" ON "Boxes" ("Code") WHERE "ArchivedAt" IS NULL;""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Boxes_ParentBoxId" ON "Boxes" ("ParentBoxId");""");
        AddColumn(db, "Photos", "Status", "TEXT NOT NULL DEFAULT 'Active'");
        AddColumn(db, "Photos", "RotationDegrees", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(db, "Photos", "ArchivedAt", "TEXT NULL");
        AddColumn(db, "Photos", "UpdatedAt", "TEXT NOT NULL DEFAULT '1970-01-01T00:00:00Z'");
        AddColumn(db, "Photos", "SourceInboxId", "INTEGER NULL");
        AddColumn(db, "Items", "ArchivedAt", "TEXT NULL");
        AddColumn(db, "Items", "Code", "TEXT NULL");
        EnsureNullableItemBoxId(db);
        EnsureTags(db);
        EnsureItemConditions(db);
        EnsureItemClassifications(db);
        EnsureInventoryActions(db);
        AddColumn(db, "Items", "ItemClassId", "INTEGER NULL");
        AddColumn(db, "Items", "ItemSubtypeId", "INTEGER NULL");
        AddColumn(db, "Items", "IsQuarantined", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(db, "Items", "NeedsReview", "INTEGER NOT NULL DEFAULT 0");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Items_ItemClassId" ON "Items" ("ItemClassId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Items_ItemSubtypeId" ON "Items" ("ItemSubtypeId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Items_IsQuarantined" ON "Items" ("IsQuarantined");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Items_NeedsReview" ON "Items" ("NeedsReview");""");
        AddColumn(db, "InventoryActions", "Kind", "TEXT NOT NULL DEFAULT 'Task'");
        EnsurePhotoInbox(db);
        AddColumn(db, "PhotoInboxes", "RotationDegrees", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(db, "PhotoInboxes", "UpdatedAt", "TEXT NOT NULL DEFAULT '1970-01-01T00:00:00Z'");
        AddColumn(db, "PhotoInboxes", "ProcessedAt", "TEXT NULL");
        EnsureAiItemSuggestions(db);
        AddColumn(db, "AiItemSuggestions", "Provider", "TEXT NOT NULL DEFAULT 'OpenAI'");
        AddColumn(db, "AiItemSuggestions", "UserHint", "TEXT NULL");
        AddColumn(db, "AiItemSuggestions", "AnalysisMode", "TEXT NOT NULL DEFAULT 'fast'");
        AddColumn(db, "AiItemSuggestions", "ImageVariant", "TEXT NOT NULL DEFAULT 'preview'");
        AddColumn(db, "AiItemSuggestions", "InputTokens", "INTEGER NULL");
        AddColumn(db, "AiItemSuggestions", "OutputTokens", "INTEGER NULL");
        EnsureAiSettings(db);
        BackfillItemCodes(db);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Items_Code_Active" ON "Items" ("Code") WHERE "ArchivedAt" IS NULL AND "Code" IS NOT NULL AND trim("Code") <> '';""");
        NormalizeContainerTypes(db);
        BackfillCategoryTags(db);
        BackfillQuarantineFlag(db);
        CleanupItemClassificationTestData(db);
        BackfillItemConditions(db);
        BackfillTimestamps(db);
        BackfillBoxCoverPhotos(db);
        NormalizeChildBoxLocations(db);
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

    private static void EnsureInventoryActions(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "InventoryActions" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_InventoryActions" PRIMARY KEY AUTOINCREMENT,
                "Title" TEXT NOT NULL,
                "Description" TEXT NULL,
                "Kind" TEXT NOT NULL DEFAULT 'Task',
                "Status" TEXT NOT NULL,
                "Priority" INTEGER NOT NULL DEFAULT 3,
                "LinkedEntityType" TEXT NOT NULL DEFAULT 'None',
                "LinkedEntityId" INTEGER NULL,
                "CreatedAt" TEXT NOT NULL,
                "CompletedAt" TEXT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "CK_InventoryActions_Priority" CHECK ("Priority" BETWEEN 1 AND 5)
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_InventoryActions_Status" ON "InventoryActions" ("Status");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_InventoryActions_Kind" ON "InventoryActions" ("Kind");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_InventoryActions_LinkedEntityType_LinkedEntityId" ON "InventoryActions" ("LinkedEntityType", "LinkedEntityId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_InventoryActions_Priority_CreatedAt" ON "InventoryActions" ("Priority", "CreatedAt");""");
    }

    private static void EnsureAiItemSuggestions(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "AiItemSuggestions" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AiItemSuggestions" PRIMARY KEY AUTOINCREMENT,
                "CreatedAt" TEXT NOT NULL,
                "PhotoIdsJson" TEXT NOT NULL,
                "UserHint" TEXT NULL,
                "Provider" TEXT NOT NULL DEFAULT 'OpenAI',
                "Model" TEXT NOT NULL,
                "AnalysisMode" TEXT NOT NULL DEFAULT 'fast',
                "ImageDetail" TEXT NOT NULL,
                "ImageVariant" TEXT NOT NULL DEFAULT 'preview',
                "PromptVersion" TEXT NOT NULL,
                "InputTokens" INTEGER NULL,
                "OutputTokens" INTEGER NULL,
                "RawResponseJson" TEXT NULL,
                "ParsedResponseJson" TEXT NULL,
                "AcceptedAt" TEXT NULL,
                "RejectedAt" TEXT NULL,
                "Error" TEXT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_AiItemSuggestions_CreatedAt" ON "AiItemSuggestions" ("CreatedAt");""");
    }

    private static void EnsureAiSettings(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "AiSettings" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AiSettings" PRIMARY KEY AUTOINCREMENT,
                "Enabled" INTEGER NOT NULL DEFAULT 0,
                "Provider" TEXT NOT NULL DEFAULT 'OpenAI',
                "Model" TEXT NOT NULL DEFAULT 'gpt-5.4-mini',
                "CheapModel" TEXT NOT NULL DEFAULT 'gpt-5.4-nano',
                "ImageDetail" TEXT NOT NULL DEFAULT 'low',
                "MaxImagesPerRequest" INTEGER NOT NULL DEFAULT 4,
                "DefaultMode" TEXT NOT NULL DEFAULT 'normal',
                "MaxDescriptionLength" INTEGER NOT NULL DEFAULT 1200,
                "StoreRawResponse" INTEGER NOT NULL DEFAULT 1,
                "AllowSuggestedNewTags" INTEGER NOT NULL DEFAULT 1,
                "EncryptedApiKey" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """);
    }

    private static void EnsureTags(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Tags" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Tags" PRIMARY KEY AUTOINCREMENT,
                "Name" TEXT NOT NULL,
                "Color" TEXT NOT NULL DEFAULT '#48ffb0',
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "ItemTags" (
                "ItemId" INTEGER NOT NULL,
                "TagId" INTEGER NOT NULL,
                CONSTRAINT "PK_ItemTags" PRIMARY KEY ("ItemId", "TagId"),
                CONSTRAINT "FK_ItemTags_Items_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Items" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ItemTags_Tags_TagId" FOREIGN KEY ("TagId") REFERENCES "Tags" ("Id") ON DELETE CASCADE
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Tags_Name" ON "Tags" ("Name");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_ItemTags_TagId" ON "ItemTags" ("TagId");""");
    }

    private static void EnsureItemConditions(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "ItemConditions" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ItemConditions" PRIMARY KEY AUTOINCREMENT,
                "Name" TEXT NOT NULL,
                "Color" TEXT NOT NULL DEFAULT '#8ad6ff',
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_ItemConditions_Name" ON "ItemConditions" ("Name");""");
    }

    private static void EnsureItemClassifications(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "ItemClasses" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ItemClasses" PRIMARY KEY AUTOINCREMENT,
                "Name" TEXT NOT NULL,
                "InventoryMode" TEXT NOT NULL,
                "Description" TEXT NULL,
                "Color" TEXT NULL,
                "Icon" TEXT NULL,
                "SortOrder" INTEGER NULL,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "ItemSubtypes" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ItemSubtypes" PRIMARY KEY AUTOINCREMENT,
                "ItemClassId" INTEGER NOT NULL,
                "Name" TEXT NOT NULL,
                "Unit" TEXT NULL,
                "MinStock" TEXT NULL,
                "TargetStock" TEXT NULL,
                "Description" TEXT NULL,
                "SortOrder" INTEGER NULL,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_ItemSubtypes_ItemClasses_ItemClassId" FOREIGN KEY ("ItemClassId") REFERENCES "ItemClasses" ("Id") ON DELETE CASCADE
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_ItemClasses_Name" ON "ItemClasses" ("Name");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_ItemClasses_IsActive_SortOrder_Name" ON "ItemClasses" ("IsActive", "SortOrder", "Name");""");
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_ItemSubtypes_ItemClassId_Name" ON "ItemSubtypes" ("ItemClassId", "Name");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_ItemSubtypes_ItemClassId_IsActive_SortOrder_Name" ON "ItemSubtypes" ("ItemClassId", "IsActive", "SortOrder", "Name");""");
    }

    private static void BackfillCategoryTags(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            INSERT OR IGNORE INTO "Tags" ("Name", "Color", "CreatedAt", "UpdatedAt")
            SELECT DISTINCT trim("Category"), '#48ffb0', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            FROM "Items"
            WHERE "Category" IS NOT NULL AND trim("Category") <> '';
            """);
        db.Database.ExecuteSqlRaw("""
            INSERT OR IGNORE INTO "ItemTags" ("ItemId", "TagId")
            SELECT i."Id", t."Id"
            FROM "Items" i
            JOIN "Tags" t ON t."Name" = trim(i."Category")
            WHERE i."Category" IS NOT NULL AND trim(i."Category") <> '';
            """);
    }

    private static void BackfillQuarantineFlag(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            UPDATE "Items"
            SET "IsQuarantined" = 1
            WHERE "Id" IN (
                SELECT it."ItemId"
                FROM "ItemTags" it
                JOIN "Tags" t ON t."Id" = it."TagId"
                WHERE t."Name" = 'Cuarentena'
            )
            OR "Code" IN ('IT-000-045', 'IT-000-154', 'IT-000-278');
            """);

        db.Database.ExecuteSqlRaw("""
            DELETE FROM "ItemTags"
            WHERE "TagId" IN (SELECT "Id" FROM "Tags" WHERE "Name" = 'Cuarentena')
            AND "ItemId" IN (SELECT "Id" FROM "Items" WHERE "IsQuarantined" = 1);
            """);
    }

    private static void CleanupItemClassificationTestData(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            UPDATE "Items"
            SET "ItemSubtypeId" = NULL
            WHERE "ItemSubtypeId" IN (
                SELECT s."Id"
                FROM "ItemSubtypes" s
                JOIN "ItemClasses" c ON c."Id" = s."ItemClassId"
                WHERE c."Name" = 'Prueba' OR s."Name" = 'Subtipo prueba'
            );
            """);

        db.Database.ExecuteSqlRaw("""
            UPDATE "Items"
            SET "ItemClassId" = NULL
            WHERE "ItemClassId" IN (SELECT "Id" FROM "ItemClasses" WHERE "Name" = 'Prueba');
            """);

        db.Database.ExecuteSqlRaw("""
            DELETE FROM "ItemTags"
            WHERE "ItemId" = (SELECT "Id" FROM "Items" WHERE "Code" = 'IT-000-190')
            AND "TagId" = (SELECT "Id" FROM "Tags" WHERE "Name" = 'Lote / Kit (temporal)');
            """);

        db.Database.ExecuteSqlRaw("""
            DELETE FROM "ItemSubtypes"
            WHERE ("Name" = 'Subtipo prueba' OR "ItemClassId" IN (SELECT "Id" FROM "ItemClasses" WHERE "Name" = 'Prueba'))
            AND "Id" NOT IN (SELECT DISTINCT "ItemSubtypeId" FROM "Items" WHERE "ItemSubtypeId" IS NOT NULL);
            """);

        db.Database.ExecuteSqlRaw("""
            DELETE FROM "ItemClasses"
            WHERE "Name" = 'Prueba'
            AND "Id" NOT IN (SELECT DISTINCT "ItemClassId" FROM "Items" WHERE "ItemClassId" IS NOT NULL)
            AND "Id" NOT IN (SELECT DISTINCT "ItemClassId" FROM "ItemSubtypes");
            """);

        db.Database.ExecuteSqlRaw("""
            UPDATE "ItemClasses"
            SET "IsActive" = 0
            WHERE "Name" = 'Prueba';
            """);

        db.Database.ExecuteSqlRaw("""
            UPDATE "ItemSubtypes"
            SET "IsActive" = 0
            WHERE "Name" = 'Subtipo prueba';
            """);
    }

    private static void BackfillItemConditions(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            INSERT OR IGNORE INTO "ItemConditions" ("Name", "Color", "CreatedAt", "UpdatedAt")
            SELECT DISTINCT trim("Condition"), '#8ad6ff', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            FROM "Items"
            WHERE "Condition" IS NOT NULL AND trim("Condition") <> '';
            """);
        db.Database.ExecuteSqlRaw("""
            INSERT OR IGNORE INTO "ItemConditions" ("Name", "Color", "CreatedAt", "UpdatedAt")
            VALUES
                ('Nuevo', '#48ffb0', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                ('Bueno', '#8ad6ff', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                ('Usado', '#ffc86b', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                ('Revisar', '#ffd39f', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                ('Para reparar', '#ff8f8f', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            """);
    }

    private static void BackfillItemCodes(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            UPDATE "Items"
            SET "Code" = 'IT-' || printf('%03d', "Id" / 1000) || '-' || printf('%03d', "Id" % 1000)
            WHERE "Code" IS NULL OR trim("Code") = '';
            """);
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
                "Code" TEXT NULL,
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
            INSERT INTO "Items_new" ("Id","Code","BoxId","Name","Category","Quantity","Condition","Retention","Sentimental","Obsolete","Consumable","MinQuantity","Unit","Notes","CoverPhoto","ArchivedAt","CreatedAt","UpdatedAt")
                SELECT "Id","Code","BoxId","Name","Category","Quantity","Condition","Retention","Sentimental","Obsolete","Consumable","MinQuantity","Unit","Notes","CoverPhoto","ArchivedAt","CreatedAt","UpdatedAt" FROM "Items";
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

    private static void BackfillBoxCoverPhotos(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            UPDATE "Boxes"
            SET "CoverPhoto" = (
                SELECT p."Filename"
                FROM "Photos" p
                WHERE p."EntityType" = 'Box'
                  AND p."EntityId" = "Boxes"."Id"
                  AND p."Status" = 'Active'
                ORDER BY p."CreatedAt" DESC, p."Id" DESC
                LIMIT 1
            )
            WHERE ("CoverPhoto" IS NULL OR trim("CoverPhoto") = '')
              AND EXISTS (
                  SELECT 1
                  FROM "Photos" p
                  WHERE p."EntityType" = 'Box'
                    AND p."EntityId" = "Boxes"."Id"
                    AND p."Status" = 'Active'
              );
            """);
    }

    private static void NormalizeChildBoxLocations(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            WITH RECURSIVE box_roots AS (
                SELECT "Id", "Id" AS "RootId", "LocationId" AS "RootLocationId"
                FROM "Boxes"
                WHERE "ParentBoxId" IS NULL
                UNION ALL
                SELECT child."Id", box_roots."RootId", box_roots."RootLocationId"
                FROM "Boxes" child
                JOIN box_roots ON child."ParentBoxId" = box_roots."Id"
            )
            UPDATE "Boxes"
            SET "LocationId" = (
                SELECT "RootLocationId"
                FROM box_roots
                WHERE box_roots."Id" = "Boxes"."Id"
                LIMIT 1
            )
            WHERE "ParentBoxId" IS NOT NULL
              AND "LocationId" <> (
                  SELECT "RootLocationId"
                  FROM box_roots
                  WHERE box_roots."Id" = "Boxes"."Id"
                  LIMIT 1
              );
            """);
    }

    private static void NormalizeContainerTypes(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw($"""
            UPDATE "Boxes"
            SET "ContainerType" = '{Models.Box.DefaultContainerType}'
            WHERE "ContainerType" IS NULL OR trim("ContainerType") = '';
            """);
    }
}
#pragma warning restore EF1002
