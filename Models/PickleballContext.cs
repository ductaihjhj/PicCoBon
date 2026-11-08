using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace PickleballClub.Models;

public partial class PickleballContext : DbContext
{
    public PickleballContext()
    {
    }

    public PickleballContext(DbContextOptions<PickleballContext> options)
        : base(options)
    {
    }

    public virtual DbSet<TbAccount> TbAccounts { get; set; }

    public virtual DbSet<TbBooking> TbBookings { get; set; }

    public virtual DbSet<TbCourt> TbCourts { get; set; }

    public virtual DbSet<TbCourtLock> TbCourtLocks { get; set; }

    public virtual DbSet<TbCmsConfig> TbCmsConfigs { get; set; }

    public virtual DbSet<TbMenu> TbMenus { get; set; }

    public virtual DbSet<TbOrder> TbOrders { get; set; }

    public virtual DbSet<TbOrderDetail> TbOrderDetails { get; set; }

    public virtual DbSet<TbOrderStatus> TbOrderStatuses { get; set; }

    public virtual DbSet<TbProduct> TbProducts { get; set; }

    public virtual DbSet<TbProductCategory> TbProductCategories { get; set; }

    public virtual DbSet<TbRole> TbRoles { get; set; }

    public virtual DbSet<TbPaymentTransaction> TbPaymentTransactions { get; set; }

