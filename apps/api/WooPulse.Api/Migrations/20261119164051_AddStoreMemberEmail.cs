using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WooPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreMemberEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "StoreMembers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "StoreMembers");
        }
    }
}
