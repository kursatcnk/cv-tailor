using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CvTailor.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InterviewState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InterviewJson",
                table: "JobTargets",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InterviewJson",
                table: "JobTargets");
        }
    }
}
