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
        EnsureItemProperties(db);
        EnsureInventoryActions(db);
        AddColumn(db, "Items", "DescriptionMarkdown", "INTEGER NOT NULL DEFAULT 0");
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "ItemRelations" (
                "ItemId" INTEGER NOT NULL REFERENCES "Items"("Id") ON DELETE CASCADE,
                "RelatedItemId" INTEGER NOT NULL REFERENCES "Items"("Id") ON DELETE CASCADE,
                "CreatedAt" TEXT NOT NULL,
                PRIMARY KEY ("ItemId", "RelatedItemId"),
                CONSTRAINT "CK_ItemRelations_Order" CHECK ("ItemId" < "RelatedItemId")
            );
            CREATE INDEX IF NOT EXISTS "IX_ItemRelations_RelatedItemId" ON "ItemRelations" ("RelatedItemId");
            """);
        AddColumn(db, "Items", "ItemClassId", "INTEGER NULL");
        AddColumn(db, "Items", "ItemSubtypeId", "INTEGER NULL");
        AddColumn(db, "Items", "IsQuarantined", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(db, "Items", "NeedsReview", "INTEGER NOT NULL DEFAULT 0");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Items_ItemClassId" ON "Items" ("ItemClassId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Items_ItemSubtypeId" ON "Items" ("ItemSubtypeId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Items_IsQuarantined" ON "Items" ("IsQuarantined");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Items_NeedsReview" ON "Items" ("NeedsReview");""");
        db.Database.ExecuteSqlRaw("REINDEX IX_InventoryActions_Kind;");
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
        AddColumn(db, "AiSettings", "ProModel", "TEXT NOT NULL DEFAULT 'gpt-6-astra'");
        BackfillItemCodes(db);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Items_Code_Active" ON "Items" ("Code") WHERE "ArchivedAt" IS NULL AND "Code" IS NOT NULL AND trim("Code") <> '';""");
        NormalizeContainerTypes(db);
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
        AddColumn(db, "InventoryActions", "Kind", "TEXT NOT NULL DEFAULT 'Task'");
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

    private static void EnsureItemProperties(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "ItemPropertyDefinitions" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ItemPropertyDefinitions" PRIMARY KEY AUTOINCREMENT,
                "Scope" TEXT NOT NULL,
                "ItemClassId" INTEGER NULL,
                "ItemSubtypeId" INTEGER NULL,
                "Key" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "DataType" TEXT NOT NULL,
                "SortOrder" INTEGER NOT NULL DEFAULT 0,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "IsRequired" INTEGER NOT NULL DEFAULT 0,
                "Unit" TEXT NULL,
                "Placeholder" TEXT NULL,
                "HelpText" TEXT NULL,
                "MinNumber" TEXT NULL,
                "MaxNumber" TEXT NULL,
                "DefaultValueJson" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_ItemPropertyDefinitions_ItemClasses_ItemClassId" FOREIGN KEY ("ItemClassId") REFERENCES "ItemClasses" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ItemPropertyDefinitions_ItemSubtypes_ItemSubtypeId" FOREIGN KEY ("ItemSubtypeId") REFERENCES "ItemSubtypes" ("Id") ON DELETE CASCADE,
                CONSTRAINT "CK_ItemPropertyDefinitions_ScopeTarget" CHECK (("Scope" = 'Class' AND "ItemClassId" IS NOT NULL AND "ItemSubtypeId" IS NULL) OR ("Scope" = 'Subtype' AND "ItemSubtypeId" IS NOT NULL AND "ItemClassId" IS NULL))
            );
            CREATE TABLE IF NOT EXISTS "ItemPropertyOptions" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ItemPropertyOptions" PRIMARY KEY AUTOINCREMENT,
                "DefinitionId" INTEGER NOT NULL,
                "Value" TEXT NOT NULL,
                "Label" TEXT NOT NULL,
                "SortOrder" INTEGER NOT NULL DEFAULT 0,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_ItemPropertyOptions_ItemPropertyDefinitions_DefinitionId" FOREIGN KEY ("DefinitionId") REFERENCES "ItemPropertyDefinitions" ("Id") ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS "ItemPropertyValues" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ItemPropertyValues" PRIMARY KEY AUTOINCREMENT,
                "ItemId" INTEGER NOT NULL,
                "DefinitionId" INTEGER NOT NULL,
                "TextValue" TEXT NULL,
                "LongTextValue" TEXT NULL,
                "IntegerValue" INTEGER NULL,
                "DecimalValue" TEXT NULL,
                "BooleanValue" INTEGER NULL,
                "DateValue" TEXT NULL,
                "JsonValue" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_ItemPropertyValues_Items_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Items" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ItemPropertyValues_ItemPropertyDefinitions_DefinitionId" FOREIGN KEY ("DefinitionId") REFERENCES "ItemPropertyDefinitions" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ItemPropertyDefinitions_Class_Key" ON "ItemPropertyDefinitions" ("Scope", "ItemClassId", "Key") WHERE "Scope" = 'Class' AND "ItemClassId" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ItemPropertyDefinitions_Subtype_Key" ON "ItemPropertyDefinitions" ("Scope", "ItemSubtypeId", "Key") WHERE "Scope" = 'Subtype' AND "ItemSubtypeId" IS NOT NULL;
            CREATE INDEX IF NOT EXISTS "IX_ItemPropertyDefinitions_List" ON "ItemPropertyDefinitions" ("Scope", "ItemClassId", "ItemSubtypeId", "IsActive", "SortOrder", "Name");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ItemPropertyOptions_DefinitionId_Value" ON "ItemPropertyOptions" ("DefinitionId", "Value");
            CREATE INDEX IF NOT EXISTS "IX_ItemPropertyOptions_List" ON "ItemPropertyOptions" ("DefinitionId", "IsActive", "SortOrder", "Label");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ItemPropertyValues_ItemId_DefinitionId" ON "ItemPropertyValues" ("ItemId", "DefinitionId");
            CREATE INDEX IF NOT EXISTS "IX_ItemPropertyValues_DefinitionId" ON "ItemPropertyValues" ("DefinitionId");
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
