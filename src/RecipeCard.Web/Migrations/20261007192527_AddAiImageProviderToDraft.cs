using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAiImageProviderToDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "AiImageDrafts",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Provider",
                table: "AiImageDrafts");
        }
    }
}
