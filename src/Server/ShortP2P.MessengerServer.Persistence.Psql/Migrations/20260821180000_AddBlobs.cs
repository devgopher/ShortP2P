using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortP2P.MessengerServer.Persistence.Psql.Migrations
{
    /// <inheritdoc />
    public partial class AddBlobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "blobs",
                columns: table => new
                {
                    BlobId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SrcNetworkId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TgtNetworkId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blobs", x => x.BlobId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_blobs_CreatedUtc",
                table: "blobs",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_blobs_TgtNetworkId",
                table: "blobs",
                column: "TgtNetworkId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "blobs");
        }
    }
}
