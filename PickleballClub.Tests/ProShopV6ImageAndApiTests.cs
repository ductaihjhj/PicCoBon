using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using PickleballClub.Areas.Admin.Controllers;
using PickleballClub.Models;
using PickleballClub.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace PickleballClub.Tests;

public class ProShopV6ImageAndApiTests : IClassFixture<SpikeWebAppFactory>, IDisposable
{
    private readonly SpikeWebAppFactory _factory;
    private readonly string _tempWebRoot;

    public ProShopV6ImageAndApiTests(SpikeWebAppFactory factory)
    {
        _factory = factory;
        _tempWebRoot = Path.Combine(Path.GetTempPath(), $"spike-proshop-v6-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempWebRoot, "uploads", "products"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempWebRoot))
            {
                Directory.Delete(_tempWebRoot, recursive: true);
            }
        }
        catch
        {
            // Ignore temp cleanup failure
        }
    }

    private sealed class TempWebHostEnv : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "PickleballClub";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
    }

    private ProductImageService CreateIsolatedImageService()
    {
        return new ProductImageService(new TempWebHostEnv
        {
            WebRootPath = _tempWebRoot,
            ContentRootPath = _tempWebRoot
        });
    }

    private static IFormFile CreateFormFile(byte[] bytes, string fileName, string contentType)
    {
        var ms = new MemoryStream(bytes);
        return new FormFile(ms, 0, bytes.Length, "ImageFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static byte[] GenerateTestImageBytes(int width, int height, string format, bool withTransparency = false)
    {
        using var img = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte alpha = (withTransparency && x < width / 3) ? (byte)0 : (byte)255;
                img[x, y] = new Rgba32(255, 85, 32, alpha);
            }
        }

        using var ms = new MemoryStream();
        switch (format.ToLowerInvariant())
        {
            case "png":
                img.Save(ms, new PngEncoder());
                break;
            case "jpg":
            case "jpeg":
                img.Save(ms, new JpegEncoder { Quality = 90 });
                break;
            case "webp":
                img.Save(ms, new WebpEncoder { Quality = 90, TransparentColorMode = WebpTransparentColorMode.Preserve });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task UploadValidPngJpgWebp_ResizesAndPreservesAlpha()
    {
        var svc = CreateIsolatedImageService();

        // 1. Transparent PNG larger than 1200px -> resized to 1200px max & preserves alpha
        var pngBytes = GenerateTestImageBytes(1500, 1000, "png", withTransparency: true);
        var pngRes = await svc.SaveValidatedProductImageAsync(CreateFormFile(pngBytes, "paddle-transparent.png", "image/png"));
        Assert.True(pngRes.Success, pngRes.ErrorMessage);
        Assert.NotNull(pngRes.RelativePath);
        Assert.StartsWith("/uploads/products/", pngRes.RelativePath);
        Assert.EndsWith(".webp", pngRes.RelativePath);
        Assert.Equal(1200, pngRes.Width);
        Assert.Equal(800, pngRes.Height);

        var savedPhysical = Path.Combine(_tempWebRoot, pngRes.RelativePath!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(savedPhysical));
        using (var loaded = await Image.LoadAsync<Rgba32>(savedPhysical))
        {
            // Verify left transparent zone still has alpha == 0
            Assert.Equal(0, loaded[10, 10].A);
        }

        // 2. Valid JPEG
        var jpgBytes = GenerateTestImageBytes(400, 400, "jpg");
        var jpgRes = await svc.SaveValidatedProductImageAsync(CreateFormFile(jpgBytes, "drink.jpg", "image/jpeg"));
        Assert.True(jpgRes.Success, jpgRes.ErrorMessage);

        // 3. Valid WebP
        var webpBytes = GenerateTestImageBytes(500, 500, "webp");
        var webpRes = await svc.SaveValidatedProductImageAsync(CreateFormFile(webpBytes, "snack.webp", "image/webp"));
        Assert.True(webpRes.Success, webpRes.ErrorMessage);
    }

    [Fact]
    public async Task RejectInvalidExtensionMimeFakeBytesAndOversizedFiles()
    {
        var svc = CreateIsolatedImageService();

        // 1. SVG / HTML extension rejected
        var svgBytes = Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>");
        var svgRes = await svc.SaveValidatedProductImageAsync(CreateFormFile(svgBytes, "xss.svg", "image/svg+xml"));
        Assert.False(svgRes.Success);

        // 2. Fake image: HTML/script renamed to .png with image/png Content-Type
        var fakePngBytes = Encoding.UTF8.GetBytes("<html><body><script>alert('xss')</script></body></html>");
        var fakePngRes = await svc.SaveValidatedProductImageAsync(CreateFormFile(fakePngBytes, "fake-image.png", "image/png"));
        Assert.False(fakePngRes.Success);

        // 3. Path traversal filename rejected
        var validPng = GenerateTestImageBytes(100, 100, "png");
        var traversalRes = await svc.SaveValidatedProductImageAsync(CreateFormFile(validPng, "../../appsettings.png", "image/png"));
        Assert.False(traversalRes.Success);

        // 4. Oversized file (> 5 MB) rejected
        var oversizedBytes = new byte[5 * 1024 * 1024 + 1024];
        var oversizedRes = await svc.SaveValidatedProductImageAsync(CreateFormFile(oversizedBytes, "huge.png", "image/png"));
        Assert.False(oversizedRes.Success);
        Assert.Contains("5 MB", oversizedRes.ErrorMessage);
    }

    [Fact]
    public async Task EditLifecycle_ReplaceKeepAndRemoveImage_CleansUpUnreferencedFilesOnly()
    {
        var svc = CreateIsolatedImageService();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();

        var category = await db.TbProductCategories.AsNoTracking().FirstAsync();
        var controller = new ProductsController(db, svc);

        // 1. Create product with initial image
        var img1Bytes = GenerateTestImageBytes(300, 300, "png");
        var createVm = new ProductFormViewModel
        {
            Title = "Test ProShop V6 Product",
            CategoryProductId = category.CategoryProductId,
            Price = 35000,
            UnitInStock = 25,
            Quantity = 25,
            IsActive = true,
            ImageFile = CreateFormFile(img1Bytes, "first.png", "image/png")
        };

        var createResult = await controller.Create(createVm);
        Assert.IsType<RedirectToActionResult>(createResult);

        var created = await db.TbProducts.AsNoTracking().OrderByDescending(p => p.ProductId).FirstAsync(p => p.Title == "Test ProShop V6 Product");
        Assert.NotNull(created.Image);
        var firstRelPath = created.Image!;
        var firstPhysical = Path.Combine(_tempWebRoot, firstRelPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(firstPhysical));

        try
        {
            // 2. Edit product WITHOUT uploading new file and RemoveImage = false -> keeps existing image & file
            var editKeepVm = ProductFormViewModel.FromEntity(created);
            editKeepVm.Title = "Test ProShop V6 Product Updated";
            editKeepVm.Price = 40000;
            editKeepVm.ImageFile = null;
            editKeepVm.RemoveImage = false;

            var keepResult = await controller.Edit(created.ProductId, editKeepVm);
            Assert.IsType<RedirectToActionResult>(keepResult);

            var afterKeep = await db.TbProducts.AsNoTracking().FirstAsync(p => p.ProductId == created.ProductId);
            Assert.Equal(firstRelPath, afterKeep.Image);
            Assert.True(File.Exists(firstPhysical));

            // 3. Edit product WITH a replacement image -> deletes old file and updates DB to new file
            var img2Bytes = GenerateTestImageBytes(320, 320, "jpg");
            var editReplaceVm = ProductFormViewModel.FromEntity(afterKeep);
            editReplaceVm.ImageFile = CreateFormFile(img2Bytes, "second.jpg", "image/jpeg");
            editReplaceVm.RemoveImage = false;

            var replaceResult = await controller.Edit(created.ProductId, editReplaceVm);
            Assert.IsType<RedirectToActionResult>(replaceResult);

            var afterReplace = await db.TbProducts.AsNoTracking().FirstAsync(p => p.ProductId == created.ProductId);
            Assert.NotNull(afterReplace.Image);
            Assert.NotEqual(firstRelPath, afterReplace.Image);
            var secondRelPath = afterReplace.Image!;
            var secondPhysical = Path.Combine(_tempWebRoot, secondRelPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(secondPhysical));
            Assert.False(File.Exists(firstPhysical)); // Old file cleaned up!

            // 4. Edit product with RemoveImage = true -> clears DB Image and deletes second file
            var editRemoveVm = ProductFormViewModel.FromEntity(afterReplace);
            editRemoveVm.ImageFile = null;
            editRemoveVm.RemoveImage = true;

            var removeResult = await controller.Edit(created.ProductId, editRemoveVm);
            Assert.IsType<RedirectToActionResult>(removeResult);

            var afterRemove = await db.TbProducts.AsNoTracking().FirstAsync(p => p.ProductId == created.ProductId);
            Assert.Null(afterRemove.Image);
            Assert.False(File.Exists(secondPhysical));
        }
        finally
        {
            var toDelete = await db.TbProducts.FirstOrDefaultAsync(p => p.ProductId == created.ProductId);
            if (toDelete != null)
            {
                var movements = await db.TbInventoryMovements.Where(m => m.ProductId == created.ProductId).ToListAsync();
                db.TbInventoryMovements.RemoveRange(movements);
                db.TbProducts.Remove(toDelete);
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task ApiDbState_ProjectsMenuImageUrl_AndInvalidatesETagWhenProductImageChanges()
    {
        using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        // 1. Fetch initial /api/db/state
        var res1 = await client.GetAsync("/api/db/state");
        res1.EnsureSuccessStatusCode();
        var etag1 = res1.Headers.ETag?.Tag;
        Assert.False(string.IsNullOrWhiteSpace(etag1));

        using var doc1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync());
        var menuArr = doc1.RootElement.GetProperty("menu");
        Assert.True(menuArr.GetArrayLength() > 0);

        foreach (var item in menuArr.EnumerateArray())
        {
            Assert.True(item.TryGetProperty("imageUrl", out var imgProp));
            var url = imgProp.GetString();
            Assert.False(string.IsNullOrWhiteSpace(url));
            Assert.True(url!.StartsWith("/uploads/products/") || url.StartsWith("/assets/products/"));
        }

        // 2. Conditional GET with If-None-Match returns 304 Not Modified when unchanged
        using (var condReq1 = new HttpRequestMessage(HttpMethod.Get, "/api/db/state"))
        {
            condReq1.Headers.TryAddWithoutValidation("If-None-Match", etag1!);
            var condRes1 = await client.SendAsync(condReq1);
            Assert.Equal(HttpStatusCode.NotModified, condRes1.StatusCode);
        }

        // 3. Mutate TbProduct.Image for Product #1 and verify ETag changes (returns 200 OK instead of 304)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var prod1 = await db.TbProducts.FirstAsync(p => p.ProductId == 1);
        var originalImage = prod1.Image;

        try
        {
            prod1.Image = "/uploads/products/etag-test-probe.webp";
            prod1.ModifiedDate = DateTime.UtcNow;
            await db.SaveChangesAsync();

            using var condReq2 = new HttpRequestMessage(HttpMethod.Get, "/api/db/state");
            condReq2.Headers.TryAddWithoutValidation("If-None-Match", etag1!);
            var condRes2 = await client.SendAsync(condReq2);
            Assert.Equal(HttpStatusCode.OK, condRes2.StatusCode);

            var etag2 = condRes2.Headers.ETag?.Tag;
            Assert.NotEqual(etag1, etag2);

            using var doc2 = JsonDocument.Parse(await condRes2.Content.ReadAsStringAsync());
            var updatedItem1 = doc2.RootElement.GetProperty("menu").EnumerateArray().First(x => x.GetProperty("id").GetInt32() == 1);
            Assert.Equal("/uploads/products/etag-test-probe.webp", updatedItem1.GetProperty("imageUrl").GetString());
        }
        finally
        {
            prod1.Image = originalImage;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task AdminProductEndpoints_EnforceAuthorizationAndAntiForgeryToken()
    {
        using var anonClient = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        // 1. Anonymous GET /Admin/Products redirects to Login
        var getAnon = await anonClient.GetAsync("/Admin/Products");
        Assert.Equal(HttpStatusCode.Redirect, getAnon.StatusCode);

        // 2. Anonymous POST /Admin/Products/Edit/1 without auth/CSRF is blocked
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "ProductId");
        form.Add(new StringContent("Hack"), "Title");
        var postAnon = await anonClient.PostAsync("/Admin/Products/Edit/1", form);
        Assert.True(
            postAnon.StatusCode == HttpStatusCode.Redirect ||
            postAnon.StatusCode == HttpStatusCode.BadRequest ||
            postAnon.StatusCode == HttpStatusCode.Forbidden);
    }
}
