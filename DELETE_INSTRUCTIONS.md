# قائمة المسارات للحذف اليدوي - Apex System

## 📋 طريقة الحذف السريع

### الخيار 1: حذف المجلدات فقط (الأسهل) ⚡

افتح File Explorer واحذف هذه المجلدات مباشرة:

#### أ. المجلدات في الجذر (d:\Apex\system\)
```
d:\Apex\system\Apex.Core
d:\Apex\system\Apex.Data
d:\Apex\system\Apex.Services
d:\Apex\system\Apex.UI
```

وهذا الملف:
```
d:\Apex\system\Apex.PrintingSystem.sln
```

#### ب. المجلدات في PrintingSystem (d:\Apex\system\Apex.PrintingSystem\)
```
d:\Apex\system\Apex.PrintingSystem\InstallerOutput
d:\Apex\system\Apex.PrintingSystem\SetupOutput
```

---

## 🗑️ حذف ملفات Build Logs

### الطريقة السريعة:
1. افتح المجلد: `d:\Apex\system\Apex.PrintingSystem`
2. في شريط البحث اكتب: `*.log`
3. حدد الكل (Ctrl+A) واحذف (Delete)
4. كرر نفس الخطوات مع: `*.txt` (لكن **لا تحذف** version.json)

---

## 📝 قائمة الملفات الكاملة للحذف (اختياري)

إذا أردت الحذف الانتقائي، هذه قائمة كاملة:

### ملفات *.log
```
d:\Apex\system\Apex.PrintingSystem\build.log
d:\Apex\system\Apex.PrintingSystem\build_core.log
d:\Apex\system\Apex.PrintingSystem\build_data.log
d:\Apex\system\Apex.PrintingSystem\build_debug.log
d:\Apex\system\Apex.PrintingSystem\build_detailed.log
d:\Apex\system\Apex.PrintingSystem\build_detailed_2.log
d:\Apex\system\Apex.PrintingSystem\build_diag.log
d:\Apex\system\Apex.PrintingSystem\build_err.log
d:\Apex\system\Apex.PrintingSystem\build_services.log
d:\Apex\system\Apex.PrintingSystem\build_sln.log
d:\Apex\system\Apex.PrintingSystem\build_ui.log
d:\Apex\system\Apex.PrintingSystem\build_ui_2.log
d:\Apex\system\Apex.PrintingSystem\build_ui_3.log
d:\Apex\system\Apex.PrintingSystem\msbuild.log
d:\Apex\system\Apex.PrintingSystem\startup.log
d:\Apex\system\Apex.PrintingSystem\viewmodel_error.log
```

### ملفات *.txt
```
d:\Apex\system\Apex.PrintingSystem\build_data.txt
d:\Apex\system\Apex.PrintingSystem\build_data_min.txt
d:\Apex\system\Apex.PrintingSystem\build_error.txt
d:\Apex\system\Apex.PrintingSystem\build_error_detailed.txt
d:\Apex\system\Apex.PrintingSystem\build_error_detailed_2.txt
d:\Apex\system\Apex.PrintingSystem\build_error_services.txt
d:\Apex\system\Apex.PrintingSystem\build_error_services_2.txt
d:\Apex\system\Apex.PrintingSystem\build_error_services_3.txt
d:\Apex\system\Apex.PrintingSystem\build_error_test.txt
d:\Apex\system\Apex.PrintingSystem\build_error_test_2.txt
d:\Apex\system\Apex.PrintingSystem\build_error_test_3.txt
d:\Apex\system\Apex.PrintingSystem\build_error_test_4.txt
d:\Apex\system\Apex.PrintingSystem\build_error_test_5.txt
d:\Apex\system\Apex.PrintingSystem\build_error_ui.txt
d:\Apex\system\Apex.PrintingSystem\build_errors_final.txt
d:\Apex\system\Apex.PrintingSystem\build_errors_final_2.txt
d:\Apex\system\Apex.PrintingSystem\build_errors_final_3.txt
d:\Apex\system\Apex.PrintingSystem\build_errors_migration.txt
d:\Apex\system\Apex.PrintingSystem\build_errors_migration_2.txt
d:\Apex\system\Apex.PrintingSystem\build_errors_migration_3.txt
d:\Apex\system\Apex.PrintingSystem\build_errors_numbered.txt
d:\Apex\system\Apex.PrintingSystem\build_errors_refactor.txt
d:\Apex\system\Apex.PrintingSystem\build_log.txt
d:\Apex\system\Apex.PrintingSystem\build_log_2.txt
d:\Apex\system\Apex.PrintingSystem\build_log_clean.txt
d:\Apex\system\Apex.PrintingSystem\build_log_clean_2.txt
d:\Apex\system\Apex.PrintingSystem\build_log_final.txt
d:\Apex\system\Apex.PrintingSystem\build_log_fix.txt
d:\Apex\system\Apex.PrintingSystem\build_log_ui.txt
d:\Apex\system\Apex.PrintingSystem\build_output.txt
d:\Apex\system\Apex.PrintingSystem\build_output_2.txt
d:\Apex\system\Apex.PrintingSystem\build_services_err.txt
d:\Apex\system\Apex.PrintingSystem\build_services_err_2.txt
d:\Apex\system\Apex.PrintingSystem\build_services_q.txt
d:\Apex\system\Apex.PrintingSystem\build_ui_log.txt
d:\Apex\system\Apex.PrintingSystem\build_ui_log_ascii.txt
d:\Apex\system\Apex.PrintingSystem\build_ui_log_final.txt
d:\Apex\system\Apex.PrintingSystem\build_ui_log_final_2.txt
d:\Apex\system\Apex.PrintingSystem\build_ui_log_full.txt
d:\Apex\system\Apex.PrintingSystem\build_warnings.txt
d:\Apex\system\Apex.PrintingSystem\cli_build_error.txt
d:\Apex\system\Apex.PrintingSystem\cli_run_error.txt
d:\Apex\system\Apex.PrintingSystem\err.txt
d:\Apex\system\Apex.PrintingSystem\errors.txt
d:\Apex\system\Apex.PrintingSystem\publish_err.txt
d:\Apex\system\Apex.PrintingSystem\publish_log.txt
d:\Apex\system\Apex.PrintingSystem\test_output.txt
d:\Apex\system\Apex.PrintingSystem\test_output_2.txt
d:\Apex\system\Apex.PrintingSystem\test_output_3.txt
d:\Apex\system\Apex.PrintingSystem\test_output_4.txt
d:\Apex\system\Apex.PrintingSystem\test_output_5.txt
d:\Apex\system\Apex.PrintingSystem\test_output_6.txt
d:\Apex\system\Apex.PrintingSystem\verification_output.txt
```

