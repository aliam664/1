# راهنمای فارسی نصب و راه‌اندازی AC Mod Hub

این راهنما دو روش را پوشش می‌دهد:

1. نصب نسخهٔ آماده با `AC-Mod-Hub-Setup.exe`
2. Build و ساخت Installer از سورس

## ۱. پیش‌نیاز اجرای برنامه

برای نسخهٔ Self-contained نیازی به نصب جداگانهٔ .NET نیست.

سیستم پیشنهادی:

- Windows 10 نسخهٔ 1809 یا جدیدتر، یا Windows 11
- پردازنده و سیستم‌عامل 64 بیتی
- نصب قانونی Assetto Corsa از Steam
- حداقل ۵ گیگابایت فضای خالی برای Cache و Backupها
- دسترسی نوشتن عادی به Steam Library بازی

برنامه نباید به‌صورت دائمی با Administrator اجرا شود و هیچ‌وقت به‌شکل مخفی دسترسی Administrator درخواست نمی‌کند.

## ۲. نصب با فایل Setup

فایل Preview آماده از مسیر زیر قابل دریافت است:

```text
releases/v1.0.0-preview.1/AC-Mod-Hub-Setup.exe
```

یا Release صفحهٔ GitHub را باز کنید:

```text
https://github.com/aliam664/1/releases/tag/v1.0.0-preview.1
```

نام فایل:

```text
AC-Mod-Hub-Setup.exe
```

مراحل:

1. روی فایل Setup دوبار کلیک کنید.
2. زبان Installer را انتخاب و مراحل را ادامه دهید.
3. مسیر پیش‌فرض نصب برای کاربر فعلی مناسب است:

```text
%LocalAppData%\Programs\ACModHub
```

4. در صورت نیاز گزینهٔ ساخت Shortcut روی Desktop را فعال کنید.
5. بعد از پایان نصب، AC Mod Hub را اجرا کنید.

Installer از نوع Per-user است و به‌طور معمول UAC یا دسترسی Administrator نیاز ندارد.

### هشدار SmartScreen

تا زمانی که فایل اجرایی با Certificate رسمی Code Signing امضا نشده باشد، Windows SmartScreen ممکن است هشدار نمایش دهد. فقط فایلی را اجرا کنید که از Repository/Release رسمی همین پروژه دریافت کرده‌اید. Hash فایل Release را نیز در صورت انتشار بررسی کنید.

## ۳. اجرای نسخهٔ Portable

فایل زیر را دریافت کنید:

```text
AC-Mod-Hub-Portable-win-x64.zip
```

سپس:

1. ZIP را در یک پوشهٔ قابل‌نوشتن Extract کنید.
2. فایل `ACModHub.exe` را اجرا کنید.
3. فایل‌های Publish را از کنار EXE حذف نکنید.

اطلاعات برنامه و مودها در حالت عادی داخل پوشهٔ Portable ذخیره نمی‌شوند؛ داده‌ها در مسیر زیر قرار می‌گیرند:

```text
%AppData%\ACModHub
```

## ۴. راه‌اندازی اولیه و پیدا کردن بازی

در اولین اجرا، برنامه Steam و تمام Libraryهای ثبت‌شده در `libraryfolders.vdf` را بررسی می‌کند.

Root معتبر بازی باید شامل موارد زیر باشد:

```text
assettocorsa\
├── acs.exe
├── content\
├── apps\
└── system\
```

اگر چند نصب معتبر پیدا شود:

1. وارد صفحهٔ **Settings / تنظیمات** شوید.
2. از فهرست Installationهای شناسایی‌شده، مسیر موردنظر را انتخاب کنید.
3. روی **Save / ذخیره** کلیک کنید.

اگر بازی خودکار پیدا نشد:

1. به **Settings** بروید.
2. روی **Browse / انتخاب مسیر** کلیک کنید.
3. پوشه‌ای را انتخاب کنید که فایل `acs.exe` مستقیماً داخل آن قرار دارد؛ برای مثال:

```text
D:\SteamLibrary\steamapps\common\assettocorsa
```

4. گزینهٔ **Run diagnostics / اجرای عیب‌یابی** را اجرا کنید.
5. نتیجهٔ Game Path، Content Folder، Write Access و Disk Space را بررسی کنید.
6. تنظیمات را ذخیره کنید.

