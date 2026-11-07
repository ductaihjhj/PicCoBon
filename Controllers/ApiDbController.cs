using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Controllers
{
    [ApiController]
    public class ApiDbController : ControllerBase
    {
        private readonly PickleballContext _context;

        public ApiDbController(PickleballContext context)
        {
            _context = context;
        }

        private static string FormatIconHtml(string? rawIcon)
        {
            if (string.IsNullOrWhiteSpace(rawIcon))
                return "<iconify-icon icon=\"mdi:cup-water\" width=\"26\"></iconify-icon>";
            string trimmed = rawIcon.Trim();
            if (trimmed.StartsWith("<"))
                return trimmed;
            return $"<iconify-icon icon=\"{trimmed}\" width=\"26\"></iconify-icon>";
        }

        private async Task ReconcileBookingsByRealTimeAsync()
        {
            var now = DateTime.Now;
            string todayIso = now.ToString("yyyy-MM-dd");
            int nowMins = now.Hour * 60 + now.Minute;

            var activeBookings = await _context.TbBookings
                .Where(b => b.Status == "approved" || b.Status == "playing")
                .ToListAsync();

            bool changed = false;
            foreach (var b in activeBookings)
            {
                string bDate = string.IsNullOrWhiteSpace(b.BookingDate) ? todayIso : b.BookingDate.Trim();
                int startMins = 17 * 60;
                if (!string.IsNullOrWhiteSpace(b.StartTime) && b.StartTime.Contains(':'))
                {
                    var parts = b.StartTime.Split(':');
                    if (parts.Length >= 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m))
                    {
                        startMins = h * 60 + m;
                    }
                }
                int dur = b.Duration > 0 ? b.Duration : 60;
                int endMins = startMins + dur;

                int dateCmp = string.CompareOrdinal(bDate, todayIso);
                if (dateCmp < 0)
                {
                    // Ngày trong quá khứ -> tự động hoàn thành nếu đang chơi
                    if (b.Status == "playing")
                    {
                        b.Status = "completed";
                        if (b.TotalPaid <= 0) b.TotalPaid = b.CourtFee;
                        changed = true;
                    }
                }
                else if (dateCmp > 0)
                {
                    // Ngày trong tương lai -> chưa thể ở trạng thái đang thi đấu
                    if (b.Status == "playing")
                    {
                        b.Status = "approved";
                        changed = true;
                    }
                }
                else
                {
                    // Hôm nay (bDate == todayIso)
                    if (nowMins >= endMins)
                    {
                        // Đã hết giờ ca đấu
                        if (b.Status == "playing")
                        {
                            b.Status = "completed";
                            if (b.TotalPaid <= 0) b.TotalPaid = b.CourtFee;
                            changed = true;
                        }
                    }
                    else if (nowMins >= startMins && nowMins < endMins)
                    {
                        // Đang nằm trong khung giờ thi đấu của đơn đã duyệt
                        if (b.Status == "approved")
                        {
                            b.Status = "playing";
                            changed = true;
                        }
                    }
                    else if (nowMins < startMins - 15)
                    {
                        // Chưa tới giờ thi đấu (còn hơn 15 phút nữa mới bắt đầu) -> giữ ở Đã duyệt (Chờ tới giờ)
                        if (b.Status == "playing")
                        {
                            b.Status = "approved";
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
            {
                await _context.SaveChangesAsync();
            }
        }

        [HttpGet("/api/db/state")]
        public async Task<IActionResult> GetState()
        {
            await ReconcileBookingsByRealTimeAsync();

            var courts = await _context.TbCourts
                .Where(c => c.IsActive)
                .OrderBy(c => c.CourtId)
                .Select(c => new
                {
                    id = c.CourtId,
                    name = c.CourtName,
                    type = c.CourtType,
                    typeLabel = c.TypeLabel,
                    surface = c.Surface,
                    surcharge = c.Surcharge,
                    basePrice = c.BasePrice,
                    imageUrl = c.Image,
                    description = c.Description
                })
                .ToListAsync();

            var rawBookings = await _context.TbBookings
                .OrderByDescending(b => b.BookingDate)
                .ThenBy(b => b.StartTime)
                .ToListAsync();

            var bookings = rawBookings.Select(b =>
            {
                object posItemsObj;
                try
                {
                    posItemsObj = JsonSerializer.Deserialize<object>(string.IsNullOrWhiteSpace(b.PosItemsJson) ? "[]" : b.PosItemsJson) ?? Array.Empty<object>();
                }
                catch
                {
                    posItemsObj = Array.Empty<object>();
                }

                return new
                {
                    id = b.BookingCode,
                    courtId = b.CourtId,
                    userId = b.AccountId,
                    date = b.BookingDate,
                    name = b.CustomerName,
                    phone = b.Phone,
                    time = b.StartTime,
                    duration = b.Duration,
                    status = b.Status,
                    players = b.Players,
                    skillLevel = b.SkillLevel,
                    courtFee = b.CourtFee,
                    posItems = posItemsObj,
                    totalPaid = b.TotalPaid
                };
            }).ToList();

            var courtLocks = await _context.TbCourtLocks
                .OrderByDescending(l => l.LockDate)
                .Select(l => new
                {
                    id = l.LockCode,
                    courtId = l.CourtId,
                    date = l.LockDate,
                    startTime = l.StartTime,
                    duration = l.Duration,
                    reason = l.Reason
                })
                .ToListAsync();

            var rawProducts = await _context.TbProducts
                .Include(p => p.CategoryProduct)
                .Where(p => p.IsActive)
                .OrderBy(p => p.ProductId)
                .ToListAsync();

            var menu = rawProducts.Select(p => new
            {
                id = p.ProductId,
                name = p.Title,
                price = p.Price ?? 0,
                type = p.CategoryProduct != null && !string.IsNullOrEmpty(p.CategoryProduct.Alias) ? p.CategoryProduct.Alias : "drink",
                icon = FormatIconHtml(p.Icon)
            }).ToList();

            var cmsRows = await _context.TbCmsConfigs.ToListAsync();
            var cmsConfig = new Dictionary<string, object>();
            foreach (var row in cmsRows)
            {
                if (int.TryParse(row.ConfigValue, out int numVal))
                    cmsConfig[row.ConfigKey] = numVal;
                else
                    cmsConfig[row.ConfigKey] = row.ConfigValue;
            }

            return Ok(new
            {
                ok = true,
                courts,
                bookings,
                courtLocks,
                menu,
                cmsConfig
            });
        }

        [HttpPost("/api/db/sync")]
        public async Task<IActionResult> SyncState([FromBody] JsonElement payload)
        {
            bool clearAll = payload.TryGetProperty("clearAll", out JsonElement clearEl) && clearEl.ValueKind == JsonValueKind.True;

            if (payload.TryGetProperty("bookings", out JsonElement bookingsEl) && bookingsEl.ValueKind == JsonValueKind.Array)
            {
                var existing = await _context.TbBookings.ToListAsync();
                if (clearAll)
                {
                    _context.TbBookings.RemoveRange(existing);
                    await _context.SaveChangesAsync();
                    existing.Clear();
                }

                foreach (var item in bookingsEl.EnumerateArray())
                {
                    string code = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? ("PB-" + Guid.NewGuid().ToString("N")[..6].ToUpper()) : "PB-0001";
                    int courtId = item.TryGetProperty("courtId", out var cProp) && cProp.TryGetInt32(out int cid) ? cid : 1;
                    int? userId = item.TryGetProperty("userId", out var uProp) && uProp.ValueKind == JsonValueKind.Number && uProp.TryGetInt32(out int uid) && uid > 0
                        ? uid
                        : (Function._AccountId > 0 ? Function._AccountId : null);
                    string date = item.TryGetProperty("date", out var dProp) ? dProp.GetString() ?? DateTime.Now.ToString("yyyy-MM-dd") : DateTime.Now.ToString("yyyy-MM-dd");
                    string name = item.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "Khách hàng" : "Khách hàng";
                    string phone = item.TryGetProperty("phone", out var pProp) ? pProp.GetString() ?? "" : "";
                    string time = item.TryGetProperty("time", out var tProp) ? tProp.GetString() ?? "17:00" : "17:00";
                    int duration = item.TryGetProperty("duration", out var durProp) && durProp.TryGetInt32(out int dur) ? dur : 60;
                    string status = item.TryGetProperty("status", out var sProp) ? sProp.GetString() ?? "pending" : "pending";
                    int players = item.TryGetProperty("players", out var plProp) && plProp.TryGetInt32(out int pl) ? pl : 4;
                    string skill = item.TryGetProperty("skillLevel", out var skProp) ? skProp.GetString() ?? "Phong trào" : "Phong trào";
                    int courtFee = item.TryGetProperty("courtFee", out var cfProp) && cfProp.TryGetInt32(out int cf) ? cf : 0;
                    int totalPaid = item.TryGetProperty("totalPaid", out var tpProp) && tpProp.TryGetInt32(out int tp) ? tp : courtFee;
                    string posJson = item.TryGetProperty("posItems", out var posProp) ? posProp.GetRawText() : "[]";

                    var existingBooking = existing.FirstOrDefault(b => b.BookingCode == code);
                    if (existingBooking != null)
                    {
                        existingBooking.CourtId = courtId;
                        existingBooking.CustomerName = name;
                        existingBooking.Phone = phone;
                        existingBooking.BookingDate = date;
                        existingBooking.StartTime = time;
                        existingBooking.Duration = duration;
                        existingBooking.Status = status;
                        existingBooking.Players = players;
                        existingBooking.SkillLevel = skill;
                        existingBooking.CourtFee = courtFee;
                        existingBooking.TotalPaid = totalPaid;
                        existingBooking.PosItemsJson = posJson;
                        if (existingBooking.AccountId == null && userId != null)
                        {
                            existingBooking.AccountId = userId;
                        }
                    }
                    else
                    {
                        _context.TbBookings.Add(new TbBooking
                        {
                            BookingCode = code,
                            CourtId = courtId,
                            AccountId = userId,
                            CustomerName = name,
                            Phone = phone,
                            BookingDate = date,
                            StartTime = time,
                            Duration = duration,
                            Status = status,
                            Players = players,
                            SkillLevel = skill,
                            CourtFee = courtFee,
                            TotalPaid = totalPaid,
                            PosItemsJson = posJson,
                            CreatedDate = DateTime.Now
                        });
                    }
                }
                await _context.SaveChangesAsync();
            }

            if (payload.TryGetProperty("courtLocks", out JsonElement locksEl) && locksEl.ValueKind == JsonValueKind.Array)
            {
                var existingLocks = await _context.TbCourtLocks.ToListAsync();
                _context.TbCourtLocks.RemoveRange(existingLocks);
                await _context.SaveChangesAsync();

                foreach (var item in locksEl.EnumerateArray())
                {
                    string code = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? ("LK-" + Guid.NewGuid().ToString("N")[..6]) : "LK-01";
                    int courtId = item.TryGetProperty("courtId", out var cProp) && cProp.TryGetInt32(out int cid) ? cid : 1;
                    string date = item.TryGetProperty("date", out var dProp) ? dProp.GetString() ?? DateTime.Now.ToString("yyyy-MM-dd") : DateTime.Now.ToString("yyyy-MM-dd");
                    string startTime = item.TryGetProperty("startTime", out var stProp) ? stProp.GetString() ?? "08:00" : "08:00";
                    int duration = item.TryGetProperty("duration", out var durProp) && durProp.TryGetInt32(out int dur) ? dur : 60;
                    string reason = item.TryGetProperty("reason", out var rProp) ? rProp.GetString() ?? "Bảo trì" : "Bảo trì";

                    _context.TbCourtLocks.Add(new TbCourtLock
                    {
                        LockCode = code,
                        CourtId = courtId,
                        LockDate = date,
                        StartTime = startTime,
                        Duration = duration,
                        Reason = reason,
                        CreatedDate = DateTime.Now
                    });
                }
                await _context.SaveChangesAsync();
            }

            if (payload.TryGetProperty("menu", out JsonElement menuEl) && menuEl.ValueKind == JsonValueKind.Array)
            {
                var existingProducts = await _context.TbProducts.ToListAsync();
                var incomingIds = new HashSet<int>();

                foreach (var item in menuEl.EnumerateArray())
                {
                    int prodId = item.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out int pid) ? pid : 0;
                    string title = item.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "Sản phẩm POS" : "Sản phẩm POS";
                    int price = item.TryGetProperty("price", out var prProp) && prProp.TryGetInt32(out int pr) ? pr : 20000;
                    string typeAlias = item.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "drink" : "drink";
                    string icon = item.TryGetProperty("icon", out var icProp) ? icProp.GetString() ?? "mdi:cup-water" : "mdi:cup-water";
                    int catId = typeAlias == "food" ? 2 : (typeAlias == "other" ? 3 : 1);

                    var existingProd = existingProducts.FirstOrDefault(p => p.ProductId == prodId);
                    if (existingProd != null)
                    {
                        incomingIds.Add(existingProd.ProductId);
                        existingProd.Title = title;
                        existingProd.Price = price;
                        existingProd.CategoryProductId = catId;
                        existingProd.Icon = FormatIconHtml(icon);
                    }
                    else
                    {
                        var newProd = new TbProduct
                        {
                            Title = title,
                            Alias = Function.TitleSlugGenerationAlias(title),
                            CategoryProductId = catId,
                            Price = price,
                            PriceSale = price,
                            Icon = FormatIconHtml(icon),
                            Quantity = 100,
                            UnitInStock = 100,
                            IsActive = true,
                            CreatedDate = DateTime.Now
                        };
                        _context.TbProducts.Add(newProd);
                    }
                }

                foreach (var oldProd in existingProducts)
                {
                    if (!incomingIds.Contains(oldProd.ProductId) && menuEl.GetArrayLength() > 0)
                    {
                        _context.TbProducts.Remove(oldProd);
                    }
                }
                await _context.SaveChangesAsync();
            }

            if (payload.TryGetProperty("cmsConfig", out JsonElement cmsEl) && cmsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in cmsEl.EnumerateObject())
                {
                    string valStr = prop.Value.ValueKind == JsonValueKind.String ? (prop.Value.GetString() ?? "") : prop.Value.GetRawText();
                    var existingCfg = await _context.TbCmsConfigs.FindAsync(prop.Name);
                    if (existingCfg != null)
                    {
                        existingCfg.ConfigValue = valStr;
                    }
                    else
                    {
                        _context.TbCmsConfigs.Add(new TbCmsConfig
                        {
                            ConfigKey = prop.Name,
                            ConfigValue = valStr
                        });
                    }
                }
                await _context.SaveChangesAsync();
            }

            return await GetState();
        }

        [HttpPost("/api/auth/login")]
        public async Task<IActionResult> LoginApi([FromBody] JsonElement payload)
        {
            string identifier = payload.TryGetProperty("identifier", out var idProp) ? (idProp.GetString() ?? "").Trim() : "";
            string password = payload.TryGetProperty("password", out var pwProp) ? (pwProp.GetString() ?? "").Trim() : "";

            if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(password))
            {
                return BadRequest(new { ok = false, error = "Vui lòng nhập Email/Số điện thoại và mật khẩu." });
            }

            string md5Pass = HashMD5.GetMD5(password);
            string lowerId = identifier.ToLower();

            var account = await _context.TbAccounts
                .Include(a => a.Role)
                .FirstOrDefaultAsync(a =>
                    a.IsActive &&
                    ((a.Email != null && a.Email.ToLower() == lowerId) ||
                     (a.Phone != null && a.Phone == identifier) ||
                     (a.Username != null && a.Username.ToLower() == lowerId)) &&
                    (a.Password == md5Pass || a.Password == password));

            if (account == null)
            {
                return Unauthorized(new { ok = false, error = "Email/Số điện thoại hoặc mật khẩu không chính xác." });
            }

            Function._AccountId = account.AccountId;
            Function._Username = account.Username ?? account.Email ?? account.Phone ?? "";
            Function._FullName = account.FullName ?? "";
            Function._Email = account.Email ?? "";
            Function._Phone = account.Phone ?? "";
            Function._Avatar = account.Avatar ?? "";
            Function._SkillLevel = account.SkillLevel ?? "";
            Function._RoleId = account.RoleId ?? 2;
            Function._Message = string.Empty;

            Response.Cookies.Append("pb_admin_uid", account.AccountId.ToString(), new CookieOptions
            {
                HttpOnly = false,
                Expires = DateTimeOffset.UtcNow.AddDays(7),
                Path = "/"
            });

            bool isAdmin = account.RoleId == 1;
            string defaultAvatar = isAdmin
                ? "https://images.unsplash.com/photo-1472099645785-5658abf4ff4e?auto=format&fit=crop&w=120&q=80"
                : "https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?auto=format&fit=crop&w=120&q=80";

            return Ok(new
            {
                ok = true,
                user = new
                {
                    id = account.AccountId,
                    name = account.FullName ?? account.Username ?? "Hội viên",
                    email = account.Email ?? "",
                    phone = account.Phone ?? "",
                    skillLevel = account.SkillLevel ?? "",
                    role = isAdmin ? "admin" : "user",
                    roleLabel = isAdmin ? "Ban Quản Trị CLB" : "Hội viên CLB",
                    avatar = !string.IsNullOrEmpty(account.Avatar) ? account.Avatar : defaultAvatar
                }
            });
        }

        [HttpPost("/api/auth/register")]
        public async Task<IActionResult> RegisterApi([FromBody] JsonElement payload)
        {
            string name = payload.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? "").Trim() : "";
            string phone = payload.TryGetProperty("phone", out var pProp) ? (pProp.GetString() ?? "").Trim() : "";
            string email = payload.TryGetProperty("email", out var eProp) ? (eProp.GetString() ?? "").Trim().ToLower() : "";
            string password = payload.TryGetProperty("password", out var pwProp) ? (pwProp.GetString() ?? "").Trim() : "";

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(phone) || string.IsNullOrEmpty(password))
            {
                return BadRequest(new { ok = false, error = "Vui lòng nhập đầy đủ Họ tên, Số điện thoại và Mật khẩu." });
            }

            bool exists = await _context.TbAccounts.AnyAsync(a =>
                (a.Phone != null && a.Phone == phone) ||
                (!string.IsNullOrEmpty(email) && a.Email != null && a.Email.ToLower() == email));

            if (exists)
            {
                return Conflict(new { ok = false, error = "Số điện thoại hoặc Email này đã được đăng ký." });
            }

            var newAccount = new TbAccount
            {
                Username = !string.IsNullOrEmpty(email) ? email.Split('@')[0] : phone,
                FullName = name,
                Phone = phone,
                Email = email,
                Password = HashMD5.GetMD5(password),
                RoleId = 2,
                IsActive = true
            };

            _context.TbAccounts.Add(newAccount);
            await _context.SaveChangesAsync();

            Function._AccountId = newAccount.AccountId;
            Function._Username = newAccount.Username ?? "";
            Function._FullName = newAccount.FullName ?? "";
            Function._Email = newAccount.Email ?? "";
            Function._Phone = newAccount.Phone ?? "";
            Function._RoleId = 2;

            Response.Cookies.Append("pb_admin_uid", newAccount.AccountId.ToString(), new CookieOptions
            {
                HttpOnly = false,
                Expires = DateTimeOffset.UtcNow.AddDays(7),
                Path = "/"
            });

            return Ok(new
            {
                ok = true,
                user = new
                {
                    id = newAccount.AccountId,
                    name = newAccount.FullName,
                    email = newAccount.Email,
                    phone = newAccount.Phone,
                    role = "user",
                    roleLabel = "Hội viên CLB",
                    avatar = "https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?auto=format&fit=crop&w=120&q=80"
                }
            });
        }

        [HttpPost("/api/orders/create")]
        public async Task<IActionResult> CreateOrderApi([FromBody] JsonElement payload)
        {
            string customerName = payload.TryGetProperty("customerName", out var nProp) ? (nProp.GetString() ?? "").Trim() : "";
            string phone = payload.TryGetProperty("phone", out var pProp) ? (pProp.GetString() ?? "").Trim() : "";
            string address = payload.TryGetProperty("address", out var aProp) ? (aProp.GetString() ?? "").Trim() : "Nhận tại quầy Pro Shop";
            string bookingCode = payload.TryGetProperty("bookingCode", out var bProp) ? (bProp.GetString() ?? "").Trim() : "";

            if (string.IsNullOrEmpty(customerName) || string.IsNullOrEmpty(phone))
            {
                return BadRequest(new { ok = false, error = "Vui lòng nhập Họ tên và Số điện thoại người nhận." });
            }

            if (!payload.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array || itemsEl.GetArrayLength() == 0)
            {
                return BadRequest(new { ok = false, error = "Giỏ hàng đang trống." });
            }

            string orderCode = "ORD-" + DateTime.Now.ToString("MMdd") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpper();
            int totalQty = 0;
            int totalAmount = 0;
            var detailRows = new List<(int prodId, string name, int price, int qty)>();

            foreach (var item in itemsEl.EnumerateArray())
            {
                int prodId = item.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int pid) ? pid : 0;
                string name = item.TryGetProperty("name", out var nmEl) ? (nmEl.GetString() ?? "Sản phẩm") : "Sản phẩm";
                int price = item.TryGetProperty("price", out var prEl) && prEl.TryGetInt32(out int pr) ? pr : 0;
                int qty = item.TryGetProperty("qty", out var qEl) && qEl.TryGetInt32(out int q) && q > 0 ? q : 1;

                totalQty += qty;
                totalAmount += price * qty;
                detailRows.Add((prodId, name, price, qty));
            }

            var newOrder = new TbOrder
            {
                Code = orderCode,
                CustomerName = customerName,
                Phone = phone,
                Address = !string.IsNullOrEmpty(bookingCode) ? $"Giao tận sân (Đơn {bookingCode}) - {address}" : address,
                TotalAmount = totalAmount,
                Quanlity = totalQty,
                OrderStatusId = 2,
                CreatedDate = DateTime.Now,
                CreatedBy = customerName
            };

            _context.TbOrders.Add(newOrder);
            await _context.SaveChangesAsync();

            var validProdIds = await _context.TbProducts.Select(p => p.ProductId).ToListAsync();
            foreach (var d in detailRows)
            {
                int? fkProdId = validProdIds.Contains(d.prodId) ? d.prodId : validProdIds.FirstOrDefault();
                _context.TbOrderDetails.Add(new TbOrderDetail
                {
                    OrderId = newOrder.OrderId,
                    ProductId = fkProdId,
                    Price = d.price,
                    Quantity = d.qty
                });
            }

            // Nếu khách chọn gộp vào ca đặt sân đang có, cộng trực tiếp vào PosItemsJson và TotalPaid của đơn sân đó
            if (!string.IsNullOrEmpty(bookingCode))
            {
                var booking = await _context.TbBookings.FirstOrDefaultAsync(b => b.BookingCode == bookingCode);
                if (booking != null)
                {
                    var currentPos = new List<Dictionary<string, object>>();
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(booking.PosItemsJson))
                        {
                            var parsed = JsonSerializer.Deserialize<List<JsonElement>>(booking.PosItemsJson);
                            if (parsed != null)
                            {
                                foreach (var el in parsed)
                                {
                                    int eid = el.TryGetProperty("id", out var xid) && xid.TryGetInt32(out int xi) ? xi : 0;
                                    string ename = el.TryGetProperty("name", out var xnm) ? (xnm.GetString() ?? "") : "";
                                    int eprice = el.TryGetProperty("price", out var xpr) && xpr.TryGetInt32(out int xp) ? xp : 0;
                                    int eqty = el.TryGetProperty("qty", out var xq) && xq.TryGetInt32(out int xqt) ? xqt : 1;
                                    currentPos.Add(new Dictionary<string, object>
                                    {
                                        ["id"] = eid,
                                        ["name"] = ename,
                                        ["price"] = eprice,
                                        ["qty"] = eqty
                                    });
                                }
                            }
                        }
                    }
                    catch { }

                    foreach (var d in detailRows)
                    {
                        var existingItem = currentPos.FirstOrDefault(x => Convert.ToInt32(x["id"]) == d.prodId || x["name"].ToString() == d.name);
                        if (existingItem != null)
                        {
                            existingItem["qty"] = Convert.ToInt32(existingItem["qty"]) + d.qty;
                        }
                        else
                        {
                            currentPos.Add(new Dictionary<string, object>
                            {
                                ["id"] = d.prodId,
                                ["name"] = d.name,
                                ["price"] = d.price,
                                ["qty"] = d.qty
                            });
                        }
                    }

                    int posSum = currentPos.Sum(x => Convert.ToInt32(x["price"]) * Convert.ToInt32(x["qty"]));
                    booking.PosItemsJson = JsonSerializer.Serialize(currentPos);
                    booking.TotalPaid = booking.CourtFee + posSum;
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                ok = true,
                orderCode,
                totalAmount,
                bookingCode
            });
        }
    }
}
