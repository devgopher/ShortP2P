using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ShortP2P.MessengerServer.Persistence.Psql.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_requests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequesterNetworkId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetNetworkId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicKey = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "chats",
                columns: table => new
                {
                    ChatId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NetworkIds = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chats", x => x.ChatId);
                });

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

            migrationBuilder.CreateTable(
                name: "client_statuses",
                columns: table => new
                {
                    NetworkId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_statuses", x => x.NetworkId);
                });

            migrationBuilder.CreateTable(
                name: "crypto_keys",
                columns: table => new
                {
                    SrcNetworkId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TgtNetworkId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crypto_keys", x => new { x.SrcNetworkId, x.TgtNetworkId });
                });

            migrationBuilder.CreateTable(
                name: "delivery_tickets",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_tickets", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SrcNetworkId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TgtNetworkId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EncryptedDataBase64 = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.MessageId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_requests_RequesterNetworkId_TargetNetworkId",
                table: "chat_requests",
                columns: new[] { "RequesterNetworkId", "TargetNetworkId" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_requests_TargetNetworkId",
                table: "chat_requests",
                column: "TargetNetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_client_accounts_Nick",
                table: "client_accounts",
                column: "Nick",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_SrcNetworkId",
                table: "messages",
                column: "SrcNetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_messages_TgtNetworkId",
                table: "messages",
                column: "TgtNetworkId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "chat_requests");
            migrationBuilder.DropTable(name: "chats");
            migrationBuilder.DropTable(name: "client_accounts");
            migrationBuilder.DropTable(name: "client_statuses");
            migrationBuilder.DropTable(name: "crypto_keys");
            migrationBuilder.DropTable(name: "delivery_tickets");
            migrationBuilder.DropTable(name: "messages");
        }
    }
}
