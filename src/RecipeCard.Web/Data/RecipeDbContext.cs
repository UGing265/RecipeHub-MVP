using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Models;

namespace RecipeCard.Web.Data;

public class RecipeDbContext(DbContextOptions<RecipeDbContext> options) : DbContext(options)
{
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<RecipeStep> RecipeSteps => Set<RecipeStep>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<AiImageDraft> AiImageDrafts => Set<AiImageDraft>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Ingredient>(b =>
        {
            b.ToTable("Ingredients");
            b.HasKey(i => i.Id);
            b.Property(i => i.Name)
                .IsRequired()
                .HasMaxLength(100);
            b.Property(i => i.NormalizedName)
                .IsRequired()
                .HasMaxLength(100);
            b.Property(i => i.DefaultUnit)
                .IsRequired()
                .HasMaxLength(20);

            b.HasIndex(i => i.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<Recipe>(b =>
        {
            b.ToTable("Recipes");
            b.HasKey(r => r.Id);
            b.Property(r => r.Name)
                .IsRequired()
                .HasMaxLength(200);
            b.Property(r => r.GeneralNote)
                .HasMaxLength(2000);

            b.HasMany(r => r.Ingredients)
                .WithOne(ri => ri.Recipe)
                .HasForeignKey(ri => ri.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(r => r.Steps)
                .WithOne(s => s.Recipe)
                .HasForeignKey(s => s.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecipeIngredient>(b =>
        {
            b.ToTable("RecipeIngredients", t =>
            {
                t.HasCheckConstraint("CK_RecipeIngredient_Quantity_Positive", "CAST(Quantity AS REAL) > 0");
            });

            b.HasKey(ri => ri.Id);
            b.Property(ri => ri.Quantity).HasPrecision(10, 2);

            b.HasIndex(ri => new { ri.RecipeId, ri.IngredientId }).IsUnique();

            b.HasOne(ri => ri.Ingredient)
                .WithMany(i => i.RecipeIngredients)
                .HasForeignKey(ri => ri.IngredientId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RecipeStep>(b =>
        {
            b.ToTable("RecipeSteps");
            b.HasKey(s => s.Id);
            b.Property(s => s.Instruction)
                .IsRequired()
                .HasMaxLength(4000);
            b.Property(s => s.ImageFileName)
                .HasMaxLength(255);

            b.HasIndex(s => new { s.RecipeId, s.SortOrder }).IsUnique();
            b.HasIndex(s => s.MediaAssetId);

            b.HasOne(s => s.MediaAsset)
                .WithMany(m => m.RecipeSteps)
                .HasForeignKey(s => s.MediaAssetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MediaAsset>(b =>
        {
            b.ToTable("MediaAssets");
            b.HasKey(m => m.Id);
            b.Property(m => m.StorageProvider)
                .IsRequired()
                .HasMaxLength(50);
            b.Property(m => m.ProviderPublicId)
                .IsRequired()
                .HasMaxLength(255);
            b.Property(m => m.DeliveryUrl)
                .IsRequired()
                .HasMaxLength(2048);
            b.Property(m => m.OriginalFileName)
                .HasMaxLength(255);
            b.Property(m => m.MimeType)
                .IsRequired()
                .HasMaxLength(50);
            b.Property(m => m.Caption)
                .HasMaxLength(500);

            b.HasIndex(m => m.ProviderPublicId).IsUnique();

            b.HasOne(m => m.AiImageDraft)
                .WithOne(d => d.MediaAsset)
                .HasForeignKey<MediaAsset>(m => m.AiImageDraftId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AiImageDraft>(b =>
        {
            b.ToTable("AiImageDrafts");
            b.HasKey(d => d.Id);
            b.Property(d => d.PromptSnapshot)
                .IsRequired()
                .HasMaxLength(2000);
            b.Property(d => d.UserBrief)
                .HasMaxLength(500);
            b.Property(d => d.Model)
                .IsRequired()
                .HasMaxLength(100);
            b.Property(d => d.TemporaryFileName)
                .IsRequired()
                .HasMaxLength(255);

            b.HasIndex(d => d.RecipeStepId);

            b.HasOne(d => d.RecipeStep)
                .WithMany(s => s.AiImageDrafts)
                .HasForeignKey(d => d.RecipeStepId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
