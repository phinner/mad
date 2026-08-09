using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mad.Migrations
{
    /// <inheritdoc />
    public partial class AddRuleAccessibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Accessible",
                table: "AutoDeleteRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Accessible", table: "AutoDeleteRules");
        }
    }
}
