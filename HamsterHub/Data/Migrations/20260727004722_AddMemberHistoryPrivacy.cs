using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HamsterHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberHistoryPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanViewOtherChildrenHistory",
                table: "HouseholdMembers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShareHistoryWithChildren",
                table: "HouseholdMembers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PointsTotalAfterApproval",
                table: "CareLogs",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanViewOtherChildrenHistory",
                table: "HouseholdMembers");

            migrationBuilder.DropColumn(
                name: "ShareHistoryWithChildren",
                table: "HouseholdMembers");

            migrationBuilder.DropColumn(
                name: "PointsTotalAfterApproval",
                table: "CareLogs");
        }
    }
}
