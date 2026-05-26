using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortP2P.Discovery.Migrations
{
    /// <inheritdoc />
    public partial class byte_12 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "PeerIdentity_NetworkId",
                table: "PeerIdentityAddress",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<byte[]>(
                name: "TargetNetworkId",
                table: "PeerChains",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<byte[]>(
                name: "PeerIdentity_NetworkId",
                table: "PeerChainNodes",
                type: "BLOB",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "PeerIdentity_NetworkId",
                table: "PeerIdentityAddress",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetNetworkId",
                table: "PeerChains",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<Guid>(
                name: "PeerIdentity_NetworkId",
                table: "PeerChainNodes",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB");
        }
    }
}
