using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortP2P.Discovery.Migrations
{
    /// <inheritdoc />
    public partial class last_seen_for_peer_address : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeen",
                table: "PeerIdentityAddress",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSeen",
                table: "PeerIdentityAddress");
        }
    }
}
