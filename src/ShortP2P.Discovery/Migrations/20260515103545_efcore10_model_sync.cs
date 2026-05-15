using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortP2P.Discovery.Migrations
{
    /// <inheritdoc />
    public partial class efcore10_model_sync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PeerIdentityAddress",
                table: "PeerIdentityAddress");

            migrationBuilder.AlterColumn<long>(
                name: "Id",
                table: "PeerIdentityAddress",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PeerIdentityAddress",
                table: "PeerIdentityAddress",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_PeerIdentityAddress_RouteId",
                table: "PeerIdentityAddress",
                column: "RouteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PeerIdentityAddress",
                table: "PeerIdentityAddress");

            migrationBuilder.DropIndex(
                name: "IX_PeerIdentityAddress_RouteId",
                table: "PeerIdentityAddress");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "PeerIdentityAddress",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER")
                .OldAnnotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PeerIdentityAddress",
                table: "PeerIdentityAddress",
                columns: new[] { "RouteId", "Id" });
        }
    }
}
