using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mad.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutoDeleteRules",
                columns: table => new
                {
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    OlderThan = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    TargetUserType = table.Column<int>(type: "INTEGER", nullable: true),
                    IncludePins = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoDeleteRules", x => new { x.GuildId, x.ChannelId });
                }
            );

            migrationBuilder.CreateTable(
                name: "GuildSettings",
                columns: table => new
                {
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    LogChannelId = table.Column<ulong>(type: "INTEGER", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildSettings", x => x.GuildId);
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AutoDeleteRules");

            migrationBuilder.DropTable(name: "GuildSettings");
        }
    }
}
