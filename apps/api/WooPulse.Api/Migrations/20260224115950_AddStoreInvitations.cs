using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WooPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StoreMember_Stores_StoreId",
                table: "StoreMember");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StoreMember",
                table: "StoreMember");

            migrationBuilder.RenameTable(
                name: "StoreMember",
                newName: "StoreMembers");

            migrationBuilder.RenameIndex(
                name: "IX_StoreMember_UserSub",
                table: "StoreMembers",
                newName: "IX_StoreMembers_UserSub");

            migrationBuilder.RenameIndex(
                name: "IX_StoreMember_StoreId_UserSub",
                table: "StoreMembers",
                newName: "IX_StoreMembers_StoreId_UserSub");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StoreMembers",
                table: "StoreMembers",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "StoreInvitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Token = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBySub = table.Column<string>(type: "text", nullable: false),
                    RespondedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RespondedBySub = table.Column<string>(type: "text", nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreInvitations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreInvitations_StoreId_Email_Status",
                table: "StoreInvitations",
                columns: new[] { "StoreId", "Email", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreInvitations_Token",
                table: "StoreInvitations",
                column: "Token",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StoreMembers_Stores_StoreId",
                table: "StoreMembers",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StoreMembers_Stores_StoreId",
                table: "StoreMembers");

            migrationBuilder.DropTable(
                name: "StoreInvitations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StoreMembers",
                table: "StoreMembers");

            migrationBuilder.RenameTable(
                name: "StoreMembers",
                newName: "StoreMember");

            migrationBuilder.RenameIndex(
                name: "IX_StoreMembers_UserSub",
                table: "StoreMember",
                newName: "IX_StoreMember_UserSub");

            migrationBuilder.RenameIndex(
                name: "IX_StoreMembers_StoreId_UserSub",
                table: "StoreMember",
                newName: "IX_StoreMember_StoreId_UserSub");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StoreMember",
                table: "StoreMember",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StoreMember_Stores_StoreId",
                table: "StoreMember",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
