using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CvTailor.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CoverLetter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverLetterJson",
                table: "TailoredCvs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverLetterJson",
                table: "TailoredCvs");
        }
    }
}
