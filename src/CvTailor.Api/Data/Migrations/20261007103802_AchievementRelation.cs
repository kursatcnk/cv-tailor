using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CvTailor.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AchievementRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RelatedId",
                table: "Achievements",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RelatedId",
                table: "Achievements");
        }
    }
}
