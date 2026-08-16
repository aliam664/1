# راهنمای افزودن لینک مود به AC Mod Hub 1.0.1

فروشگاه مود از دو منبع پشتیبانی می‌کند:

1. Catalog داخلی که داخل Assembly ذخیره می‌شود.
2. Catalog آنلاین اختیاری که از یک URL امن HTTPS دریافت می‌شود.

## Catalog داخلی و ساخت نسخهٔ قابل‌ارسال

فایل زیر را ویرایش کنید:

```text
assets/catalog.v1.json
```

نمونهٔ ساختار:

```json
{
  "schemaVersion": 1,
  "title": "My Assetto Corsa Collection",
  "updatedAt": "2026-08-12T00:00:00Z",
  "mods": [
    {
      "id": "example-car",
      "name": "Example Car",
      "author": "Author Name",
      "version": "1.2.0",
      "category": "Car",
      "description": "توضیح کوتاه برای نمایش روی کارت مود",
      "downloadUrl": "https://example.com/files/example-car.zip",
      "fileName": "example-car.zip",
      "thumbnailUrl": "https://example.com/images/example-car.jpg",
      "sha256": "64_HEXADECIMAL_CHARACTERS",
      "expectedSize": 123456789,
      "featured": true,
      "tags": ["street", "turbo", "4k"]
    }
  ]
}
```

`downloadUrl` باید لینک مستقیم HTTP/HTTPS به یکی از فرمت‌های زیر باشد:

```text
.zip
.7z
.rar
```

بعد از ویرایش فایل، نسخه را Build کنید. Catalog به‌صورت Embedded Resource داخل Assembly ذخیره می‌شود و همراه EXE برای دیگران قابل ارسال خواهد بود.

## Catalog آنلاین

همین JSON را روی یک URL عمومی HTTPS قرار دهید. سپس در برنامه:

1. Settings را باز کنید.
2. URL را در `Remote catalog URL` وارد کنید.
3. Save را بزنید.
4. صفحه Mod Store را Refresh کنید.

برنامه ابتدا نسخهٔ آنلاین را بررسی می‌کند، آخرین نسخهٔ معتبر را Cache می‌کند و در صورت قطع اینترنت به Cache یا Catalog داخلی برمی‌گردد.

## قواعد امنیتی Catalog

- شناسهٔ هر مود باید یکتا باشد.
- URL آنلاین Catalog فقط HTTPS است.
- Download URL فقط HTTP/HTTPS است.
- نام فایل نباید مسیر یا `..` داشته باشد.
- پسوند باید ZIP، 7Z یا RAR باشد.
- SHA-256 در صورت وجود باید دقیقاً ۶۴ کاراکتر Hex باشد.
- Catalog حداکثر ۲ MiB است.
- Catalog نمی‌تواند Command، PowerShell یا EXE اجرا کند.
- Download همچنان وارد Archive Validation، Preview، Conflict Check، Backup، Install و Verify می‌شود.

## Categoryهای قابل استفاده

```text
Car
Track
Skin
App
Weather
Csp
Graphics
Sound
Mixed
Miscellaneous
```

## اطلاعاتی که برای افزودن لینک‌ها لازم است

برای هر مود این قالب را ارسال کنید:

```text
Name:
Direct Download URL:
Author:
Version:
Category:
Description:
Thumbnail URL:
SHA-256:
Expected Size:
Tags:
Featured: true/false
```
