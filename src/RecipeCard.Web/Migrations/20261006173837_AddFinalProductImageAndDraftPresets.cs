using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddFinalProductImageAndDraftPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ImageAspectRatioPreset",
                table: "RecipeSteps",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FinalImageAspectRatioPreset",
                table: "Recipes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FinalMediaAssetId",
                table: "Recipes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "RecipeStepId",
                table: "AiImageDrafts",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "AspectRatioPreset",
                table: "AiImageDrafts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RecipeId",
                table: "AiImageDrafts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "AiImageDrafts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
            migrationBuilder.Sql(
                """
                UPDATE AiImageDrafts
                SET RecipeId = (
                    SELECT rs.RecipeId
                    FROM RecipeSteps rs
                    WHERE rs.Id = AiImageDrafts.RecipeStepId
                )
                WHERE RecipeStepId IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_FinalMediaAssetId",
                table: "Recipes",
                column: "FinalMediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AiImageDrafts_RecipeId",
                table: "AiImageDrafts",
                column: "RecipeId");

            migrationBuilder.AddForeignKey(
                name: "FK_AiImageDrafts_Recipes_RecipeId",
                table: "AiImageDrafts",
                column: "RecipeId",
                principalTable: "Recipes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Recipes_MediaAssets_FinalMediaAssetId",
                table: "Recipes",
                column: "FinalMediaAssetId",
                principalTable: "MediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiImageDrafts_Recipes_RecipeId",
                table: "AiImageDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_Recipes_MediaAssets_FinalMediaAssetId",
                table: "Recipes");

            migrationBuilder.DropIndex(
                name: "IX_Recipes_FinalMediaAssetId",
                table: "Recipes");

            migrationBuilder.DropIndex(
                name: "IX_AiImageDrafts_RecipeId",
                table: "AiImageDrafts");

            migrationBuilder.DropColumn(
                name: "ImageAspectRatioPreset",
                table: "RecipeSteps");

            migrationBuilder.DropColumn(
                name: "FinalImageAspectRatioPreset",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "FinalMediaAssetId",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "AspectRatioPreset",
                table: "AiImageDrafts");

            migrationBuilder.DropColumn(
                name: "RecipeId",
                table: "AiImageDrafts");

            migrationBuilder.DropColumn(
                name: "TargetKind",
                table: "AiImageDrafts");

            migrationBuilder.AlterColumn<int>(
                name: "RecipeStepId",
                table: "AiImageDrafts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