    public virtual DbSet<TbAuditLog> TbAuditLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TbAccount>(entity =>
        {
            entity.HasKey(e => e.AccountId);

            entity.ToTable("tb_Account");

            entity.Property(e => e.AccountId).HasColumnName("AccountId");
            entity.Property(e => e.Email).HasMaxLength(150);
            entity.Property(e => e.FullName).HasMaxLength(150);
            entity.Property(e => e.LastLogin).HasMaxLength(50);
            entity.Property(e => e.Password).HasMaxLength(256);
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.Username).HasMaxLength(50);
            entity.Property(e => e.Avatar).HasMaxLength(500);
            entity.Property(e => e.Gender).HasMaxLength(30);
            entity.Property(e => e.BirthDate).HasMaxLength(20);
            entity.Property(e => e.Address).HasMaxLength(250);
            entity.Property(e => e.SkillLevel).HasMaxLength(100);
            entity.Property(e => e.DominantHand).HasMaxLength(50);
            entity.Property(e => e.PaddleBrand).HasMaxLength(150);
            entity.Property(e => e.PlayStyle).HasMaxLength(150);
            entity.Property(e => e.EmergencyContact).HasMaxLength(150);
            entity.Property(e => e.Bio).HasMaxLength(500);
            entity.Property(e => e.MemberTier).HasMaxLength(50);

            entity.HasOne(d => d.Role).WithMany(p => p.TbAccounts)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("FK_tb_Account_tb_Role");
        });

        modelBuilder.Entity<TbBooking>(entity =>
        {
            entity.HasKey(e => e.BookingId);

            entity.ToTable("tb_Booking");

            entity.HasIndex(e => e.BookingCode).IsUnique();
            entity.HasIndex(e => new { e.CourtId, e.BookingDate, e.Status });

            entity.Property(e => e.BookingCode).HasMaxLength(30);
            entity.Property(e => e.BookingDate).HasMaxLength(20);
            entity.Property(e => e.CreatedDate).HasColumnType("datetime");
            entity.Property(e => e.CheckedInAt).HasColumnType("datetime");
            entity.Property(e => e.CompletedAt).HasColumnType("datetime");
            entity.Property(e => e.CancelledAt).HasColumnType("datetime");
            entity.Property(e => e.CustomerName).HasMaxLength(150);
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.SkillLevel).HasMaxLength(100);
            entity.Property(e => e.StartTime).HasMaxLength(10);
            entity.Property(e => e.Status).HasMaxLength(30);
            entity.Property(e => e.PaymentMethod).HasMaxLength(20);
            entity.Property(e => e.PaymentStatus).HasMaxLength(20).HasDefaultValue("unpaid");
            entity.Property(e => e.CheckInToken).HasMaxLength(256);
            entity.Property(e => e.CancelReason).HasMaxLength(300);

            entity.HasOne(d => d.Account).WithMany(p => p.TbBookings)
                .HasForeignKey(d => d.AccountId)
                .HasConstraintName("FK_tb_Booking_tb_Account");

            entity.HasOne(d => d.Court).WithMany(p => p.TbBookings)
                .HasForeignKey(d => d.CourtId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tb_Booking_tb_Court");

            entity.HasOne(d => d.OrderStatus).WithMany(p => p.TbBookings)
                .HasForeignKey(d => d.OrderStatusId)
                .HasConstraintName("FK_tb_Booking_tb_OrderStatus");
        });

        modelBuilder.Entity<TbCourt>(entity =>
        {
            entity.HasKey(e => e.CourtId);

            entity.ToTable("tb_Court");

            entity.Property(e => e.Alias).HasMaxLength(150);
            entity.Property(e => e.CourtName).HasMaxLength(150);
            entity.Property(e => e.CourtType).HasMaxLength(50);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Image).HasMaxLength(500);
            entity.Property(e => e.Surface).HasMaxLength(150);
            entity.Property(e => e.TypeLabel).HasMaxLength(250);
        });

        modelBuilder.Entity<TbCourtLock>(entity =>
        {
            entity.HasKey(e => e.LockId);

            entity.ToTable("tb_CourtLock");

            entity.HasIndex(e => new { e.CourtId, e.LockDate });

            entity.Property(e => e.CreatedDate).HasColumnType("datetime");
            entity.Property(e => e.LockCode).HasMaxLength(30);
            entity.Property(e => e.LockDate).HasMaxLength(20);
            entity.Property(e => e.Reason).HasMaxLength(250);
            entity.Property(e => e.StartTime).HasMaxLength(10);

            entity.HasOne(d => d.Court).WithMany(p => p.TbCourtLocks)
                .HasForeignKey(d => d.CourtId)
                .HasConstraintName("FK_tb_CourtLock_tb_Court");
        });

        modelBuilder.Entity<TbCmsConfig>(entity =>
        {
            entity.HasKey(e => e.ConfigKey);

            entity.ToTable("tb_CmsConfig");

            entity.Property(e => e.ConfigKey).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(250);
        });

        modelBuilder.Entity<TbMenu>(entity =>
        {
            entity.HasKey(e => e.MenuId);

            entity.ToTable("tb_Menu");

            entity.Property(e => e.Alias).HasMaxLength(150);
            entity.Property(e => e.CreatedBy).HasMaxLength(150);
            entity.Property(e => e.CreatedDate).HasColumnType("datetime");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(150);
            entity.Property(e => e.ModifiedDate).HasColumnType("datetime");
            entity.Property(e => e.Title).HasMaxLength(150);
        });

        modelBuilder.Entity<TbOrder>(entity =>
        {
            entity.HasKey(e => e.OrderId);

            entity.ToTable("tb_Order");

            entity.HasIndex(e => e.BookingId);
            entity.HasIndex(e => e.IdempotencyKey);

            entity.Property(e => e.Address).HasMaxLength(250);
            entity.Property(e => e.Code).HasMaxLength(50);
            entity.Property(e => e.BookingCode).HasMaxLength(30);
            entity.Property(e => e.PaymentStatus).HasMaxLength(20).HasDefaultValue("unpaid");
            entity.Property(e => e.PaymentMethod).HasMaxLength(20).HasDefaultValue("cash");
            entity.Property(e => e.IdempotencyKey).HasMaxLength(100);
            entity.Property(e => e.CreatedBy).HasMaxLength(150);
            entity.Property(e => e.CreatedDate).HasColumnType("datetime");
            entity.Property(e => e.CustomerName).HasMaxLength(150);
            entity.Property(e => e.ModifiedBy).HasMaxLength(150);
            entity.Property(e => e.ModifiedDate).HasColumnType("datetime");
            entity.Property(e => e.Phone).HasMaxLength(50);

            entity.HasOne(d => d.Booking).WithMany(p => p.TbOrders)
                .HasForeignKey(d => d.BookingId)
                .HasConstraintName("FK_tb_Order_tb_Booking");

            entity.HasOne(d => d.OrderStatus).WithMany(p => p.TbOrders)
                .HasForeignKey(d => d.OrderStatusId)
                .HasConstraintName("FK_tb_Order_tb_OrderStatus");
        });

        modelBuilder.Entity<TbOrderDetail>(entity =>
        {
            entity.HasKey(e => e.OrderDetailId);

            entity.ToTable("tb_OrderDetail");

            entity.HasOne(d => d.Order).WithMany(p => p.TbOrderDetails)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("FK_tb_OrderDetail_tb_Order");

            entity.HasOne(d => d.Product).WithMany(p => p.TbOrderDetails)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("FK_tb_OrderDetail_tb_Product");
        });

        modelBuilder.Entity<TbOrderStatus>(entity =>
        {
            entity.HasKey(e => e.OrderStatusId);

            entity.ToTable("tb_OrderStatus");

            entity.Property(e => e.Description).HasMaxLength(250);
            entity.Property(e => e.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<TbProduct>(entity =>
        {
            entity.HasKey(e => e.ProductId);

            entity.ToTable("tb_Product");

            entity.Property(e => e.Alias).HasMaxLength(250);
            entity.Property(e => e.CreatedBy).HasMaxLength(150);
            entity.Property(e => e.CreatedDate).HasColumnType("datetime");
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.Icon).HasMaxLength(300);
            entity.Property(e => e.Image).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(150);
            entity.Property(e => e.ModifiedDate).HasColumnType("datetime");
            entity.Property(e => e.Title).HasMaxLength(250);

            entity.HasOne(d => d.CategoryProduct).WithMany(p => p.TbProducts)
                .HasForeignKey(d => d.CategoryProductId)
                .HasConstraintName("FK_tb_Product_tb_ProductCategory");
        });

        modelBuilder.Entity<TbProductCategory>(entity =>
        {
            entity.HasKey(e => e.CategoryProductId);

            entity.ToTable("tb_ProductCategory");

            entity.Property(e => e.Alias).HasMaxLength(150);
            entity.Property(e => e.CreatedDate).HasColumnType("datetime");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Icon).HasMaxLength(250);
            entity.Property(e => e.Title).HasMaxLength(150);
        });

        modelBuilder.Entity<TbRole>(entity =>
        {
            entity.HasKey(e => e.RoleId);

            entity.ToTable("tb_Role");

            entity.Property(e => e.Description).HasMaxLength(250);
            entity.Property(e => e.RoleName).HasMaxLength(50);
        });

        modelBuilder.Entity<TbPaymentTransaction>(entity =>
        {
            entity.HasKey(e => e.TransactionId);
            entity.ToTable("tb_PaymentTransaction");
            entity.HasIndex(e => e.BookingId);
            entity.HasIndex(e => e.OrderId);
            entity.Property(e => e.BookingCode).HasMaxLength(30);
            entity.Property(e => e.PaymentMethod).HasMaxLength(20).HasDefaultValue("cash");
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("completed");
            entity.Property(e => e.ReferenceCode).HasMaxLength(100);
            entity.Property(e => e.Note).HasMaxLength(300);
            entity.Property(e => e.RecordedBy).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");

            entity.HasOne(d => d.Booking).WithMany(p => p.TbPaymentTransactions)
                .HasForeignKey(d => d.BookingId)
                .HasConstraintName("FK_tb_PaymentTransaction_tb_Booking");

            entity.HasOne(d => d.Order).WithMany(p => p.TbPaymentTransactions)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("FK_tb_PaymentTransaction_tb_Order");
        });

        modelBuilder.Entity<TbAuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditId);
            entity.ToTable("tb_AuditLog");
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.Action).HasMaxLength(100);
            entity.Property(e => e.EntityType).HasMaxLength(50);
            entity.Property(e => e.EntityId).HasMaxLength(50);
            entity.Property(e => e.ActorUsername).HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasMaxLength(64);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
