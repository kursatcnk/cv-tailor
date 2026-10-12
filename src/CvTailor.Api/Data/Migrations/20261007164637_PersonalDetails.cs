using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CvTailor.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersonalDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanTravel",
                table: "CareerProfiles",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverLicense",
                table: "CareerProfiles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MilitaryDeferredUntil",
                table: "CareerProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MilitaryStatus",
                table: "CareerProfiles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanTravel",
                table: "CareerProfiles");

            migrationBuilder.DropColumn(
                name: "DriverLicense",
                table: "CareerProfiles");

            migrationBuilder.DropColumn(
                name: "MilitaryDeferredUntil",
                table: "CareerProfiles");

            migrationBuilder.DropColumn(
                name: "MilitaryStatus",
                table: "CareerProfiles");
        }
    }
}
