using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortP2P.Discovery.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Routes",
                columns: table => new
                {
                    RouteId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routes", x => x.RouteId);
                });

            migrationBuilder.CreateTable(
                name: "PeerIdentityAddress",
                columns: table => new
                {
                    RouteId = table.Column<string>(type: "TEXT", nullable: false),
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    PeerIdentity_Nickname = table.Column<string>(type: "TEXT", nullable: false),
                    PeerIdentity_NetworkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeerIdentity_DataUdpPort = table.Column<int>(type: "INTEGER", nullable: false),
                    PeerAddress = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeerIdentityAddress", x => new { x.RouteId, x.Id });
                    table.ForeignKey(
                        name: "FK_PeerIdentityAddress_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "RouteId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PeerIdentityAddress");

            migrationBuilder.DropTable(
                name: "Routes");
        }
    }
}
