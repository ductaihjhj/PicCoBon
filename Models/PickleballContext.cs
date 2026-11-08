using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        base.OnConfiguring(optionsBuilder);
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

    public virtual DbSet<TbInventoryMovement> TbInventoryMovements { get; set; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SyncTypedColumns();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SyncTypedColumns();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void SyncTypedColumns()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
            {
                continue;
            }

            switch (entry.Entity)
            {
                case TbBooking b:
                    if (!string.IsNullOrWhiteSpace(b.BookingDate) && DateOnly.TryParse(b.BookingDate, out var bd))
                        b.BookingDateValue = bd;
                    if (!string.IsNullOrWhiteSpace(b.StartTime) && TimeOnly.TryParse(b.StartTime, out var st))
                        b.StartTimeValue = st;
                    b.AmountDueDec = b.AmountDue;
                    b.PaidAmountDec = b.PaidAmount;
                    break;

                case TbOrder o:
                    o.TotalAmountDec = o.TotalAmount ?? 0;
                    o.PaidAmountDec = o.PaidAmount;
                    break;

                case TbPaymentTransaction pt:
                    pt.AmountDec = pt.Amount;
                    break;

                case TbProduct p:
                    p.PriceDec = p.Price ?? 0;
                    break;

                case TbCourtLock cl:
                    if (!string.IsNullOrWhiteSpace(cl.LockDate) && DateOnly.TryParse(cl.LockDate, out var ld))
                        cl.LockDateValue = ld;
                    if (!string.IsNullOrWhiteSpace(cl.StartTime) && TimeOnly.TryParse(cl.StartTime, out var lst))
                        cl.StartTimeValue = lst;
                    break;
            }
        }
    }

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
            entity.Property(e => e.SecurityStamp).HasMaxLength(64);
            entity.Property(e => e.MustChangePassword).HasDefaultValue(false);

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
            entity.HasIndex(e => e.IdempotencyKey)
                .IsUnique()
                .HasFilter("[IdempotencyKey] IS NOT NULL");

            entity.Property(e => e.BookingCode).HasMaxLength(30);
            entity.Property(e => e.BookingDate).HasMaxLength(20);
            entity.Property(e => e.BookingDateValue).HasColumnType("date");
            entity.Property(e => e.StartTime).HasMaxLength(10);
            entity.Property(e => e.StartTimeValue).HasColumnType("time(0)");
            entity.Property(e => e.AmountDueDec).HasColumnType("decimal(18, 2)").HasDefaultValue(0m);
            entity.Property(e => e.PaidAmountDec).HasColumnType("decimal(18, 2)").HasDefaultValue(0m);
            entity.Property(e => e.CreatedDate).HasColumnType("datetime2");
            entity.Property(e => e.CheckedInAt).HasColumnType("datetime2");
            entity.Property(e => e.CompletedAt).HasColumnType("datetime2");
            entity.Property(e => e.CancelledAt).HasColumnType("datetime2");
            entity.Property(e => e.CustomerName).HasMaxLength(150);
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.SkillLevel).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(30);
            entity.Property(e => e.PaymentMethod).HasMaxLength(20);
            entity.Property(e => e.PaymentStatus).HasMaxLength(20).HasDefaultValue("unpaid");
            entity.Property(e => e.CheckInToken).HasMaxLength(256);
            entity.Property(e => e.AccessTokenHash).HasMaxLength(128);
            entity.Property(e => e.IdempotencyKey).HasMaxLength(100);
            entity.Property(e => e.CancelReason).HasMaxLength(300);
            entity.Property(e => e.RowVersion).IsRowVersion().IsRequired(false);

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

            entity.Property(e => e.CreatedDate).HasColumnType("datetime2");
            entity.Property(e => e.LockCode).HasMaxLength(30);
            entity.Property(e => e.LockDate).HasMaxLength(20);
            entity.Property(e => e.LockDateValue).HasColumnType("date");
            entity.Property(e => e.Reason).HasMaxLength(250);
            entity.Property(e => e.StartTime).HasMaxLength(10);
            entity.Property(e => e.StartTimeValue).HasColumnType("time(0)");
            entity.Property(e => e.RowVersion).IsRowVersion().IsRequired(false);

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
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime2");
            entity.Property(e => e.UpdatedBy).HasMaxLength(100);
            entity.Property(e => e.RowVersion).IsRowVersion().IsRequired(false);
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
            entity.HasIndex(e => e.AccountId);
            entity.HasIndex(e => e.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
            entity.HasIndex(e => e.IdempotencyKey)
                .IsUnique()
                .HasFilter("[IdempotencyKey] IS NOT NULL");

            entity.Property(e => e.Address).HasMaxLength(250);
            entity.Property(e => e.Code).HasMaxLength(50);
            entity.Property(e => e.BookingCode).HasMaxLength(30);
            entity.Property(e => e.PaymentStatus).HasMaxLength(20).HasDefaultValue("unpaid");
            entity.Property(e => e.Status).HasMaxLength(30).HasDefaultValue("confirmed");
            entity.Property(e => e.PaymentMethod).HasMaxLength(20).HasDefaultValue("cash");
            entity.Property(e => e.IdempotencyKey).HasMaxLength(100);
            entity.Property(e => e.TotalAmountDec).HasColumnType("decimal(18, 2)").HasDefaultValue(0m);
            entity.Property(e => e.PaidAmountDec).HasColumnType("decimal(18, 2)").HasDefaultValue(0m);
            entity.Property(e => e.CreatedBy).HasMaxLength(150);
            entity.Property(e => e.CreatedDate).HasColumnType("datetime2");
            entity.Property(e => e.CustomerName).HasMaxLength(150);
            entity.Property(e => e.ModifiedBy).HasMaxLength(150);
            entity.Property(e => e.ModifiedDate).HasColumnType("datetime2");
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.RowVersion).IsRowVersion().IsRequired(false);

            entity.HasOne(d => d.Booking).WithMany(p => p.TbOrders)
                .HasForeignKey(d => d.BookingId)
                .HasConstraintName("FK_tb_Order_tb_Booking");

            entity.HasOne(d => d.Account).WithMany()
                .HasForeignKey(d => d.AccountId)
                .HasConstraintName("FK_tb_Order_tb_Account");

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
            entity.Property(e => e.CreatedDate).HasColumnType("datetime2");
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.Icon).HasMaxLength(300);
            entity.Property(e => e.Image).HasMaxLength(500);
            entity.Property(e => e.ModifiedBy).HasMaxLength(150);
            entity.Property(e => e.ModifiedDate).HasColumnType("datetime2");
            entity.Property(e => e.Title).HasMaxLength(250);
            entity.Property(e => e.PriceDec).HasColumnType("decimal(18, 2)").HasDefaultValue(0m);
            entity.Property(e => e.RowVersion).IsRowVersion().IsRequired(false);

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
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.IdempotencyKey)
                .IsUnique()
                .HasFilter("[IdempotencyKey] IS NOT NULL");

            entity.Property(e => e.BookingCode).HasMaxLength(30);
            entity.Property(e => e.AmountDec).HasColumnType("decimal(18, 2)").HasDefaultValue(0m);
            entity.Property(e => e.Currency).HasMaxLength(10).HasDefaultValue("VND");
            entity.Property(e => e.TransactionType).HasMaxLength(20).HasDefaultValue("payment");
            entity.Property(e => e.PaymentMethod).HasMaxLength(20).HasDefaultValue("cash");
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("completed");
            entity.Property(e => e.IdempotencyKey).HasMaxLength(100);
            entity.Property(e => e.ReferenceCode).HasMaxLength(100);
            entity.Property(e => e.Note).HasMaxLength(300);
            entity.Property(e => e.RecordedBy).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2");

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
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2");
        });

        modelBuilder.Entity<TbInventoryMovement>(entity =>
        {
            entity.HasKey(e => e.MovementId);
            entity.ToTable("tb_InventoryMovement");
            entity.HasIndex(e => e.ProductId);
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.BookingId);
            entity.Property(e => e.Reason).HasMaxLength(200);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2");

            entity.HasOne(d => d.Product).WithMany(p => p.TbInventoryMovements)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("FK_tb_InventoryMovement_tb_Product");

            entity.HasOne(d => d.Order).WithMany(p => p.TbInventoryMovements)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("FK_tb_InventoryMovement_tb_Order");

            entity.HasOne(d => d.Booking).WithMany(p => p.TbInventoryMovements)
                .HasForeignKey(d => d.BookingId)
                .HasConstraintName("FK_tb_InventoryMovement_tb_Booking");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
