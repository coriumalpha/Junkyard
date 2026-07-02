using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Migrations;

public partial class AddItemClassifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ItemClasses",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                InventoryMode = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                Color = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                Icon = table.Column<string>(type: "TEXT", maxLength: 48, nullable: true),
                SortOrder = table.Column<int>(type: "INTEGER", nullable: true),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ItemClasses", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ItemSubtypes",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ItemClassId = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Unit = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                MinStock = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: true),
                TargetStock = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: true),
                Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                SortOrder = table.Column<int>(type: "INTEGER", nullable: true),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ItemSubtypes", x => x.Id);
                table.ForeignKey(
                    name: "FK_ItemSubtypes_ItemClasses_ItemClassId",
                    column: x => x.ItemClassId,
                    principalTable: "ItemClasses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddColumn<int>(
            name: "ItemClassId",
            table: "Items",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ItemSubtypeId",
            table: "Items",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_ItemClasses_Name",
            table: "ItemClasses",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ItemClasses_IsActive_SortOrder_Name",
            table: "ItemClasses",
            columns: new[] { "IsActive", "SortOrder", "Name" });

        migrationBuilder.CreateIndex(
            name: "IX_ItemSubtypes_ItemClassId_Name",
            table: "ItemSubtypes",
            columns: new[] { "ItemClassId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ItemSubtypes_ItemClassId_IsActive_SortOrder_Name",
            table: "ItemSubtypes",
            columns: new[] { "ItemClassId", "IsActive", "SortOrder", "Name" });

        migrationBuilder.CreateIndex(
            name: "IX_Items_ItemClassId",
            table: "Items",
            column: "ItemClassId");

        migrationBuilder.CreateIndex(
            name: "IX_Items_ItemSubtypeId",
            table: "Items",
            column: "ItemSubtypeId");

        migrationBuilder.AddForeignKey(
            name: "FK_Items_ItemClasses_ItemClassId",
            table: "Items",
            column: "ItemClassId",
            principalTable: "ItemClasses",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_Items_ItemSubtypes_ItemSubtypeId",
            table: "Items",
            column: "ItemSubtypeId",
            principalTable: "ItemSubtypes",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_Items_ItemClasses_ItemClassId", table: "Items");
        migrationBuilder.DropForeignKey(name: "FK_Items_ItemSubtypes_ItemSubtypeId", table: "Items");
        migrationBuilder.DropIndex(name: "IX_Items_ItemClassId", table: "Items");
        migrationBuilder.DropIndex(name: "IX_Items_ItemSubtypeId", table: "Items");
        migrationBuilder.DropColumn(name: "ItemClassId", table: "Items");
        migrationBuilder.DropColumn(name: "ItemSubtypeId", table: "Items");
        migrationBuilder.DropTable(name: "ItemSubtypes");
        migrationBuilder.DropTable(name: "ItemClasses");
    }
}