## ۵. نصب مود از Archive

فرمت‌های پشتیبانی‌شده:

```text
.zip
.7z
.rar
```

دو روش Import وجود دارد:

- کشیدن Archive و رهاکردن آن روی پنجرهٔ برنامه
- انتخاب دکمهٔ **Import mod / وارد کردن مود**

فرآیند نصب:

```text
Analyze
→ Preview
→ Conflict Check
→ Backup
→ Install
→ Verify
→ Done
```

قبل از نصب:

1. نام، نسخه، سازنده و Category تشخیص‌داده‌شده را بررسی کنید.
2. فهرست دقیق Relative Pathها را ببینید.
3. Conflictها و فایل‌هایی که جایگزین می‌شوند را بررسی کنید.
4. برای بسته‌های Unknown فقط بعد از بررسی تمام مسیرها، تأیید بازنویسی را فعال کنید.
5. روی **Install & Verify** کلیک کنید.

مودهای Mixed می‌توانند همزمان بخش‌هایی مانند `content` و `extension` داشته باشند و ساختار هر دو نسبت به Game Root حفظ می‌شود.

## ۶. نصب مود از URL

1. صفحهٔ **Downloads / دانلودها** را باز کنید.
2. URL کامل HTTP یا HTTPS را وارد کنید.
3. نام فایل را با پسوند صحیح، مانند `my-car.zip` وارد کنید.
4. اگر SHA-256 در اختیار دارید، آن را در کادر مربوط وارد کنید.
5. روی **Add / افزودن** کلیک کنید.
6. Progress، Bytes، Size، Speed و ETA را مشاهده کنید.
7. بعد از رسیدن Status به `Completed`، ردیف دانلود را انتخاب کنید.
8. روی **Install / نصب** کلیک کنید تا فایل وارد مرحلهٔ Analyze و Preview شود.

اگر Server مقدار Content-Length ندهد، درصد جعلی نمایش داده نمی‌شود؛ Bytes و Status همچنان واقعی هستند.

## ۷. Conflict و Backup

قبل از جایگزینی هر فایل موجود، نسخهٔ قبلی خارج از Game Root ذخیره می‌شود:

```text
%AppData%\ACModHub\backups
```

Manifest هر مود ثبت می‌کند:

- Relative Path فایل
- Size
- SHA-256 نصب‌شده
- اینکه فایل قبل از نصب وجود داشته یا نه
- مسیر Backup نسخهٔ قبلی
- ترتیب مالکیت فایل‌های مشترک

هیچ Backup داخل پوشهٔ Assetto Corsa ذخیره نمی‌شود.

## ۸. Disable، Enable، Repair و Update

در صفحهٔ جزئیات مود:

- **Disable:** فایل مود را از بازی خارج می‌کند و در صورت وجود فایل قبلی، نسخهٔ قبلی را برمی‌گرداند.
- **Enable:** فایل مود را دوباره اعمال می‌کند.
- **Verify:** وجود، Size و SHA-256 فایل‌ها را بررسی می‌کند.
- **Repair:** فایل‌های خراب یا گم‌شده را از Package Cache بازسازی می‌کند.
- **Reinstall:** همان Package ذخیره‌شده را دوباره نصب می‌کند.
- **Update:** Archive نسخهٔ جدید را انتخاب و به‌صورت Transactional نصب می‌کند؛ Backup اصلی نسخهٔ قبل از اولین نصب حفظ می‌شود.

## ۹. حذف امن مود

هنگام Uninstall سه وضعیت ممکن است رخ دهد:

### فایل بدون تغییر است

- فایل ساخته‌شده توسط مود حذف می‌شود.
- فایل موجود قبل از نصب از Backup بازگردانده می‌شود.

### فایل بعد از نصب توسط کاربر تغییر کرده است

برنامه حذف عادی را متوقف می‌کند و دو انتخاب ارائه می‌دهد:

- **Keep modified & uninstall:** فایل تغییرکرده را نگه می‌دارد و فقط Ownership/Manifest مود را حذف می‌کند.
- **Discard changes & uninstall:** تغییرات را کنار می‌گذارد و حالت قبل از نصب را Restore می‌کند.

