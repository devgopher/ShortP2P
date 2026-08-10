using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortP2P.MessengerServer.Persistence.Psql.Migrations;

/// <inheritdoc />
public partial class DropClientAccounts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "client_accounts");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "client_accounts",
            columns: table => new
            {
                NetworkId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Nick = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                PasswordSalt = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PasswordHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_client_accounts", x => x.NetworkId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_client_accounts_Nick",
            table: "client_accounts",
            column: "Nick",
            unique: true);
    }
}
