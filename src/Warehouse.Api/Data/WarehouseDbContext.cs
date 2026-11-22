using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Warehouse.Api.Models;

namespace Warehouse.Api.Data;

public partial class WarehouseDbContext : DbContext
{
    public WarehouseDbContext(DbContextOptions<WarehouseDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Batch> Batches { get; set; }

    public virtual DbSet<Level> Levels { get; set; }

    public virtual DbSet<Obstacle> Obstacles { get; set; }

    public virtual DbSet<Pallet> Pallets { get; set; }

    public virtual DbSet<PalletType> PalletTypes { get; set; }

    public virtual DbSet<ProductType> ProductTypes { get; set; }

    public virtual DbSet<Rack> Racks { get; set; }

    public virtual DbSet<Room> Rooms { get; set; }

    public virtual DbSet<Slot> Slots { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Batch>(entity =>
        {
            entity.HasIndex(e => e.Code, "UQ_Batches_Code").IsUnique();

            entity.Property(e => e.Code).HasMaxLength(20);
            entity.Property(e => e.ReceivedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<Level>(entity =>
        {
            entity.HasIndex(e => new { e.RackId, e.IsTop }, "UQ_Levels_OneTopPerRack")
                .IsUnique()
                .HasFilter("\"IsTop\"");

            entity.HasIndex(e => new { e.RackId, e.Number }, "UQ_Levels_Rack_Number").IsUnique();

            entity.HasOne(d => d.Rack).WithMany(p => p.Levels)
                .HasForeignKey(d => d.RackId)
                .HasConstraintName("FK_Levels_Racks");
        });

        modelBuilder.Entity<Obstacle>(entity =>
        {
            entity.HasIndex(e => e.RoomId, "IX_Obstacles_RoomId");

            entity.Property(e => e.Kind).HasMaxLength(20);

            entity.HasOne(d => d.Room).WithMany(p => p.Obstacles)
                .HasForeignKey(d => d.RoomId)
                .HasConstraintName("FK_Obstacles_Rooms");
        });

        modelBuilder.Entity<Pallet>(entity =>
        {
            entity.HasIndex(e => e.BatchId, "IX_Pallets_BatchId");

            entity.HasIndex(e => e.PalletTypeId, "IX_Pallets_PalletTypeId");

            entity.HasIndex(e => e.ProductTypeId, "IX_Pallets_ProductTypeId");

            entity.HasIndex(e => e.Code, "UQ_Pallets_Code").IsUnique();

            entity.Property(e => e.Code).HasMaxLength(30);

            entity.HasOne(d => d.Batch).WithMany(p => p.Pallets)
                .HasForeignKey(d => d.BatchId)
                .HasConstraintName("FK_Pallets_Batches");

            entity.HasOne(d => d.PalletType).WithMany(p => p.Pallets)
                .HasForeignKey(d => d.PalletTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Pallets_PalletTypes");

            entity.HasOne(d => d.ProductType).WithMany(p => p.Pallets)
                .HasForeignKey(d => d.ProductTypeId)
                .HasConstraintName("FK_Pallets_ProductTypes");
        });

        modelBuilder.Entity<PalletType>(entity =>
        {
            entity.HasIndex(e => e.Code, "UQ_PalletTypes_Code").IsUnique();

            entity.Property(e => e.Code).HasMaxLength(10);
            entity.Property(e => e.Color).HasMaxLength(20);
        });

        modelBuilder.Entity<ProductType>(entity =>
        {
            entity.HasIndex(e => e.Code, "UQ_ProductTypes_Code").IsUnique();

            entity.Property(e => e.Code).HasMaxLength(20);
            entity.Property(e => e.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<Rack>(entity =>
        {
            entity.HasIndex(e => e.ProductTypeId, "IX_Racks_ProductTypeId");

            entity.HasIndex(e => new { e.RoomId, e.Code }, "UQ_Racks_Room_Code").IsUnique();

            entity.Property(e => e.Code).HasMaxLength(20);

            entity.HasOne(d => d.ProductType).WithMany(p => p.Racks)
                .HasForeignKey(d => d.ProductTypeId)
                .HasConstraintName("FK_Racks_ProductTypes");

            entity.HasOne(d => d.Room).WithMany(p => p.Racks)
                .HasForeignKey(d => d.RoomId)
                .HasConstraintName("FK_Racks_Rooms");
        });

        modelBuilder.Entity<Room>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<Slot>(entity =>
        {
            entity.HasIndex(e => new { e.LevelId, e.Position }, "UQ_Slots_Level_Position").IsUnique();

            entity.HasIndex(e => e.PalletId, "UQ_Slots_PalletId").IsUnique();

            entity.Property(e => e.Active).HasDefaultValue(true);
            entity.Property(e => e.Code).HasMaxLength(40);

            entity.HasOne(d => d.Level).WithMany(p => p.Slots)
                .HasForeignKey(d => d.LevelId)
                .HasConstraintName("FK_Slots_Levels");

            entity.HasOne(d => d.Pallet).WithOne(p => p.Slot)
                .HasForeignKey<Slot>(d => d.PalletId)
                .HasConstraintName("FK_Slots_Pallets");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
