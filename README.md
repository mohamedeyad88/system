# Apex Printing System 🖨️

**نظام إدارة الطباعة والكتب المرقمة المتقدم**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-0078D4?logo=windows)](https://github.com/dotnet/wpf)
[![SQLite](https://img.shields.io/badge/Database-SQLite-003B57?logo=sqlite)](https://www.sqlite.org/)
[![Arabic](https://img.shields.io/badge/Lang-Arabic%20%7C%20English-green)](README.md)

---

## 📖 نظرة عامة

**Apex Printing System** هو نظام شامل ومتقدم لإدارة عمليات الطباعة، الكتب المرقمة، وتوزيع المهام على الطابعات. تم تطويره باستخدام تقنيات .NET الحديثة مع واجهة WPF احترافية ودعم كامل للغة العربية.

### المزايا الرئيسية ✨

- 🖨️ **إدارة شاملة للطابعات** - تتبع الحالة، التشخيصات، والصيانة
- 📚 **محرك الكتب المرقمة** - نظام متطور لترقيم وطباعة الكتب
- 🎯 **توزيع ذكي للمهام** - Load balancing وتوجيه تلقائي
- 📊 **لوحة تحكم متقدمة** - إحصائيات وتقارير في الوقت الفعلي
- 🌐 **دعم ثنائي اللغة** - عربي/إنجليزي مع RTL support
- 💾 **قاعدة بيانات مرنة** - SQLite مع auto-migration
- 🎨 **واجهة حديثة** - Material Design مع رسوم متحركة

---

## 🏗️ البنية التنظيمية

المشروع منظم في مجلدين رئيسيين:

### 1. المجلد الجذر (`d:\Apex\system`)

> **ملاحظة:** هذا المجلد يحتوي على مشاريع prototype/template قديمة غير مستخدمة حالياً.

```
d:\Apex\system\
├── Apex.Core\          (مشروع قديم - غير مستخدم)
├── Apex.Data\          (مشروع قديم - غير مستخدم)
├── Apex.Services\      (مشروع قديم - غير مستخدم)
├── Apex.UI\            (مشروع قديم - غير مستخدم)
└── Apex.PrintingSystem\ (المشروع الفعلي - انظر أدناه)
```

### 2. نظام الطباعة الكامل (`Apex.PrintingSystem`)

```
Apex.PrintingSystem/
├── Apex.Core/                      # المكونات الأساسية المشتركة
├── Apex.Data/                      # طبقة قاعدة البيانات
│   ├── ApexDbContext.cs           # Entity Framework Context
│   ├── Entities/                  # نماذج البيانات
│   ├── Repositories/              # Data Access Layer
│   └── Migrations/                # Auto-migration system
├── Apex.Services/                  # منطق الأعمال
│   ├── PrinterService.cs
│   ├── PrintJobService.cs
│   └── DistributionService.cs
├── Apex.UI/                        # الواجهة الرئيسية (WPF)
│   ├── ViewModels/                # 14+ ViewModel
│   ├── Views/                     # 33+ View (XAML)
│   ├── Resources/                 # اللغات والموارد
│   ├── Styles/                    # التنسيقات والمواضيع
│   └── Converters/                # Value Converters
├── Apex.NumberedBooksEngine/       # محرك الكتب المرقمة
├── Apex.NumberedBooksEngine.UI/    # واجهة الكتب المرقمة
├── Apex.NumberedBooksEngine.CLI/   # واجهة سطر الأوامر
├── Apex.NumberedBooksEngine.Tests/ # الاختبارات
└── Apex.Setup/                     # برنامج التثبيت
```

---

## 🚀 البدء السريع

### المتطلبات الأساسية

- **Windows** 10/11 (x64)
- **.NET 8.0 Desktop Runtime** ([تحميل](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Visual Studio 2022** أو أحدث (للتطوير)
- **4GB RAM** على الأقل
- **500MB** مساحة تخزين

### التثبيت والتشغيل

#### 1. للمطورين (Development)

```powershell
# استنساخ المشروع
git clone <repository-url>
cd Apex\system\Apex.PrintingSystem

# البناء
dotnet build Apex.PrintingSystem.sln -c Debug

# التشغيل
cd Apex.UI\bin\Debug\net8.0-windows
.\Apex.UI.exe
```

#### 2. للمستخدمين (Production)

```powershell
# بناء نسخة Release
dotnet build Apex.PrintingSystem.sln -c Release

# أو استخدام المثبت
.\ApexPrintingSystemSetup.exe
```

#### 3. التشغيل مع التشخيصات

```cmd
# لتشخيص أي مشاكل في بدء التشغيل
.\LaunchWithDiagnostics.bat
```

---

## 📚 الوحدات الرئيسية

### 1. لوحة التحكم (Dashboard)
- نظرة شاملة على حالة النظام
- إحصائيات الطباعة في الوقت الفعلي
- تنبيهات وإشعارات

### 2. إدارة الطابعات (Printers Management)
- إضافة/تعديل/حذف الطابعات
- مراقبة الحالة والأداء
- تشخيصات تفصيلية
- إدارة قدرات الطابعات

### 3. مدير الطباعة (Print Manager)
- قوائم انتظار الطباعة
- جدولة المهام
- إدارة الأولويات
- حفظ واستعادة الطلبات

### 4. معالج الترقيم (Numbering Wizard)
- إنشاء كتب مرقمة
- تصميم التخطيط
- معاينة قبل الطباعة
- دعم PDF import

### 5. التوزيع الذكي (Distribution)
- قواعد توجيه تلقائية
- Load balancing
- مجموعات طابعات (Printer Pools)
- استراتيجيات مخصصة

### 6. التقارير والإحصائيات
- تقارير الأداء
- إحصائيات الاستخدام
- تحليل التكاليف
- تصدير البيانات

---

## 🛠️ التقنيات المستخدمة

### Frontend
- **WPF** - Windows Presentation Foundation
- **MVVM Pattern** - Model-View-ViewModel
- **CommunityToolkit.Mvvm** - Helper library
- **LiveCharts** - Data visualization
- **SkiaSharp** - Graphics rendering

### Backend
- **.NET 8.0** - Framework
- **Entity Framework Core 8.0** - ORM
- **SQLite** - Database
- **Serilog** - Logging
- **Dependency Injection** - Microsoft.Extensions

### Tools
- **Inno Setup** - Installer creation
- **AutoMapper** - Object mapping
- **FluentValidation** - Validation (optional)

---

## 🗄️ قاعدة البيانات

### الجداول الرئيسية

| الجدول | الوصف |
|--------|-------|
| `Printers` | معلومات الطابعات |
| `PrintJobs` | مهام الطباعة |
| `SystemSettings` | إعدادات النظام |
| `SavedQueues` | قوائم الانتظار المحفوظة |
| `RoutingRules` | قواعد التوجيه |
| `PrinterPools` | مجموعات الطابعات |

### Auto-Migration System

التطبيق يستخدم نظام ترحيل تلقائي مخصص:
- يكتشف الفروقات في Schema تلقائياً
- يطبق التحديثات عند بدء التشغيل
- يحفظ نسخة احتياطية قبل التحديث
- يسجل جميع التغييرات

---

## 🌐 دعم اللغات

### اللغات المدعومة
- 🇸🇦 **العربية** (الافتراضية)
- 🇬🇧 **الإنجليزية**

### تغيير اللغة
1. افتح **الإعدادات** (Settings)
2. اختر **اللغة** (Language)
3. حدد اللغة المطلوبة
4. أعد تشغيل التطبيق

---

## 🐛 استكشاف الأخطاء

### التطبيق لا يبدأ

**المشكلة:** النافذة لا تظهر بعد تشغيل .exe

**الحل:**
1. تحقق من ملفات السجل على سطح المكتب:
   - `ApexStartup_ERROR.log`
   - `ApexStartup_YYYYMMDD_HHMMSS.log`
2. شغل `LaunchWithDiagnostics.bat`
3. تأكد من تثبيت .NET 8.0 Desktop Runtime

### مشاكل المسار العربي

**المشكلة:** أخطاء Unicode في اسم المجلد

**الحل:**
```powershell
# انسخ المجلد إلى مسار إنجليزي
Copy-Item "Apex.UI\bin\Debug\net8.0-windows" -Destination "C:\Temp\ApexTest" -Recurse
cd C:\Temp\ApexTest
.\Apex.UI.exe
```

### مشاكل OneDrive

**المشكلة:** قفل الملفات أو بطء الأداء

**الحل:**
- أوقف مزامنة OneDrive مؤقتاً
- أو: انقل المشروع خارج مجلد OneDrive

### راجع دليل النشر الكامل
للمزيد من التفاصيل، راجع: [`README_DEPLOYMENT.md`](./README_DEPLOYMENT.md)

---

## 📖 الوثائق

- **دليل المستخدم:** `docs/UserGuide.md`
- **دليل المطور:** `docs/DeveloperGuide.md`
- **دليل API:** `docs/API.md`
- **دليل النشر:** `README_DEPLOYMENT.md`

---

## 🤝 المساهمة

نرحب بمساهماتكم! يرجى:
1. Fork المشروع
2. إنشاء branch للميزة (`git checkout -b feature/AmazingFeature`)
3. Commit التغييرات (`git commit -m 'Add some AmazingFeature'`)
4. Push للـ branch (`git push origin feature/AmazingFeature`)
5. فتح Pull Request

---

## 📝 الترخيص

هذا المشروع محمي بحقوق الطبع والنشر © 2025 Apex Printing Press

---

## 📞 الدعم الفني

- **البريد الإلكتروني:** support@apex-printing.local
- **الهاتف:** +20-XXX-XXX-XXXX
- **الموقع:** [www.apex-printing.local](http://www.apex-printing.local)

---

## 🎯 خارطة الطريق (Roadmap)

### الإصدار القادم (v2.0)

- [ ] دعم Cloud printing
- [ ] تطبيق Mobile (iOS/Android)
- [ ] REST API للتكامل
- [ ] تقارير BI متقدمة
- [ ] دعم لغات إضافية
- [ ] AI-powered optimization

---

## ⚡ ملاحظات مهمة

> [!WARNING]
> **للمطورين:** المشاريع في المجلد الجذر (`Apex.Core`, `Apex.Data`, `Apex.Services`, `Apex.UI`) هي نماذج أولية قديمة. 
> استخدم فقط المشاريع داخل مجلد `Apex.PrintingSystem`.

> [!TIP]
> استخدم `DependencyChecker.ps1` للتحقق من جميع المتطلبات قبل التثبيت.

> [!IMPORTANT]
> تأكد من أخذ نسخة احتياطية من قاعدة البيانات (`apex.db`) بشكل دوري.

---

<div align="center">

**صُنع بـ ❤️ في مصر**

[⬆ العودة للأعلى](#apex-printing-system-)

</div>
