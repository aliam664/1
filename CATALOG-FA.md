# راهنمای کاتالوگ عمومی مود — AC Mod Hub 2.0

این سند قرارداد کاتالوگ عمومی را از دید **کلاینت (AC Mod Hub)** توصیف می‌کند.
کاتالوگ در مخزن عمومی `aliam664/Data` نگهداری می‌شود و **بدون تغییر برنامه** قابل
به‌روزرسانی، افزودن، مخفی‌کردن یا مسدودکردن مود است.

## ۱) Endpointها

| ترتیب | Endpoint | نقش |
| --- | --- | --- |
| ۱ | `https://aliam664.github.io/Data/catalog.v1.json` | کاتالوگ اصلی (GitHub Pages) |
| ۲ | `https://raw.githubusercontent.com/aliam664/Data/generated/catalog.v1.json` | جایگزین خام (شاخهٔ `generated`) |
| ۳ | کاتالوگ داخلی برنامه | خالی و آماده، فقط در حالت آفلاین کامل |

در بخش «تنظیمات ← پیشرفته» می‌توانید یک Endpoint سفارشی (فقط HTTPS) وارد کنید.
اگر پاسخ نامعتبر یا اتصال قطع باشد، برنامه به **آخرین کپی سالم کش‌شده** برمی‌گردد
و هرگز کش سالم را با دادهٔ خراب جایگزین نمی‌کند.

## ۲) قالب سند کاتالوگ (schemaVersion = 1)

```json
{
  "schemaVersion": 1,
  "revision": 1,
  "generatedAt": "2026-08-16T00:00:00Z",
  "repository": "aliam664/Data",
  "minimumLauncherVersion": "2.0.0",
  "mods": []
}
```

هر عضو `mods`:

```json
{
  "id": "author.mod-id",
  "status": "published",
  "revocationReason": null,
  "name": { "fa": "نام فارسی", "en": "English name" },
  "author": { "name": "Author name", "url": "https://example.com" },
  "version": "1.0.0",
  "category": "car",
  "description": { "fa": "توضیحات فارسی", "en": "English description" },
  "cover": "assets/covers/author.mod-id.webp",
  "tags": [],
  "package": {
    "releaseTag": "author.mod-id-v1.0.0",
    "assetName": "author.mod-id-v1.0.0.zip",
    "downloadUrl": "https://github.com/aliam664/Data/releases/download/author.mod-id-v1.0.0/author.mod-id-v1.0.0.zip",
    "size": 123456,
    "sha256": "64_کاراکتر_هگز"
  },
  "minimumLauncherVersion": "2.0.0",
  "publishedAt": "2026-08-16T00:00:00Z",
  "updatedAt": "2026-08-16T00:00:00Z"
}
```

## ۳) رفتار Status در کلاینت

| Status | رفتار برنامه |
| --- | --- |
| `draft` | در خروجی عمومی نمایش داده نمی‌شود. |
| `published` | عادی نمایش داده می‌شود و قابل نصب است. |
| `hidden` | برای کاربران جدید نمایش داده نمی‌شود. |
| `deprecated` | نمایش داده می‌شود ولی با برچسب هشدار «منسوخ‌شده». |
| `revoked` | فرادادهٔ امنیتی باقی می‌ماند ولی دانلود و نصب **مسدود** است؛ دلیل (`revocationReason`) نمایش داده می‌شود. |

علاوه بر آن:

- دستهٔ `category` فقط از مقادیر `car / track / skin / app / weather / csp / miscellaneous`.
- نسخهٔ `version` باید Semantic Version معتبر باشد.
- `sha256` باید دقیقاً ۶۴ کاراکتر هگز باشد و پیش از نصب صحت بسته بررسی می‌شود.
- `size` مثبت و الزامی است و با حجم واقعی دانلود مقایسه می‌شود.
- `package.assetName` فقط ZIP/7Z/RAR و بدون مسیر؛ نام‌های رزروشدهٔ ویندوز رد می‌شوند.
- `cover` مسیر نسبی امن است و از HTTPS و Hostهای مجاز بارگیری می‌شود (با کش محدود).
- اگر `minimumLauncherVersion` مود از نسخهٔ برنامه جدیدتر باشد، نصب آن مود مسدود می‌شود.

## ۴) جریان نصب از فروشگاه

1. کاتالوگ دریافت، اعتبارسنجی و کش می‌شود (ETag / 304).
2. کاربر «نصب» را می‌زند؛ برنامه فقط HTTPS دانلود می‌کند.
3. پیشرفت واقعی (حجم/سرعت/درصد) نمایش داده می‌شود؛ لغو، فایل ناقص `.part` را پاک می‌کند.
4. پس از دانلود، SHA-256 و حجم با فراداده مقایسه می‌شود.
5. بسته وارد «نصب امن» می‌شود: تحلیل ← پیش‌نمایش ← بررسی تداخل ← پشتیبان ← نصب ← تأیید.
6. شناسهٔ کاتالوگ (`catalogId` / `catalogVersion`) در Manifest مود ثبت می‌شود تا
   داشبورد و کتابخانه بتوانند نسخهٔ جدیدتر را تشخیص دهند.

## ۵) افزودن/به‌روزرسانی/مخفی‌کردن/Revoke (سمت مخزن Data)

مدیریت کاتالوگ در مخزن `aliam664/Data` انجام می‌شود و این برنامه نیازی به تغییر ندارد:

- **افزودن مود:** GitHub Release بسازید، بستهٔ ZIP/7Z/RAR را ضمیمه کنید، SHA-256 را با
  PowerShell (`Get-FileHash -Algorithm SHA256`) بگیرید، `mods/{mod-id}/mod.json` را از
  Template بسازید، Cover را در `assets/covers/` بگذارید و Commit کنید.
- **آپدیت مود:** Release جدید + تغییر `version` و `package` (Tag، نام فایل، حجم، SHA) + Commit.
- **مخفی‌کردن:** `"status": "hidden"`.
- **منسوخ‌کردن:** `"status": "deprecated"`.
- **Revoke امنیتی:** `"status": "revoked"` همراه `revocationReason`؛ دانلود و نصب بلافاصله
  در همهٔ کلاینت‌ها مسدود می‌شود.
- **حذف فیزیکی** فقط برای مود منتشرنشده/اشتباهی توصیه می‌شود؛ برای مود منتشرشده از
  `hidden` یا `revoked` استفاده کنید.

## ۶) حالت‌های نمایش در فروشگاه

- **بارگذاری (Skeleton):** کارت‌های placeholder پیش از رسیدن داده.
- **خالی:** وقتی کاتالوگ عمومی هنوز مودی ندارد.
- **آفلاین:** آخرین کپی معتبر کش + پیام آفلاین.
- **خطا:** پیام خطای محلی‌شده + دکمهٔ تلاش مجدد.
- **بدون WebView2:** فروشگاه سادهٔ داخلی + پیام نصب Runtime رسمی Microsoft.
