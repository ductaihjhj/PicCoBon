using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;

namespace PickleballClub.Services;

public class OrderItemInput
{
    public int ProductId { get; set; }
    public int Qty { get; set; }
}

public class CreateShopOrderInput
{
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? NoteOrAddress { get; set; }
    public string? BookingCode { get; set; }
    public string? BookingAccessToken { get; set; }
    public string? IdempotencyKey { get; set; }
    public int? ActorAccountId { get; set; }
    public int? ActorRoleId { get; set; }
    public string? ActorUsername { get; set; }
    public string? IpAddress { get; set; }
    public List<OrderItemInput> Items { get; set; } = new();
}

public class OrderOperationResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; } = 200;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public TbOrder? Order { get; set; }
    public TbBooking? LinkedBooking { get; set; }
    public TbPaymentTransaction? Transaction { get; set; }
    public bool IsIdempotentReplay { get; set; }

    public static OrderOperationResult Fail(string msg, int code = 400, string? errorCode = null) => new()
    {
        Success = false,
        StatusCode = code,
        ErrorCode = errorCode,
        ErrorMessage = msg
    };

    public static OrderOperationResult Ok(TbOrder order, TbBooking? linked = null, TbPaymentTransaction? tx = null, bool replay = false) => new()
    {
        Success = true,
        StatusCode = 200,
        Order = order,
        LinkedBooking = linked,
        Transaction = tx,
        IsIdempotentReplay = replay
    };
}

