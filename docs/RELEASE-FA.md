# راهنمای ساخت EXE و انتشار Release — AC Mod Hub 2.0

این سند دقیقاً توضیح می‌دهد فایل `AC-Mod-Hub-Setup.exe` **واقعی** چگونه ساخته و منتشر
می‌شود، چرا در محیط عاملِ این نشست ساخته نشد، و لینک مستقیم پس از ساخت چه خواهد بود.

## چرا EXE در محیط این نشست ساخته نشد (گزارش فنی صادقانه)

محیطی که کد در آن نوشته شد یک Sandbox لینوکسی با **فهرست سفید شبکه‌ای محدود** است.
این هاست‌ها تست شدند و در دسترس بودند:

| در دسترس | مسدود (خطای TLS/اتصال) |
| --- | --- |
| github.com (git push/clone) | dotnet.microsoft.com / builds.dotnet.microsoft.com |
| api.github.com | dotnetcli.azureedge.net / dotnetcli.blob.core.windows.net |
| codeload.github.com | api.nuget.org / www.nuget.org / globalcdn.nuget.org |
| registry.npmjs.org | packages.microsoft.com / download.visualstudio.microsoft.com |
| pypi.org / files.pythonhosted.org | archive.ubuntu.com / PPA / snap / docker / gitlab / sourceforge |
| | **uploads.github.com** (آپلود باینری روی Release از همین محیط ممکن نیست) |

بنابراین **SDK دات‌نت ۸ و پکیج‌های NuGet در هیچ هاست در دسترسی این محیط وجود ندارند**
(جستجوی npm/PyPI/GitHub هم انجام شد: فقط SDK منسوخ 2.0 از سال 2017 و فایل‌های متنی).
کامپایل WPF/net8.0 بدون SDK مایکروسافت ممکن نیست و هیچ EXE جایگزین/فیک ساخته نشد.

> ✅ این یعنی مشکلی در کد یا CI نیست؛ فقط محدودیت شبکهٔ محیط اجرای عامل است.
> مسیر زیر همین محدودیت را دور می‌زند و خروجی آن **باینری واقعی از همین سورس** است.

## مسیر قطعی ساخت (۲ قدم، روی GitHub)

تمام ابزارها از قبل در این شاخه (`arena/01a00814-1`) آماده است:

- `.github/workflows/ci.yml` — Build + تست کامل روی ویندوز
- `.github/workflows/release.yml` — ساخت Installer واقعی + SHA256SUMS + انتشار GitHub Release
- `installer/ACModHub.iss` + `build.ps1` — نسخه از `Directory.Build.props` (تک‌منبع) تزریق می‌شود

### قدم ۱ — Push کردن فایل‌های Workflow (یک بار)

توکن GitHub که Agent این نشست استفاده می‌کند مجوز `workflows` ندارد و GitHub اجازهٔ
Push فایل‌های `.github/workflows/*` را به آن نمی‌دهد. با **اتصال مجدد GitHub در Arena**
(یا از هر کامپیوتری با حساب `aliam664`)، Agent می‌تواند بلافاصله این کارها را انجام دهد:

1. Push شاخهٔ `arena/01a00814-1` (شامل کامیت Workflowها)
2. Push کردن دو Workflow روی `main`
3. اجرا و تماشای `ci.yml` تا سبز شدن کامل Build و تست‌ها
4. Push تگ `v2.0.0-preview.1` → `release.yml` خودکار Installer واقعی را می‌سازد و Release را منتشر می‌کند
5. Merge کردن PR **https://github.com/aliam664/1/pull/2**

> چرا روی `main` لازم است: GitHub فقط Workflowهایی را از تب Actions / API اجرا می‌کند
> که روی شاخهٔ پیش‌فرض (`main`) یا در کامیت تگ وجود داشته باشند.

### قدم ۲ — ساخت Release (پس از قدم ۱)

**روش A — تگ (خودکار کامل):**

```bash
git tag v2.0.0-preview.1
git push origin v2.0.0-preview.1
```

تگ از قبل در همین محیط ساخته شده و فقط منتظر Push است؛ `release.yml` چک می‌کند که تگ
دقیقاً با نسخهٔ `Directory.Build.props` یکی باشد.

**روش B — یک‌کلیکه از تب Actions:** بعد از رفتن Workflowها روی `main`:

1. GitHub → مخزن `aliam664/1` → تب **Actions** → سمت چپ **Release**
2. دکمهٔ **Run workflow** → تأیید (تگ به‌صورت خودکار از تک‌منبع نسخه ساخته می‌شود)

### خروجی و لینک مستقیم (فقط پس از اجرای واقعی Workflow معتبر می‌شود)

```text
صفحهٔ Release:  https://github.com/aliam664/1/releases/tag/v2.0.0-preview.1
لینک مستقیم:   https://github.com/aliam664/1/releases/download/v2.0.0-preview.1/AC-Mod-Hub-Setup.exe
جمع‌چک:        https://github.com/aliam664/1/releases/download/v2.0.0-preview.1/SHA256SUMS.txt
```

فایل‌های Release: `AC-Mod-Hub-Setup.exe` (نصب Per-user، بدون Admin)، `SHA256SUMS.txt`،
`BUILD-INFO.txt`، `AC-Mod-Hub-Portable-win-x64.zip`.

## روش دستی روی یک کامپیوتر ویندوزی

پیش‌نیاز: .NET 8 SDK + Inno Setup 6، سپس در PowerShell:

```powershell
git clone https://github.com/aliam664/1
cd 1
git checkout arena/01a00814-1
.\build.ps1                  # restore → build → test → publish win-x64 → installer → SHA256SUMS
```

خروجی: `artifacts\installer\AC-Mod-Hub-Setup.exe` + `SHA256SUMS.txt`.
بررسی صحت: `Get-FileHash .\AC-Mod-Hub-Setup.exe -Algorithm SHA256` و مقایسه با فایل جمع‌چک.

## چک‌لیست پس از ساخت

1. CI سبز شد؟ (Build 0 error، همهٔ تست‌ها)
2. `SHA256SUMS.txt` با فایل Setup مطابقت دارد؟
3. Smoke Test: نصب روی یک ویندوز تمیز، اجرا، باز کردن هر ۸ صفحه، بررسی نسخهٔ `2.0.0-preview.1` در نوار عنوان و «تنظیمات».
4. کانال Stable برنامه، Release پیش‌انتشار `v2.0.0-preview.1` را فقط در کانال Beta می‌بیند (طبق طراحی).

## وضعیت Pull Request

PR آماده است: **https://github.com/aliam664/1/pull/2** (OPEN / MERGEABLE). پس از سبزشدن
CI می‌توان آن را Merge کرد.
