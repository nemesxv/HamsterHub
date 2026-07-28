using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HamsterHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskAndCareLogPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "CareTasks",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CareLogPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CareLogId = table.Column<int>(type: "int", nullable: false),
                    ImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareLogPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareLogPhotos_CareLogs_CareLogId",
                        column: x => x.CareLogId,
                        principalTable: "CareLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareLogPhotos_CareLogId",
                table: "CareLogPhotos",
                column: "CareLogId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CareLogPhotos");

            migrationBuilder.DropColumn(
                name: "ImagePath",
                table: "CareTasks");
        }
    }
}
