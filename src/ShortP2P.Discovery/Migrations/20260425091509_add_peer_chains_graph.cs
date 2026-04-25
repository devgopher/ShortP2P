using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortP2P.Discovery.Migrations
{
    /// <inheritdoc />
    public partial class add_peer_chains_graph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PeerChains",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceRouteId = table.Column<string>(type: "TEXT", nullable: false),
                    TargetNetworkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChainKey = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeerChains", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PeerChainNodes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RouteId = table.Column<string>(type: "TEXT", nullable: false),
                    PeerIdentity_Nickname = table.Column<string>(type: "TEXT", nullable: false),
                    PeerIdentity_NetworkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeerIdentity_DataUdpPort = table.Column<int>(type: "INTEGER", nullable: false),
                    PeerAddress = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    PeerChainId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeerChainNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PeerChainNodes_PeerChains_PeerChainId",
                        column: x => x.PeerChainId,
                        principalTable: "PeerChains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PeerChainNodes_PeerChainId_OrderIndex",
                table: "PeerChainNodes",
                columns: new[] { "PeerChainId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_PeerChains_ChainKey",
                table: "PeerChains",
                column: "ChainKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeerChains_TargetNetworkId",
                table: "PeerChains",
                column: "TargetNetworkId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PeerChainNodes");

            migrationBuilder.DropTable(
                name: "PeerChains");
        }
    }
}
