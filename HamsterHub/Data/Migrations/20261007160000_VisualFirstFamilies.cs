using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace HamsterHub.Data.Migrations;

public partial class VisualFirstFamilies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("VisualKey", "CareTasks", type: "nvarchar(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>("Instructions", "CareTasks", type: "nvarchar(600)", maxLength: 600, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("HelpRequestedAt", "CareTasks", type: "datetimeoffset", nullable: true);
        migrationBuilder.AddColumn<bool>("PictureMode", "HouseholdMembers", type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<Guid>("SubmissionId", "CareLogs", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<string>("Feedback", "CareLogs", type: "nvarchar(300)", maxLength: 300, nullable: true);
        migrationBuilder.CreateIndex("IX_CareLogs_SubmissionId", "CareLogs", "SubmissionId", unique: true, filter: "[SubmissionId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_CareLogs_SubmissionId", "CareLogs");
        migrationBuilder.DropColumn("SubmissionId", "CareLogs");
        migrationBuilder.DropColumn("Feedback", "CareLogs");
        migrationBuilder.DropColumn("PictureMode", "HouseholdMembers");
        migrationBuilder.DropColumn("VisualKey", "CareTasks");
        migrationBuilder.DropColumn("Instructions", "CareTasks");
        migrationBuilder.DropColumn("HelpRequestedAt", "CareTasks");
    }
}