### مود جدیدتری همان فایل را Override کرده است

برای جلوگیری از خراب‌شدن زنجیرهٔ Backup، ابتدا باید مود جدیدتر حذف شود. سپس مود قدیمی قابل حذف خواهد بود.

## ۱۰. محل داده‌ها و Logها

```text
%AppData%\ACModHub\
├── backups\
├── cache\
├── disabled\
├── downloads\
├── journals\
├── locks\
├── logs\
├── manifests\
├── library.json
├── ownership.json
└── settings.json
```

Logهای Debug و خطا در مسیر زیر هستند:

```text
%AppData%\ACModHub\logs
```

برای گزارش مشکل، جدیدترین Log را همراه با توضیح عملیات ارسال کنید؛ اطلاعات حساس یا مسیرهای شخصی را قبل از ارسال بررسی کنید.

## ۱۱. Build پروژه از سورس

### ابزارهای لازم

- Windows 10/11 x64
- Git
- .NET 8 SDK
- Inno Setup 6 برای ساخت Setup

Repository را Clone و وارد شاخهٔ پروژه شوید:

```powershell
git clone https://github.com/aliam664/1.git
cd 1
git checkout arena/019ff412-1
```

سپس:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet publish -c Release -r win-x64 --self-contained true
```

برای Publish در مسیر مشخص:

```powershell
dotnet publish .\src\ACModHub.App\ACModHub.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o .\artifacts\publish\win-x64
```

## ۱۲. ساخت Portable و Installer

Inno Setup 6 را نصب کنید، سپس از Root پروژه اجرا کنید:

```powershell
.\build.ps1
```

خروجی‌ها:

```text
artifacts\AC-Mod-Hub-Portable-win-x64.zip
artifacts\installer\AC-Mod-Hub-Setup.exe
```

## ۱۳. ساخت خودکار با GitHub Actions

تعریف Workflow ویندوز در قالب زیر حفظ شده است:

```text
installer/windows-release.yml.template
```

برای فعال‌کردن GitHub Actions، این فایل را با یک Token/اتصال دارای مجوز Workflows به مسیر `.github/workflows/windows-release.yml` کپی و commit کنید.

پس از فعال‌کردن و Push کردن Workflow، در GitHub:

1. وارد تب **Actions** شوید.
2. Workflow با نام **Windows Build, Test and Installer** را انتخاب کنید.
3. روی **Run workflow** کلیک کنید.
4. شاخهٔ `arena/019ff412-1` را انتخاب کنید.
5. بعد از موفقیت Job، Artifact با نام زیر را دانلود کنید:

```text
AC-Mod-Hub-1.0.1-win-x64
```

Artifact شامل Setup و Portable ZIP است.

## ۱۴. رفع خطاهای رایج

### بازی پیدا نمی‌شود

پوشه‌ای را انتخاب کنید که `acs.exe` داخل همان پوشه است. انتخاب `content` یا پوشهٔ بالاتر Steam اشتباه است.

### Write Access ناموفق است

Steam Library را به یک مسیر قابل‌نوشتن منتقل کنید یا Permission همان Library را اصلاح کنید. برنامه خودش UAC را دور نمی‌زند و Silent Elevation انجام نمی‌دهد.

### فایل Locked است

Assetto Corsa، Content Manager و ابزارهایی که ممکن است فایل بازی را باز نگه داشته باشند ببندید و دوباره Analyze کنید. برنامه برای کارکرد به Content Manager وابسته نیست.

### Archive رد می‌شود

Archiveهای رمزدار، خراب، دارای Path Traversal، symlink/junction، executable/command یا ساختار خطرناک عمداً مسدود می‌شوند.

### نصب در وسط عملیات قطع شده است

برنامه را دوباره اجرا کنید. Crash Recovery با استفاده از Journal، فایل‌ها، Manifest و Ownership را به حالت قبلی برمی‌گرداند. اگر فایل همچنان Locked باشد، Journal در وضعیت Recovery Required باقی می‌ماند.

### Backup موردنیاز پیدا نمی‌شود

Uninstall یا Disable متوقف می‌شود تا فایل قدیمی اشتباهاً حذف نشود. Backup Manager و Log را بررسی کنید؛ از حذف دستی پوشهٔ Backups خودداری کنید.