public class OrderService
{
    public async Task<OrderOperationResult> CreateOrderAsync(
        PickleballContext context,
        CreateShopOrderInput input)
    {
        string cleanName = (input.CustomerName ?? string.Empty).Trim();
        if (cleanName.Length < 2 || cleanName.Length > 120)
        {
            return OrderOperationResult.Fail("Vui lòng nhập họ tên người nhận hợp lệ (2–120 ký tự).");
        }

        if (!BookingService.IsValidPhone(input.Phone))
        {
            return OrderOperationResult.Fail("Số điện thoại liên hệ không hợp lệ.");
        }
        string cleanPhone = BookingService.NormalizePhone(input.Phone);

        if (input.Items == null || input.Items.Count == 0)
        {
            return OrderOperationResult.Fail("Giỏ hàng đang trống.");
        }

        if (input.Items.Any(i => i.ProductId <= 0 || i.Qty <= 0 || i.Qty > 500))
        {
            return OrderOperationResult.Fail("Số lượng đặt của mỗi mặt hàng phải từ 1 đến 500.", 400);
        }

        // Aggregate duplicate product lines so stock validation cannot be bypassed
        var groupedItems = input.Items
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, TotalQty = g.Sum(x => x.Qty) })
            .OrderBy(g => g.ProductId)
            .ToList();

        if (groupedItems.Any(g => g.TotalQty <= 0 || g.TotalQty > 500))
        {
            return OrderOperationResult.Fail("Tổng số lượng mỗi sản phẩm không hợp lệ.", 400);
        }

        string cleanIdem = IdempotencyService.NormalizeKey(input.IdempotencyKey) ?? string.Empty;
        string canonicalItems = string.Join(",", groupedItems.Select(g => $"{g.ProductId}:{g.TotalQty}"));
        string requestedBookingCode = string.IsNullOrWhiteSpace(input.BookingCode)
            ? "STANDALONE"
            : input.BookingCode.Trim().ToUpperInvariant();
        string idemScope = "order.create";
        string idemActor = IdempotencyService.BuildActorKey(input.ActorAccountId, cleanPhone);
        string idemTarget = $"ORDER_TARGET:{requestedBookingCode}";
        string idemPayload = $"booking={requestedBookingCode}|phone={cleanPhone}|name={cleanName.ToLowerInvariant()}|items={canonicalItems}";

        if (!string.IsNullOrEmpty(cleanIdem))
        {
            var idemCheck = await IdempotencyService.CheckAsync(context, cleanIdem, idemScope, idemActor, idemTarget, idemPayload);
            if (idemCheck.Status == IdempotencyStatus.Conflict)
            {
                return OrderOperationResult.Fail(idemCheck.ConflictMessage!, 409, IdempotencyService.ConflictErrorCode);
            }
            if (idemCheck.Status == IdempotencyStatus.Replay && idemCheck.ExistingRecord?.ResultEntityId != null)
            {
                var replayed = await context.TbOrders.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.OrderId == idemCheck.ExistingRecord.ResultEntityId.Value);
                if (replayed != null)
                {
                    return OrderOperationResult.Ok(replayed, replay: true);
                }
            }

            var existingPre = await context.TbOrders
                .Include(o => o.TbOrderDetails)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.IdempotencyKey == cleanIdem);
            if (existingPre != null)
            {
                string existingItems = string.Join(",", existingPre.TbOrderDetails
                    .Where(d => d.ProductId.HasValue && d.Quantity.HasValue)
                    .GroupBy(d => d.ProductId!.Value)
                    .OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}:{g.Sum(x => x.Quantity!.Value)}"));
                bool matchesExisting =
                    string.Equals(existingItems, canonicalItems, StringComparison.Ordinal) &&
                    string.Equals(existingPre.BookingCode ?? "STANDALONE", requestedBookingCode, StringComparison.OrdinalIgnoreCase) &&
                    existingPre.AccountId == input.ActorAccountId &&
                    string.Equals(BookingService.NormalizePhone(existingPre.Phone), cleanPhone, StringComparison.Ordinal);

                if (!matchesExisting)
                {
                    return OrderOperationResult.Fail(
                        "Khóa IdempotencyKey đã được sử dụng cho một đơn hàng khác.",
                        409,
                        IdempotencyService.ConflictErrorCode);
                }

                return OrderOperationResult.Ok(existingPre, replay: true);
            }
        }

        bool isRelational = context.Database.IsRelational();
        await using var tx = isRelational ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted) : null;

        try
        {
            if (!string.IsNullOrEmpty(cleanIdem))
            {
                var txIdem = await IdempotencyService.CheckAsync(context, cleanIdem, idemScope, idemActor, idemTarget, idemPayload);
                if (txIdem.Status == IdempotencyStatus.Conflict)
                {
                    if (tx != null) await tx.RollbackAsync();
                    return OrderOperationResult.Fail(txIdem.ConflictMessage!, 409, IdempotencyService.ConflictErrorCode);
                }
                if (txIdem.Status == IdempotencyStatus.Replay && txIdem.ExistingRecord?.ResultEntityId != null)
                {
                    var existingInTx = await context.TbOrders.FirstOrDefaultAsync(o => o.OrderId == txIdem.ExistingRecord.ResultEntityId.Value);
                    if (existingInTx != null)
                    {
                        if (tx != null) await tx.CommitAsync();
                        return OrderOperationResult.Ok(existingInTx, replay: true);
                    }
                }
            }

            var vnNow = BookingService.GetVietnamNow();
            string todayIso = vnNow.ToString("yyyy-MM-dd");

            TbBooking? linkedBooking = null;
            if (!string.IsNullOrWhiteSpace(input.BookingCode))
            {
                string requestedCode = input.BookingCode.Trim().ToUpperInvariant();
                if (isRelational)
                {
                    await context.Database.ExecuteSqlRawAsync(
                        "SELECT 1 FROM tb_Booking WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE BookingCode = {0}",
                        requestedCode);
                }

                linkedBooking = await context.TbBookings.FirstOrDefaultAsync(b => b.BookingCode == requestedCode);
                if (linkedBooking == null)
                {
                    if (tx != null) await tx.RollbackAsync();
                    return OrderOperationResult.Fail("Mã đặt sân cần gộp hóa đơn không tồn tại.", 404);
                }

                // P1-14: Check ownership FIRST before disclosing booking status/date/time (prevents IDOR oracle probing!)
                bool isStaffOrAdmin = input.ActorRoleId == 1 || input.ActorRoleId == 3;
                if (!isStaffOrAdmin)
                {
                    bool ownsByAccount = input.ActorAccountId.HasValue &&
                                         linkedBooking.AccountId.HasValue &&
                                         linkedBooking.AccountId.Value == input.ActorAccountId.Value;

                    bool ownsByToken = !string.IsNullOrWhiteSpace(input.BookingAccessToken) &&
                                       QrTokenService.VerifyGuestAccessToken(input.BookingAccessToken, linkedBooking.AccessTokenHash);

                    if (!ownsByAccount && !ownsByToken)
                    {
                        if (tx != null) await tx.RollbackAsync();
                        return OrderOperationResult.Fail(
                            "Không thể xác minh quyền sở hữu ca đặt sân (yêu cầu đăng nhập đúng tài khoản chủ vé hoặc nhập Mã bảo mật vé hợp lệ).", 403);
                    }
                }

                // P1-14: Must be an active approved/playing booking on TODAY's date within operating session!
                if (linkedBooking.Status != "approved" && linkedBooking.Status != "playing")
                {
                    if (tx != null) await tx.RollbackAsync();
                    return OrderOperationResult.Fail(
                        $"Chỉ có thể gộp đơn dịch vụ vào ca đặt sân Đã duyệt hoặc Đang thi đấu (trạng thái hiện tại: '{linkedBooking.Status}').", 409);
                }

                if (!string.Equals(linkedBooking.BookingDate, todayIso, StringComparison.Ordinal))
                {
                    if (tx != null) await tx.RollbackAsync();
                    return OrderOperationResult.Fail(
                        $"Chỉ cho phép gộp dịch vụ vào ca thi đấu diễn ra trong ngày hôm nay ({todayIso}).", 409);
                }

                if (PricingService.TryParseTimeMinutes(linkedBooking.StartTime, out int bStart))
                {
                    int bEnd = bStart + Math.Max(30, linkedBooking.Duration);
                    int nowMins = vnNow.Hour * 60 + vnNow.Minute;
                    if (nowMins >= bEnd)
                    {
                        if (tx != null) await tx.RollbackAsync();
                        return OrderOperationResult.Fail("Ca đặt sân này đã hết giờ thi đấu, không thể gộp thêm đơn mới.", 409);
                    }
                }
            }

            // Validate & atomically decrement stock in deterministic ProductId order using UPDLOCK to prevent S->X deadlocks
            var lineItems = new List<(TbProduct Product, int Qty, int UnitPrice, int BalanceAfter)>();
            foreach (var grp in groupedItems)
            {
                if (isRelational)
                {
                    await context.Database.ExecuteSqlRawAsync(
                        "SELECT 1 FROM tb_Product WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE ProductId = {0}",
                        grp.ProductId);
                }

                var prod = await context.TbProducts.FirstOrDefaultAsync(p => p.ProductId == grp.ProductId && p.IsActive);
                if (prod == null)
                {
                    if (tx != null) await tx.RollbackAsync();
                    return OrderOperationResult.Fail($"Mặt hàng #{grp.ProductId} không tồn tại hoặc đã ngừng bán.", 404);
                }

                int unitPrice = prod.Price ?? 0;
                if (unitPrice < 0)
                {
                    if (tx != null) await tx.RollbackAsync();
                    return OrderOperationResult.Fail("Đơn giá sản phẩm trên hệ thống không hợp lệ.", 422);
                }

                int balanceAfter;
                if (isRelational)
                {
                    int affected = await context.Database.ExecuteSqlRawAsync(@"
UPDATE tb_Product
SET UnitInStock = COALESCE(UnitInStock, Quantity, 0) - {1},
    Quantity = COALESCE(UnitInStock, Quantity, 0) - {1}
WHERE ProductId = {0} AND IsActive = 1 AND COALESCE(UnitInStock, Quantity, 0) >= {1}",
                        grp.ProductId, grp.TotalQty);

                    if (affected == 0)
                    {
                        if (tx != null) await tx.RollbackAsync();
                        return OrderOperationResult.Fail($"Mặt hàng '{prod.Title}' không đủ số lượng tồn kho.", 409);
                    }

                    await context.Entry(prod).ReloadAsync();
                    balanceAfter = prod.UnitInStock ?? prod.Quantity ?? 0;
                }
                else
                {
                    int available = prod.UnitInStock ?? prod.Quantity ?? 0;
                    if (available < grp.TotalQty)
                    {
                        return OrderOperationResult.Fail($"Mặt hàng '{prod.Title}' chỉ còn {available} sản phẩm trong kho.", 409);
                    }
                    balanceAfter = available - grp.TotalQty;
                    prod.UnitInStock = balanceAfter;
                    prod.Quantity = balanceAfter;
                }

                lineItems.Add((prod, grp.TotalQty, unitPrice, balanceAfter));
            }

            int totalAmount = lineItems.Sum(x => x.UnitPrice * x.Qty);
            int totalQty = lineItems.Sum(x => x.Qty);
            string orderCode = $"ORD-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

            var order = new TbOrder
            {
                Code = orderCode,
                BookingId = linkedBooking?.BookingId,
                AccountId = input.ActorAccountId,
                BookingCode = linkedBooking?.BookingCode,
                CustomerName = cleanName,
                Phone = cleanPhone,
                Address = string.IsNullOrWhiteSpace(input.NoteOrAddress)
                    ? (linkedBooking != null ? $"Giao ra sân ({linkedBooking.BookingCode})" : "Nhận tại quầy Pro Shop")
                    : input.NoteOrAddress.Trim()[..Math.Min(input.NoteOrAddress.Trim().Length, 250)],
                TotalAmount = totalAmount,
                PaidAmount = 0,
                PaymentStatus = linkedBooking != null ? "merged_to_booking" : "unpaid",
                Status = "confirmed",
                FulfillmentStatus = "pending",
                PaymentMethod = "cash",
                IdempotencyKey = string.IsNullOrEmpty(cleanIdem) ? null : cleanIdem,
                Quanlity = totalQty,
                OrderStatusId = 1,
                CreatedDate = vnNow,
                ModifiedDate = vnNow,
                CreatedBy = input.ActorUsername ?? cleanName
            };

            context.TbOrders.Add(order);
            await context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(cleanIdem))
            {
                string reqHash = IdempotencyService.ComputeCanonicalHash(idemScope, idemActor, idemTarget, idemPayload);
                context.TbIdempotencyRecords.Add(IdempotencyService.CreateRecord(
                    cleanIdem, idemScope, idemActor, idemTarget, reqHash, order.Code, order.OrderId));
            }

            foreach (var li in lineItems)
            {
                context.TbOrderDetails.Add(new TbOrderDetail
                {
                    OrderId = order.OrderId,
                    ProductId = li.Product.ProductId,
                    Price = li.UnitPrice,
                    Quantity = li.Qty
                });

                context.TbInventoryMovements.Add(new TbInventoryMovement
                {
                    ProductId = li.Product.ProductId,
                    OrderId = order.OrderId,
                    BookingId = linkedBooking?.BookingId,
                    ChangeQty = -li.Qty,
                    BalanceAfter = li.BalanceAfter,
                    Reason = $"Shop Order {orderCode}",
                    ActorAccountId = input.ActorAccountId,
                    CreatedAt = vnNow
                });
            }

            if (linkedBooking != null)
            {
                var posList = BookingService.ParsePosItems(linkedBooking.PosItemsJson);
                foreach (var li in lineItems)
                {
                    posList.Add(new PosCartItemDto
                    {
                        ProductId = li.Product.ProductId,
                        Name = li.Product.Title ?? "Pro Shop",
                        Price = li.UnitPrice,
                        Qty = li.Qty,
                        OrderId = order.OrderId,
                        OrderCode = order.Code
                    });
                }

                linkedBooking.PosItemsJson = System.Text.Json.JsonSerializer.Serialize(posList);
                linkedBooking.PosTotal = posList.Sum(p => p.Price * p.Qty);
                linkedBooking.AmountDue = Math.Max(0, linkedBooking.CourtFee - linkedBooking.DiscountAmount + linkedBooking.PosTotal);
                linkedBooking.PaymentStatus = BookingService.ComputePaymentStatus(linkedBooking.PaidAmount, linkedBooking.AmountDue);
            }

            context.TbAuditLogs.Add(new TbAuditLog
            {
                Action = "SHOP_ORDER_CREATED",
                EntityType = "Order",
                EntityId = orderCode,
                ActorAccountId = input.ActorAccountId,
                ActorUsername = input.ActorUsername,
                Details = $"Total={totalAmount}, Qty={totalQty}, LinkedBooking={linkedBooking?.BookingCode ?? "STANDALONE"}",
                IpAddress = input.IpAddress,
                CreatedAt = vnNow
            });

            await context.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            return OrderOperationResult.Ok(order, linkedBooking);
        }
        catch (DbUpdateException)
        {
            if (tx != null) await tx.RollbackAsync();

            if (!string.IsNullOrEmpty(cleanIdem))
            {
                var afterConflict = await IdempotencyService.CheckAsync(context, cleanIdem, idemScope, idemActor, idemTarget, idemPayload);
                if (afterConflict.Status == IdempotencyStatus.Conflict)
                {
                    return OrderOperationResult.Fail(afterConflict.ConflictMessage!, 409, IdempotencyService.ConflictErrorCode);
                }
                if (afterConflict.Status == IdempotencyStatus.Replay && afterConflict.ExistingRecord?.ResultEntityId != null)
                {
                    var replayed = await context.TbOrders.AsNoTracking()
                        .FirstOrDefaultAsync(o => o.OrderId == afterConflict.ExistingRecord.ResultEntityId.Value);
                    if (replayed != null)
                        return OrderOperationResult.Ok(replayed, replay: true);
                }
            }

            return OrderOperationResult.Fail("Giao dịch tạo đơn bị xung đột dữ liệu hoặc trùng khóa. Vui lòng thử lại.", 409);
        }
    }

    public async Task<OrderOperationResult> RecordStandaloneOrderPaymentAsync(
        PickleballContext context,
        string orderCodeOrId,
        int amount,
        string paymentMethod,
        string? referenceCode,
        string? note,
        string? idempotencyKey,
        int? actorAccountId,
        string? actorUsername,
        string? ipAddress = null)
    {
        if (amount <= 0)
        {
            return OrderOperationResult.Fail("Số tiền thu thực tế phải lớn hơn 0 VNĐ.", 400);
        }

        string cleanMethod = (paymentMethod ?? string.Empty).Trim().ToLowerInvariant();
        if (cleanMethod != "cash" && cleanMethod != "transfer")
        {
            return OrderOperationResult.Fail("Phương thức thanh toán chỉ chấp nhận 'cash' (Tiền mặt) hoặc 'transfer' (Chuyển khoản).", 400);
        }

        string? cleanRef = string.IsNullOrWhiteSpace(referenceCode)
            ? null
            : referenceCode.Trim()[..Math.Min(referenceCode.Trim().Length, 100)];
        if (cleanMethod == "transfer" && (cleanRef == null || cleanRef.Length < 3))
        {
            return OrderOperationResult.Fail("Vui lòng nhập mã giao dịch ngân hàng khi xác nhận Chuyển khoản.", 400);
        }

        string cleanIdem = string.IsNullOrWhiteSpace(idempotencyKey)
            ? string.Empty
            : idempotencyKey.Trim()[..Math.Min(idempotencyKey.Trim().Length, 100)];
        if (string.IsNullOrEmpty(cleanIdem))
        {
            return OrderOperationResult.Fail("Thiếu IdempotencyKey.", 400);
        }

        bool isRelational = context.Database.IsRelational();
        await using var tx = isRelational ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;

        var order = await FindOrderAsync(context, orderCodeOrId);
        if (order == null)
        {
            if (tx != null) await tx.RollbackAsync();
            return OrderOperationResult.Fail("Không tìm thấy đơn hàng Pro Shop.", 404);
        }

        string idemScope = "order.payment";
        string idemActor = IdempotencyService.BuildActorKey(actorAccountId, actorUsername);
        string idemTarget = $"ORDER:{order.Code ?? order.OrderId.ToString()}";
        string idemPayload = $"order={order.OrderId}|amount={amount}|method={cleanMethod}|ref={(cleanRef ?? "").ToUpperInvariant()}";

        var idemCheck = await IdempotencyService.CheckAsync(context, cleanIdem, idemScope, idemActor, idemTarget, idemPayload);
        if (idemCheck.Status == IdempotencyStatus.Conflict)
        {
            if (tx != null) await tx.RollbackAsync();
            return OrderOperationResult.Fail(idemCheck.ConflictMessage!, 409, IdempotencyService.ConflictErrorCode);
        }

        var existingTx = await context.TbPaymentTransactions.FirstOrDefaultAsync(t => t.IdempotencyKey == cleanIdem);
        if (existingTx != null)
        {
            bool matchesTx =
                existingTx.OrderId == order.OrderId &&
                string.Equals(existingTx.TransactionType, "payment", StringComparison.OrdinalIgnoreCase) &&
                existingTx.Amount == amount &&
                string.Equals(existingTx.PaymentMethod, cleanMethod, StringComparison.OrdinalIgnoreCase) &&
                (!actorAccountId.HasValue || !existingTx.ActorAccountId.HasValue || existingTx.ActorAccountId == actorAccountId);

            if (!matchesTx)
            {
                if (tx != null) await tx.RollbackAsync();
                return OrderOperationResult.Fail(
                    "Khóa IdempotencyKey đã được sử dụng cho một giao dịch thu tiền khác (khác đơn hàng, số tiền hoặc phương thức).",
                    409,
                    IdempotencyService.ConflictErrorCode);
            }

            if (tx != null) await tx.CommitAsync();
            return OrderOperationResult.Ok(order, tx: existingTx, replay: true);
        }

        if (order.BookingId.HasValue)
        {
            if (tx != null) await tx.RollbackAsync();
            return OrderOperationResult.Fail(
                $"Đơn hàng này đã được gộp vào hóa đơn sân {order.BookingCode}. Vui lòng thu tiền trên hóa đơn đặt sân.", 409);
        }

        if (order.Status == "cancelled")
        {
            if (tx != null) await tx.RollbackAsync();
            return OrderOperationResult.Fail("Đơn hàng đã hủy, không thể thu tiền.", 409);
        }

        int totalDue = order.TotalAmount ?? 0;
        int currentNetPaid = await ComputeOrderLedgerNetPaidAsync(context, order.OrderId);
        int remaining = Math.Max(0, totalDue - currentNetPaid);

        if (remaining == 0)
        {
            if (tx != null) await tx.RollbackAsync();
            return OrderOperationResult.Fail("Đơn hàng Pro Shop này đã được thanh toán đủ.", 409);
        }

        if (amount > remaining)
        {
            if (tx != null) await tx.RollbackAsync();
            return OrderOperationResult.Fail($"Số tiền thu ({amount:N0} đ) vượt quá số tiền còn phải thu ({remaining:N0} đ).", 422);
        }

        var vnNow = BookingService.GetVietnamNow();
        var paymentRow = new TbPaymentTransaction
        {
            OrderId = order.OrderId,
            Amount = amount,
            Currency = "VND",
            TransactionType = "payment",
            PaymentMethod = cleanMethod,
            Status = "completed",
            IdempotencyKey = cleanIdem,
            ReferenceCode = cleanRef ?? $"POS-{vnNow:yyyyMMddHHmmss}",
            Note = string.IsNullOrWhiteSpace(note) ? $"Thu tiền đơn lẻ Pro Shop {order.Code}" : note.Trim()[..Math.Min(note.Trim().Length, 300)],
            ActorAccountId = actorAccountId,
            RecordedBy = actorUsername,
            CreatedAt = vnNow
        };

        context.TbPaymentTransactions.Add(paymentRow);
        await context.SaveChangesAsync();

        string reqHash = IdempotencyService.ComputeCanonicalHash(idemScope, idemActor, idemTarget, idemPayload);
        context.TbIdempotencyRecords.Add(IdempotencyService.CreateRecord(
            cleanIdem, idemScope, idemActor, idemTarget, reqHash, order.Code, paymentRow.TransactionId));

        int newNetPaid = await ComputeOrderLedgerNetPaidAsync(context, order.OrderId);
        order.PaidAmount = newNetPaid;
        order.PaymentMethod = cleanMethod;
        order.PaymentStatus = BookingService.ComputePaymentStatus(newNetPaid, totalDue);
        if (newNetPaid >= totalDue && totalDue > 0)
        {
            order.Status = "completed";
            order.OrderStatusId = 4;
        }
        order.ModifiedDate = vnNow;
        order.ModifiedBy = actorUsername;

        context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "SHOP_ORDER_PAYMENT_RECORDED",
            EntityType = "Order",
            EntityId = order.Code,
            ActorAccountId = actorAccountId,
            ActorUsername = actorUsername,
            Details = $"Amount={amount}, Method={cleanMethod}, NetPaid={newNetPaid}/{totalDue}",
            IpAddress = ipAddress,
            CreatedAt = vnNow
        });

        await context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return OrderOperationResult.Ok(order, tx: paymentRow);
    }

    public async Task<OrderOperationResult> RefundStandaloneOrderAsync(
        PickleballContext context,
        string orderCodeOrId,
        int refundAmount,
        string paymentMethod,
        string? referenceCode,
        string? reason,
        string? idempotencyKey,
        int? actorAccountId,
        string? actorUsername,
        string? ipAddress = null)
    {
        if (refundAmount <= 0)
        {
            return OrderOperationResult.Fail("Số tiền hoàn phải lớn hơn 0 VNĐ.", 400);
        }

        string cleanIdem = string.IsNullOrWhiteSpace(idempotencyKey)
            ? string.Empty
            : idempotencyKey.Trim()[..Math.Min(idempotencyKey.Trim().Length, 100)];
        if (string.IsNullOrEmpty(cleanIdem))
        {
            return OrderOperationResult.Fail("Thiếu IdempotencyKey.", 400);
        }

        string cleanMethod = (paymentMethod ?? "cash").Trim().ToLowerInvariant() == "transfer" ? "transfer" : "cash";

        bool isRelational = context.Database.IsRelational();
        await using var tx = isRelational ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;

        var order = await FindOrderAsync(context, orderCodeOrId);
        if (order == null || order.BookingId.HasValue)
        {
            if (tx != null) await tx.RollbackAsync();
            return OrderOperationResult.Fail("Không tìm thấy đơn hàng lẻ Pro Shop hợp lệ.", 404);
        }

        string idemScope = "order.refund";
        string idemActor = IdempotencyService.BuildActorKey(actorAccountId, actorUsername);
        string idemTarget = $"ORDER:{order.Code ?? order.OrderId.ToString()}";
        string idemPayload = $"order={order.OrderId}|refund={refundAmount}|method={cleanMethod}";

        var idemCheck = await IdempotencyService.CheckAsync(context, cleanIdem, idemScope, idemActor, idemTarget, idemPayload);
        if (idemCheck.Status == IdempotencyStatus.Conflict)
        {
            if (tx != null) await tx.RollbackAsync();
            return OrderOperationResult.Fail(idemCheck.ConflictMessage!, 409, IdempotencyService.ConflictErrorCode);
        }

        var existingTx = await context.TbPaymentTransactions.FirstOrDefaultAsync(t => t.IdempotencyKey == cleanIdem);
        if (existingTx != null)
        {
            bool matchesRefund =
                existingTx.OrderId == order.OrderId &&
                string.Equals(existingTx.TransactionType, "refund", StringComparison.OrdinalIgnoreCase) &&
                existingTx.Amount == refundAmount &&
                string.Equals(existingTx.PaymentMethod, cleanMethod, StringComparison.OrdinalIgnoreCase);

            if (!matchesRefund)
            {
                if (tx != null) await tx.RollbackAsync();
                return OrderOperationResult.Fail(
                    "Khóa IdempotencyKey đã được sử dụng cho một giao dịch hoàn tiền khác.",
                    409,
                    IdempotencyService.ConflictErrorCode);
            }

            if (tx != null) await tx.CommitAsync();
            return OrderOperationResult.Ok(order, tx: existingTx, replay: true);
        }

        int currentNetPaid = await ComputeOrderLedgerNetPaidAsync(context, order.OrderId);
        if (refundAmount > currentNetPaid)
        {
            if (tx != null) await tx.RollbackAsync();
            return OrderOperationResult.Fail($"Số tiền hoàn ({refundAmount:N0} đ) vượt quá số tiền đã thu ({currentNetPaid:N0} đ).", 422);
        }

        var vnNow = BookingService.GetVietnamNow();
        var refundRow = new TbPaymentTransaction
        {
            OrderId = order.OrderId,
            Amount = refundAmount,
            Currency = "VND",
            TransactionType = "refund",
            PaymentMethod = cleanMethod,
            Status = "completed",
            IdempotencyKey = cleanIdem,
            ReferenceCode = string.IsNullOrWhiteSpace(referenceCode) ? $"ORFD-{vnNow:yyyyMMddHHmmss}" : referenceCode.Trim()[..Math.Min(referenceCode.Trim().Length, 100)],
            Note = string.IsNullOrWhiteSpace(reason) ? $"Hoàn tiền đơn Pro Shop {order.Code}" : reason.Trim()[..Math.Min(reason.Trim().Length, 300)],
            ActorAccountId = actorAccountId,
            RecordedBy = actorUsername,
            CreatedAt = vnNow
        };

        context.TbPaymentTransactions.Add(refundRow);
        await context.SaveChangesAsync();

        string refundHash = IdempotencyService.ComputeCanonicalHash(idemScope, idemActor, idemTarget, idemPayload);
        context.TbIdempotencyRecords.Add(IdempotencyService.CreateRecord(
            cleanIdem, idemScope, idemActor, idemTarget, refundHash, order.Code, refundRow.TransactionId));

        int newNetPaid = await ComputeOrderLedgerNetPaidAsync(context, order.OrderId);
        order.PaidAmount = newNetPaid;
        order.PaymentStatus = newNetPaid == 0 ? "refunded" : BookingService.ComputePaymentStatus(newNetPaid, order.TotalAmount ?? 0);
        order.ModifiedDate = vnNow;
        order.ModifiedBy = actorUsername;

        context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "SHOP_ORDER_REFUNDED",
            EntityType = "Order",
            EntityId = order.Code,
            ActorAccountId = actorAccountId,
            ActorUsername = actorUsername,
            Details = $"Refund={refundAmount}, NetPaidAfter={newNetPaid}",
            IpAddress = ipAddress,
            CreatedAt = vnNow
        });

        await context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return OrderOperationResult.Ok(order, tx: refundRow);
    }

    public async Task<OrderOperationResult> FulfillOrderAsync(
        PickleballContext context,
        string orderCodeOrId,
        int? actorAccountId,
        string? actorUsername,
        string? ipAddress = null)
    {
        var order = await FindOrderAsync(context, orderCodeOrId);
        if (order == null)
        {
            return OrderOperationResult.Fail("Không tìm thấy đơn hàng Pro Shop.", 404);
        }

        if (order.Status is "cancelled" or "rejected")
        {
            return OrderOperationResult.Fail("Không thể đánh dấu giao hàng cho đơn đã hủy.", 409);
        }

        if (string.Equals(order.FulfillmentStatus, "delivered", StringComparison.OrdinalIgnoreCase))
        {
            return OrderOperationResult.Ok(order, replay: true);
        }

        var vnNow = BookingService.GetVietnamNow();
        order.FulfillmentStatus = "delivered";
        order.ModifiedDate = vnNow;
        order.ModifiedBy = actorUsername;

        context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "SHOP_ORDER_FULFILLED",
            EntityType = "Order",
            EntityId = order.Code ?? order.OrderId.ToString(),
            ActorAccountId = actorAccountId,
            ActorUsername = actorUsername,
            Details = "FulfillmentStatus=delivered",
            IpAddress = ipAddress,
            CreatedAt = vnNow
        });

        await context.SaveChangesAsync();
        return OrderOperationResult.Ok(order);
    }

    public async Task<OrderOperationResult> CancelOrderAndRestockAsync(
        PickleballContext context,
        string orderCodeOrId,
        string? reason,
        int? actorAccountId,
        string? actorUsername,
        string? ipAddress = null,
        string? idempotencyKey = null)
    {
        bool isRelational = context.Database.IsRelational();
        await using var tx = isRelational ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;

        bool isNumericId = int.TryParse(orderCodeOrId, out int parsedOrderId);
        var order = await context.TbOrders
            .Include(o => o.TbOrderDetails)
            .FirstOrDefaultAsync(o => o.Code == orderCodeOrId || (isNumericId && o.OrderId == parsedOrderId));

        if (order == null)
        {
            if (tx != null) await tx.RollbackAsync();
            return OrderOperationResult.Fail("Không tìm thấy đơn hàng Pro Shop.", 404);
        }

        string cleanIdem = IdempotencyService.NormalizeKey(idempotencyKey) ?? string.Empty;
        string idemScope = "order.cancel";
        string idemActor = IdempotencyService.BuildActorKey(actorAccountId, actorUsername);
        string idemTarget = $"ORDER:{order.Code ?? order.OrderId.ToString()}";
        string idemPayload = $"order={order.OrderId}|reason={(reason ?? "").Trim().ToLowerInvariant()}";

        if (!string.IsNullOrEmpty(cleanIdem))
        {
            var idemCheck = await IdempotencyService.CheckAsync(context, cleanIdem, idemScope, idemActor, idemTarget, idemPayload);
            if (idemCheck.Status == IdempotencyStatus.Conflict)
            {
                if (tx != null) await tx.RollbackAsync();
                return OrderOperationResult.Fail(idemCheck.ConflictMessage!, 409, IdempotencyService.ConflictErrorCode);
            }
            if (idemCheck.Status == IdempotencyStatus.Replay)
            {
                if (tx != null) await tx.CommitAsync();
                return OrderOperationResult.Ok(order, replay: true);
            }
        }

        if (order.Status == "cancelled")
        {
            if (tx != null) await tx.CommitAsync();
            return OrderOperationResult.Ok(order, replay: true);
        }

        if (string.Equals(order.FulfillmentStatus, "delivered", StringComparison.OrdinalIgnoreCase))
        {
            if (tx != null) await tx.RollbackAsync();
            return OrderOperationResult.Fail("Đơn hàng đã giao thực tế cho khách, không thể tự động hủy hoàn kho.", 409, "ORDER_ALREADY_DELIVERED");
        }

        if (order.BookingId.HasValue)
        {
            if (tx != null) await tx.RollbackAsync();
            return OrderOperationResult.Fail("Đơn đã gộp vào hóa đơn sân, vui lòng điều chỉnh món trên màn hình POS của sân hoặc hủy trực tiếp ca đặt sân.", 409);
        }

        int netPaid = await ComputeOrderLedgerNetPaidAsync(context, order.OrderId);
        if (netPaid > 0)
        {
            if (tx != null) await tx.RollbackAsync();
            return OrderOperationResult.Fail("Đơn hàng đã thu tiền. Vui lòng thực hiện giao dịch Hoàn tiền (Refund) trước khi hủy và hoàn kho.", 409, "ORDER_HAS_PAYMENT");
        }

        var vnNow = BookingService.GetVietnamNow();
        foreach (var detail in order.TbOrderDetails.OrderBy(d => d.ProductId))
        {
            if (!detail.ProductId.HasValue || (detail.Quantity ?? 0) <= 0) continue;
            int qty = detail.Quantity!.Value;
            int prodId = detail.ProductId.Value;

            int balanceAfter;
            if (isRelational)
            {
                await context.Database.ExecuteSqlRawAsync(@"
UPDATE tb_Product
SET UnitInStock = COALESCE(UnitInStock, Quantity, 0) + {1},
    Quantity = COALESCE(UnitInStock, Quantity, 0) + {1}
WHERE ProductId = {0}",
                    prodId, qty);
                var p = await context.TbProducts.AsNoTracking().FirstAsync(x => x.ProductId == prodId);
                balanceAfter = p.UnitInStock ?? p.Quantity ?? 0;
            }
            else
            {
                var p = await context.TbProducts.FindAsync(prodId);
                balanceAfter = (p?.UnitInStock ?? p?.Quantity ?? 0) + qty;
                if (p != null)
                {
                    p.UnitInStock = balanceAfter;
                    p.Quantity = balanceAfter;
                }
            }

            context.TbInventoryMovements.Add(new TbInventoryMovement
            {
                ProductId = prodId,
                OrderId = order.OrderId,
                ChangeQty = qty,
                BalanceAfter = balanceAfter,
                Reason = $"Restock cancelled order {order.Code}",
                ActorAccountId = actorAccountId,
                CreatedAt = vnNow
            });
        }

        order.Status = "cancelled";
        order.FulfillmentStatus = "cancelled";
        order.OrderStatusId = 5;
        order.ModifiedDate = vnNow;
        order.ModifiedBy = actorUsername;

        if (!string.IsNullOrEmpty(cleanIdem))
        {
            string reqHash = IdempotencyService.ComputeCanonicalHash(idemScope, idemActor, idemTarget, idemPayload);
            context.TbIdempotencyRecords.Add(IdempotencyService.CreateRecord(
                cleanIdem, idemScope, idemActor, idemTarget, reqHash, order.Code, order.OrderId));
        }

        context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "SHOP_ORDER_CANCELLED_RESTOCKED",
            EntityType = "Order",
            EntityId = order.Code,
            ActorAccountId = actorAccountId,
            ActorUsername = actorUsername,
            Details = string.IsNullOrWhiteSpace(reason) ? "Cancelled & restocked" : reason,
            IpAddress = ipAddress,
            CreatedAt = vnNow
        });

        await context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return OrderOperationResult.Ok(order);
    }

    public static async Task<int> ComputeOrderLedgerNetPaidAsync(PickleballContext context, int orderId)
    {
        var rows = await context.TbPaymentTransactions
            .AsNoTracking()
            .Where(t => t.OrderId == orderId && t.Status == "completed")
            .Select(t => new { t.TransactionType, t.Amount })
            .ToListAsync();

        int net = 0;
        foreach (var r in rows)
        {
            if (string.Equals(r.TransactionType, "refund", StringComparison.OrdinalIgnoreCase))
                net -= Math.Abs(r.Amount);
            else
                net += Math.Max(0, r.Amount);
        }

        return Math.Max(0, net);
    }

    public static async Task<TbOrder?> FindOrderAsync(PickleballContext context, string codeOrId)
    {
        if (string.IsNullOrWhiteSpace(codeOrId)) return null;
        string clean = codeOrId.Trim();
        if (int.TryParse(clean, out int id))
        {
            return await context.TbOrders.FirstOrDefaultAsync(o => o.OrderId == id);
        }
        string upper = clean.ToUpperInvariant();
        return await context.TbOrders.FirstOrDefaultAsync(o => o.Code == upper);
    }
}
