using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HamsterHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class TaskReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ReminderStartDate",
                table: "CareTasks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ReminderTime",
                table: "CareTasks",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReminderTimeZoneId",
                table: "CareTasks",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderStartDate",
                table: "CareTasks");

            migrationBuilder.DropColumn(
                name: "ReminderTime",
                table: "CareTasks");

            migrationBuilder.DropColumn(
                name: "ReminderTimeZoneId",
                table: "CareTasks");
        }
    }
}
