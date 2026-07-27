using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HamsterHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AssignTasksToMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedMemberId",
                table: "CareTasks",
                type: "int",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "IX_CareTasks_AssignedMemberId",
                table: "CareTasks",
                column: "AssignedMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_CareTasks_HouseholdMembers_AssignedMemberId",
                table: "CareTasks",
                column: "AssignedMemberId",
                principalTable: "HouseholdMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CareTasks_HouseholdMembers_AssignedMemberId",
                table: "CareTasks");

            migrationBuilder.DropIndex(
                name: "IX_CareTasks_AssignedMemberId",
                table: "CareTasks");

            migrationBuilder.DropColumn(
                name: "AssignedMemberId",
                table: "CareTasks");
        }
    }
}
