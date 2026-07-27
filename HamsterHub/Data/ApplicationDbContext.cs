using HamsterHub.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HamsterHub.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Household> Households => Set<Household>();
    public DbSet<HouseholdMember> HouseholdMembers => Set<HouseholdMember>();
    public DbSet<Pet> Pets => Set<Pet>();
    public DbSet<CareCategory> CareCategories => Set<CareCategory>();
    public DbSet<CareTask> CareTasks => Set<CareTask>();
    public DbSet<CareLog> CareLogs => Set<CareLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
            entity.Property(user => user.DisplayName).HasMaxLength(100).IsRequired());

        builder.Entity<Household>(entity =>
            entity.Property(household => household.Name).HasMaxLength(100).IsRequired());

        builder.Entity<HouseholdMember>(entity =>
        {
            entity.HasIndex(member => new { member.HouseholdId, member.UserId }).IsUnique();
            entity.HasOne(member => member.Household)
                .WithMany(household => household.Members)
                .HasForeignKey(member => member.HouseholdId);
            entity.HasOne(member => member.User)
                .WithMany(user => user.HouseholdMemberships)
                .HasForeignKey(member => member.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Pet>(entity =>
        {
            entity.Property(pet => pet.Name).HasMaxLength(100).IsRequired();
            entity.Property(pet => pet.Species).HasMaxLength(100).IsRequired();
            entity.Property(pet => pet.PhotoPath).HasMaxLength(500);
        });

        builder.Entity<CareTask>(entity =>
        {
            entity.ToTable(table =>
                table.HasCheckConstraint("CK_CareTasks_PointValue_NonNegative", "[PointValue] >= 0"));
            entity.HasOne(task => task.Pet)
                .WithMany(pet => pet.CareTasks)
                .HasForeignKey(task => task.PetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(task => task.CareCategory)
                .WithMany(category => category.CareTasks)
                .HasForeignKey(task => task.CareCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(task => task.AssignedMember)
                .WithMany(member => member.AssignedCareTasks)
                .HasForeignKey(task => task.AssignedMemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CareCategory>(entity =>
        {
            entity.Property(category => category.Code).HasMaxLength(50);
            entity.Property(category => category.CustomName).HasMaxLength(100);
            entity.HasIndex(category => new { category.HouseholdId, category.CustomName })
                .IsUnique()
                .HasFilter("[HouseholdId] IS NOT NULL AND [CustomName] IS NOT NULL");
            entity.HasOne(category => category.Household)
                .WithMany(household => household.CareCategories)
                .HasForeignKey(category => category.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasData(
                new CareCategory { Id = 1, Code = "Feeding" },
                new CareCategory { Id = 2, Code = "Water" },
                new CareCategory { Id = 3, Code = "Cleaning" },
                new CareCategory { Id = 4, Code = "Playing" },
                new CareCategory { Id = 5, Code = "Health" });
        });

        builder.Entity<CareLog>(entity =>
        {
            entity.HasIndex(log => new { log.PetId, log.CompletedAt });
            entity.HasIndex(log => new { log.CompletedByUserId, log.Status });
            entity.ToTable(table =>
                table.HasCheckConstraint("CK_CareLogs_PointsAwarded_NonNegative", "[PointsAwarded] >= 0"));
            entity.HasOne(log => log.Pet)
                .WithMany(pet => pet.CareLogs)
                .HasForeignKey(log => log.PetId);
            entity.HasOne(log => log.CareTask)
                .WithMany(task => task.CareLogs)
                .HasForeignKey(log => log.CareTaskId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(log => log.CompletedByUser)
                .WithMany()
                .HasForeignKey(log => log.CompletedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(log => log.ApprovedByUser)
                .WithMany()
                .HasForeignKey(log => log.ApprovedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<IdentityRole>().HasData(
            new IdentityRole
            {
                Id = "parent",
                Name = "Parent",
                NormalizedName = "PARENT",
                ConcurrencyStamp = "ec1628fc-17da-4b2f-a15c-1b90d245f108"
            },
            new IdentityRole
            {
                Id = "child",
                Name = "Child",
                NormalizedName = "CHILD",
                ConcurrencyStamp = "d0d7806b-4e8e-4e4c-bd95-306a17f768aa"
            });
    }
}
