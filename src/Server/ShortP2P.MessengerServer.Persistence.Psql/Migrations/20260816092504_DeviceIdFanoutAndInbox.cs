using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ShortP2P.MessengerServer.Persistence.Psql.Migrations
{
    /// <inheritdoc />
    public partial class DeviceIdFanoutAndInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_client_statuses",
                table: "client_statuses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_chat_requests",
                table: "chat_requests");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "chat_requests");

            migrationBuilder.AddColumn<string>(
                name: "DeviceId",
                table: "client_statuses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestId",
                table: "chat_requests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_client_statuses",
                table: "client_statuses",
                columns: new[] { "NetworkId", "DeviceId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_chat_requests",
                table: "chat_requests",
                column: "RequestId");

            migrationBuilder.CreateTable(
                name: "chat_request_inbox",
                columns: table => new
                {
                    RequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetNetworkId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_request_inbox", x => new { x.RequestId, x.DeviceId });
                });

            migrationBuilder.CreateTable(
                name: "message_inbox",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TgtNetworkId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_inbox", x => new { x.MessageId, x.DeviceId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_messages_CreatedUtc",
                table: "messages",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_chat_request_inbox_TargetNetworkId_DeviceId",
                table: "chat_request_inbox",
                columns: new[] { "TargetNetworkId", "DeviceId" });

            migrationBuilder.CreateIndex(
                name: "IX_message_inbox_TgtNetworkId_DeviceId",
                table: "message_inbox",
                columns: new[] { "TgtNetworkId", "DeviceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_request_inbox");

            migrationBuilder.DropTable(
                name: "message_inbox");

            migrationBuilder.DropIndex(
                name: "IX_messages_CreatedUtc",
                table: "messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_client_statuses",
                table: "client_statuses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_chat_requests",
                table: "chat_requests");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "client_statuses");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "chat_requests");

            migrationBuilder.AddColumn<long>(
                name: "Id",
                table: "chat_requests",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "PK_client_statuses",
                table: "client_statuses",
                column: "NetworkId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_chat_requests",
                table: "chat_requests",
                column: "Id");
        }
    }
}