**⚠️ لا تحذف:** `version.json`

---

## ✅ الخطوات المقترحة (الأسرع)

### خطوة 1: حذف المجلدات القديمة (دقيقة واحدة)
1. افتح File Explorer
2. اذهب إلى `d:\Apex\system`
3. حدد هذه المجلدات:
   - Apex.Core
   - Apex.Data
   - Apex.Services
   - Apex.UI
4. اضغط Delete أو Shift+Delete (للحذف النهائي)
5. احذف الملف `Apex.PrintingSystem.sln`

### خطوة 2: حذف مجلدات Build Output (30 ثانية)
1. اذهب إلى `d:\Apex\system\Apex.PrintingSystem`
2. احذف المجلدات:
   - InstallerOutput
   - SetupOutput

### خطوة 3: حذف Build Logs (دقيقة واحدة)
1. في نفس المجلد `d:\Apex\system\Apex.PrintingSystem`
2. في شريط البحث اكتب: `*.log`
3. Ctrl+A ثم Delete
4. في شريط البحث اكتب: `build*.txt`
5. Ctrl+A ثم Delete
6. في شريط البحث اكتب: `*error*.txt`
7. Ctrl+A ثم Delete
8. في شريط البحث اكتب: `*output*.txt`
9. Ctrl+A ثم Delete

---

## 📊 النتيجة المتوقعة

بعد الحذف سيكون لديك:

✅ **المشاريع النشطة فقط:**
- Apex.Core (في PrintingSystem)
- Apex.Data (في PrintingSystem)
- Apex.Services (في PrintingSystem)
- Apex.UI (في PrintingSystem)
- Apex.NumberedBooksEngine
- + المشاريع الأخرى النشطة

✅ **المستندات المهمة:**
- README.md
- PROJECT_STRUCTURE.md
- Documentation/

✅ **الملفات المهمة:**
- version.json
- schema.sql

❌ **تم حذف:**
- 4 مشاريع قديمة فارغة
- ~80 ملف log/txt
- 2 مجلد output فارغة

**توفير المساحة:** ~30-40 MB

---

## 🔄 PowerShell بديل (نسخ ولصق)

إذا أردت استخدام PowerShell بشكل أسرع (أمر واحد):

```powershell
# انسخ والصق هذا الأمر كاملاً
cd "d:\Apex\system"; Remove-Item "Apex.Core","Apex.Data","Apex.Services","Apex.UI","Apex.PrintingSystem.sln" -Recurse -Force -ErrorAction SilentlyContinue; cd "Apex.PrintingSystem"; Remove-Item "InstallerOutput","SetupOutput" -Recurse -Force -ErrorAction SilentlyContinue; Remove-Item *.log,build*.txt,*error*.txt,*output*.txt,err.txt,errors.txt,cli*.txt,publish*.txt,test_*.txt -Force -ErrorAction SilentlyContinue; Write-Host "✅ تم الحذف بنجاح!" -ForegroundColor Green
```

---

**اختر الطريقة الأسهل لك!** 🎯
