using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaAssetAndAiDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MediaAssetId",
                table: "RecipeSteps",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiImageDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecipeStepId = table.Column<int>(type: "INTEGER", nullable: false),
                    PromptSnapshot = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    UserBrief = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TemporaryFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiImageDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiImageDrafts_RecipeSteps_RecipeStepId",
                        column: x => x.RecipeStepId,
                        principalTable: "RecipeSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StorageProvider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProviderPublicId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DeliveryUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ByteSize = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    Caption = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AiImageDraftId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaAssets_AiImageDrafts_AiImageDraftId",
                        column: x => x.AiImageDraftId,
                        principalTable: "AiImageDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeSteps_MediaAssetId",
                table: "RecipeSteps",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AiImageDrafts_RecipeStepId",
                table: "AiImageDrafts",
                column: "RecipeStepId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_AiImageDraftId",
                table: "MediaAssets",
                column: "AiImageDraftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_ProviderPublicId",
                table: "MediaAssets",
                column: "ProviderPublicId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RecipeSteps_MediaAssets_MediaAssetId",
                table: "RecipeSteps",
                column: "MediaAssetId",
                principalTable: "MediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecipeSteps_MediaAssets_MediaAssetId",
                table: "RecipeSteps");

            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropTable(
                name: "AiImageDrafts");

            migrationBuilder.DropIndex(
                name: "IX_RecipeSteps_MediaAssetId",
                table: "RecipeSteps");

            migrationBuilder.DropColumn(
                name: "MediaAssetId",
                table: "RecipeSteps");
        }
    }
}
