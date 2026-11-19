using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WooPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    ShopUrl = table.Column<string>(type: "text", nullable: false),
                    ConsumerKeyProtected = table.Column<string>(type: "text", nullable: false),
                    ConsumerSecretProtected = table.Column<string>(type: "text", nullable: false),
                    KeyHint = table.Column<string>(type: "text", nullable: false),
                    AuthMode = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBySub = table.Column<string>(type: "text", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBySub = table.Column<string>(type: "text", nullable: true),
                    LastTestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTestOk = table.Column<bool>(type: "boolean", nullable: true),
                    LastTestMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreConnections_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreConnections_StoreId",
                table: "StoreConnections",
                column: "StoreId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreConnections");
        }
    }
}
