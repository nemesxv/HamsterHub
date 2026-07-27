using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HamsterHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class RedesignPetCareTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "CareTasks");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "CareTasks");

            migrationBuilder.AddColumn<int>(
                name: "CareCategoryId",
                table: "CareTasks",
                type: "int",
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "PetId",
                table: "CareTasks",
                type: "int",
                nullable: false);

            migrationBuilder.CreateTable(
                name: "CareCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdId = table.Column<int>(type: "int", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CustomName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareCategories_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "CareCategories",
                columns: new[] { "Id", "Code", "CustomName", "HouseholdId", "IsActive" },
                values: new object[,]
                {
                    { 1, "Feeding", null, null, true },
                    { 2, "Water", null, null, true },
                    { 3, "Cleaning", null, null, true },
                    { 4, "Playing", null, null, true },
                    { 5, "Health", null, null, true }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareTasks_CareCategoryId",
                table: "CareTasks",
                column: "CareCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CareTasks_PetId",
                table: "CareTasks",
                column: "PetId");

            migrationBuilder.CreateIndex(
                name: "IX_CareCategories_HouseholdId_CustomName",
                table: "CareCategories",
                columns: new[] { "HouseholdId", "CustomName" },
                unique: true,
                filter: "[HouseholdId] IS NOT NULL AND [CustomName] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CareTasks_CareCategories_CareCategoryId",
                table: "CareTasks",
                column: "CareCategoryId",
                principalTable: "CareCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CareTasks_Pets_PetId",
                table: "CareTasks",
                column: "PetId",
                principalTable: "Pets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CareTasks_CareCategories_CareCategoryId",
                table: "CareTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_CareTasks_Pets_PetId",
                table: "CareTasks");

            migrationBuilder.DropTable(
                name: "CareCategories");

            migrationBuilder.DropIndex(
                name: "IX_CareTasks_CareCategoryId",
                table: "CareTasks");

            migrationBuilder.DropIndex(
                name: "IX_CareTasks_PetId",
                table: "CareTasks");

            migrationBuilder.DropColumn(
                name: "CareCategoryId",
                table: "CareTasks");

            migrationBuilder.DropColumn(
                name: "PetId",
                table: "CareTasks");

            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "CareTasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "CareTasks",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }
    }
}
