using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortP2P.MessengerServer.Auth.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuthAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_accounts",
                columns: table => new
                {
                    NetworkId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Nick = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PasswordSalt = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_accounts", x => x.NetworkId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_accounts_Nick",
                table: "auth_accounts",
                column: "Nick",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_accounts");
        }
    }
}
