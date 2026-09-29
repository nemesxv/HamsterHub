using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HamsterHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class GeneralFamilyTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CareLogs_Pets_PetId",
                table: "CareLogs");

            migrationBuilder.AlterColumn<int>(
                name: "PetId",
                table: "CareTasks",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "CareCategoryId",
                table: "CareTasks",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "CareTasks",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PetId",
                table: "CareLogs",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_CareLogs_Pets_PetId",
                table: "CareLogs",
                column: "PetId",
                principalTable: "Pets",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM CareTasks WHERE PetId IS NULL OR CareCategoryId IS NULL)
                   OR EXISTS (SELECT 1 FROM CareLogs WHERE PetId IS NULL)
                    THROW 51000, 'Cannot downgrade while general family tasks or reports exist. Restore the pre-upgrade backup instead.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_CareLogs_Pets_PetId",
                table: "CareLogs");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "CareTasks");

            migrationBuilder.AlterColumn<int>(
                name: "PetId",
                table: "CareTasks",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CareCategoryId",
                table: "CareTasks",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PetId",
                table: "CareLogs",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CareLogs_Pets_PetId",
                table: "CareLogs",
                column: "PetId",
                principalTable: "Pets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
