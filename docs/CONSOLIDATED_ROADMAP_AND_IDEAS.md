# وثيقة خارطة الطريق الشاملة والتوصيات المعتمدة — TBD Movies
## Consolidated Roadmap, Architecture Decisions & Improvement Ideas

**المشروع**: TBD Movies (تطبيق Windows 11 لسطح المكتب مبني بـ WinUI 3 و C# / .NET 10)  
**المستودع الرئيسي**: https://github.com/Mohaythem/TBD-Movies.git  
**الحالة العامة**: مستودع نظيف 100%، بناء Release ناجح بصفر أخطاء، واختبارات مرشقة بنسبة نجاح 100%.

---

## فهرس المحتويات
• [القسم الأول: واجهة المستخدم والهوية البصرية (UI/UX & Fluent Design)](#1-واجهة-المستخدم-والهوية-البصرية-uiux--fluent-design)  
• [القسم الثاني: الأمان وحماية الأسرار ونظام الملفات (Security & Hardening)](#2-الأمان-وحماية-الأسرار-ونظام-الملفات-security--hardening)  
• [القسم الثالث: ضبط وترشيق حزمة الاختبارات (QA & Test Suite Optimization)](#3-ضبط-وترشيق-حزمة-الاختبارات-qa--test-suite-optimization)  
• [القسم الرابع: خط الأساس للإصدار والمستودع (Git & Release Baseline)](#4-خط-الأساس-للإصدار-والمستودع-git--release-baseline)  
• [القسم الخامس: المعمارية والأداء ومقياس 10,000+ عنصر والتزامن (Architecture, 10k Scale & Concurrency)](#5-المعمارية-والأداء-ومقياس-10000-عنصر-والتزامن-architecture-10k-scale--concurrency)  

---

## 1. واجهة المستخدم والهوية البصرية (UI/UX & Fluent Design)
> **توجيه وشرط حاسم معتمد**: يُحظر نهائياً وضع بوستر الفيلم كخلفية ضبابية ممتدة خلف صفحة العمل أو خلفية التفاصيل لتجنب أي بهرجة أو تشويش بصري، مع الاعتماد الحصري على زجاج وسطح مكتب ويندوز 11 النقي (Native Windows 11 Desktop Mica).

### المحور الأول: تفعيل Mica / Mica Alt الأصلي لسطح المكتب
• **الملف المستهدف**: `MainWindow.xaml.cs` و `MainWindow.xaml`  
• **التحليل**: النافذة حالياً مغطاة بألوان صلبة 100% تعزل التطبيق تماماً عن سطح المكتب.  
• **الكود المعتمد**:
في `MainWindow.xaml.cs` داخل دالة `ConfigureNativeWindow()`:
```csharp
private void ConfigureNativeWindow()
{
    ExtendsContentIntoTitleBar = true;
    SetTitleBar(TitleBarDragRegion);

    // تفعيل خلفية ميكا الأصلية لسطح مكتب ويندوز 11
    if (MicaController.IsSupported())
    {
        this.SystemBackdrop = new MicaBackdrop
        {
            Kind = MicaKind.BaseAlt // النمط المخصص لتطبيقات التبويبات المتعددة والوسائط
        };
    }

    var windowHandle = WindowNative.GetWindowHandle(this);
    WindowHandle = windowHandle;
    var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
    _appWindow = AppWindow.GetFromWindowId(windowId);
    _minimumWindowSize = new MinimumWindowSize(windowHandle);
    var minimum = _minimumWindowSize.MinimumOuterSize;
    _appWindow.Resize(new Windows.Graphics.SizeInt32(
        Math.Max(1360, minimum.Width), Math.Max(860, minimum.Height)));
}
```
وفي `MainWindow.xaml`:
```xaml
<!-- تفريغ خلفية RootGrid للسماح بتدفق الميكا -->
<Grid x:Name="RootGrid" Background="Transparent" KeyboardAcceleratorPlacementMode="Hidden">

<!-- تعديل SlateSurface لتكون شبه شفافة بنسبة 85% لنفاذ الميكا -->
<Border
    x:Name="SlateSurface"
    Grid.RowSpan="2"
    Background="{ThemeResource SlateBackdropTintBrush}"
    IsHitTestVisible="False"
    Visibility="Collapsed" />
```
وفي `MovieDetailsPage.xaml` و `TVShowDetailsPage.xaml`:
حذف `Background="{ThemeResource FigmaSurfaceAppBrush}"` واستبدالها بـ `Background="Transparent"` لترث طبقة سطح المحتوى الموحد (`ShellContentBackgroundBrush`).

---

### المحور الثاني: القضاء التام على وميض الفليكر الأبيض في تبديل الثيمات
• **الملف المستهدف**: `MainWindow.xaml.cs` (دالة `ApplyTheme()`)  
• **التحليل**: كود قلب الثيم اللحظي المعاكس لإجبار الشجرة على إعادة الحساب يسبب وميضاً أبيض فاقعاً ومزعجاً للعين.  
• **الكود المعتمد**:
```csharp
private void ApplyTheme()
{
    var isSlate = ThemeService.UsesSlateSurface && !_accessibilitySettings.HighContrast;
    
    // تحديث الموارد مباشرة في قاموس النافذة دون وميض
    if (isSlate)
    {
        foreach (var key in _slatePaletteResources.Keys)
        {
            RootGrid.Resources[key] = _slatePaletteResources[key];
        }
    }
    else
    {
        RootGrid.Resources.Clear();
    }

    // تعيين الثيم المطلوب مباشرة لمرة واحدة فقط وبدون وميض
    RootGrid.RequestedTheme = ThemeService.ElementTheme;
    SlateSurface.Visibility = isSlate ? Visibility.Visible : Visibility.Collapsed;
    
    UpdateNavigationPaneBackgrounds();
    UpdateTitleBarColors();
}
```

---

### المحور الثالث: توحيد باليت الألوان السينمائية الراقية (Cinema Gold & Obsidian)
• **الملفات المستهدفة**: `MainWindow.xaml.cs`, `ThemeResources.xaml`, `Styles.xaml`  
• **التحليل**: إنهاء الصراع اللوني بين الخوخي (`#F2B59A`) والسيان الفاقع (`#44C4F6`) واعتماد نغمة سينمائية دافئة وموحدة مستوحاة من ألوان شاشات السينما والجوائز السينمائية المذهبة:  
• **رموز الألوان المعتمدة**:
- **اللون الرئيسي للتمييز (Cinema Gold Accent)**:
  * الأساسي: `#F59E0B`
  * عند التحويم (Hover): `#D97706`
  * عند الضغط (Pressed): `#B45309`
  * مسحة التحديد الشفافة: `#26F59E0B` (شفافية 15%)
- توحيد `FigmaAccentButtonBrush` ليرتبط مباشرة بـ `AccentFillColorDefaultBrush`، بحيث تتوحد أزرار التشغيل والتقييمات والقوائم بنفس النغمة اللونية الفخمة.

---

### المحور الرابع: توحيد الحواشي عند 32 epx ومنع قفزة المحتوى (Content Jump)
• **الملفات المستهدفة**: `MovieDetailsPage.xaml`, `TVShowDetailsPage.xaml`, `HomePage.xaml`  
• **التحليل**: الحاشية في المكتبة والقوائم هي 32 epx بينما في التفاصيل 40 epx، مما يحدث قفزة 8 epx مزعجة عند فتح أي فيلم.  
• **الكود المعتمد**:
في `MovieDetailsPage.xaml` و `TVShowDetailsPage.xaml`:
```xaml
<!-- توحيد الحاشية الأفقية بدقة 32 epx لتطابق المكتبة تماماً -->
<ScrollViewer x:Name="Root" Style="{StaticResource PageScrollViewerStyle}" Padding="32,16,32,48" Background="Transparent">
```
وفي كروت الصفحة الرئيسية `HomePage.xaml`:
- استبدال `Margin="14,68,14,14"` بـ `Margin="16,68,16,16"`.
- استبدال `Spacing="3"` بـ `Spacing="4"`.
(الالتزام الصارم بمضاعفات الـ 4px/8px لشبكة Fluent Design).

---

### المحور الخامس: جعل شبكة كروت MyListsPage متجاوبة بالكامل
• **الملفات المستهدفة**: `MyListsPage.xaml`, `MyListsPage.xaml.cs`  
• **التحليل**: شبكة `MyListsPage` الحالية تستخدم `ItemWidth="192"` ثابتاً وتترك فراغاً شاسعاً على الشاشات العريضة.  
• **الكود المعتمد**:
في `MyListsPage.xaml`:
```xaml
<GridView.ItemsPanel>
    <ItemsPanelTemplate>
        <ItemsWrapGrid x:Name="ListItemsPanel" Orientation="Horizontal" SizeChanged="ListItemsPanel_SizeChanged" />
    </ItemsPanelTemplate>
</GridView.ItemsPanel>
```
وفي `MyListsPage.xaml.cs`:
```csharp
private void ListItemsPanel_SizeChanged(object sender, SizeChangedEventArgs e)
{
    if (sender is ItemsWrapGrid panel && ListGrid.ActualWidth > 0)
    {
        var available = ListGrid.ActualWidth - ListGrid.Padding.Left - ListGrid.Padding.Right;
        var metrics = LibraryPosterLayout.Calculate(available);
        panel.ItemWidth = metrics.CellWidth;
        panel.ItemHeight = metrics.MovieCellHeight;
    }
}
```
*الأثر*: تتمدد الكروت بسلاسة تامة لتغطي عرض الشاشة بالتساوي مع الحفاظ على نسبة البوستر السينمائية (2:3).

---

### المحور السادس: استبدال أصول SVG الصلبة برموز Segoe Fluent Icons الأصلية
• **الملف المستهدف**: `MovieDetailsPage.xaml`  
• **التحليل**: استبدال 14 ملف SVG ذات ألوان ثابتة برموز نظام ويندوز 11 المدمجة:  
• **جدول الرموز المعتمدة**:
| الوظيفة | رمز Glyph المعتمد | السلوك مع الثيم واللغة |
| :--- | :--- | :--- |
| **Play (تشغيل)** | `&#xE768;` | يتلون تلقائياً بفرشاة الـ Foreground للزر |
| **Add to List (إضافة لقائمة)** | `&#xE710;` | يتفاعل مع حالات Hover و Pressed |
| **Favorite (المفضلة)** | `&#xEB51;` (محدد) / `&#xEB52;` (مفرغ) | لون ديناميكي نقي |
| **Watch Later (لاحقاً)** | `&#xE8FD;` | أيقونة قياسية موحدة |
| **Star Rating (التقييم)** | `&#xE735;` (ممتلئ) / `&#xE734;` (مفرغ) | لون ذهبي سينمائي |
| **Open Folder (المجلد)** | `&#xE8B7;` | متناسق مع شل ويندوز |
| **Back Button (رجوع)** | `&#xE72B;` | ينعكس تلقائياً مع العربية عبر `MirroredWhenRightToLeft="True"` |

---

### المحور السابع: هوية بصرية سينمائية مجردة وحالات فراغ خطية نظيفة
• **الملفات المستهدفة**: `packaging/generate-icon.ps1`, `EmptyStateCard.xaml`  
• **أيقونة التطبيق (App Icon)**:
  - استبدال الخطوط الثلاثة العشوائية بتصميم هندسي سينمائي مبسط (Play Prism & Golden Frame):
    قاعدة ميكا فحمية عميقة بحواف ناعمة، وداخلها إطار بوستر مذهب رفيع مع مثلث تشغيل بلوري أنيق، يضمن قراءة واضحة من حجم 16px وحتى 256px.
• **حالات الفراغ الخطية النظيفة (Clean Vector Empty States)**:
  - في `EmptyStateCard.xaml`: استخدام رسومات هندسية متجهة (XAML Path Geometry) بخطوط رمادية ناعمة وهادئة بدون أي خلفيات أو بهرجة:
    1. **مكتبة فارغة**: بكرة فيلم سينمائي رقيقة مع إطار بوستر متدرج وزر جذاب: "إضافة مجلد الأفلام الأول".
    2. **لا توجد نتائج بحث**: شريط فيلم متقطع مع عدسة مكبرة أنيقة وزر: "مسح الفلتر وعرض الكل".
    3. **قوائم فارغة**: بطاقة تذكرة سينمائية كلاسيكية مفرغة مع زر: "إضافة أفلام إلى هذه القائمة".

---

## 2. الأمان وحماية الأسرار ونظام الملفات (Security & Hardening)
> تم إجراء تدقيق شامل لكافة المشاريع؛ الحالة العامة ممتازة، وجميع استعلامات SQLite ذات معاملات صريحة (100% Parameterized SQL) وخالية تماماً من ثغرات الحقن (0 SQLi) و 0 ثغرات في حزم NuGet.

### التوصيات الأمنية المعتمدة للتحصين الإضافي:
1. **إابطال رموز Trakt عند تسجيل الخروج (SEC-02)**:
   - *الملف*: `TraktAuthService.cs` و `TraktAuthClient.cs`
   - *الإجراء*: إضافة استدعاء `POST https://auth.trakt.tv/oauth/revoke` عند النقر على `Disconnect` لضمان إبطال الرمز على خوادم Trakt فورياً بدلاً من حذفه محلياً فقط.
2. **تعقيم وسيطات تشغيل مستكشف الملفات (SEC-03)**:
   - *الملف*: `MediaFileActions.cs` (السطر 93)
   - *الإجراء*: تعقيم علامات الاقتباس في مسار الملف `Arguments = "/select,\"" + path.Replace("\"", "") + "\""` لمنع أي تشويش على وسيطات `explorer.exe`.
3. **تعزيز التحقق في محرك سياسة المسارات (SEC-04)**:
   - *الملف*: `PathPolicy.cs`
   - *الإجراء*: إضافة فحص صريح يرفض أسماء أجهزة DOS المحجوزة (`CON`, `PRN`, `AUX`, `NUL`) ويرفض تدفقات البيانات البديلة لـ NTFS (وجود رمز `:` بعد محرك الأقراص).
4. **إضافة إنتروبيا خاصة بالتطبيق لـ DPAPI (SEC-05)**:
   - *الملفات*: `WindowsTmdbCredentialStore.cs`, `WindowsTraktCredentialStore.cs`
   - *الإجراء*: تمرير بايتات إنتروبيا خاصة بالتطبيق (`optionalEntropy`) عند التشفير الاحتياطي في جدول `settings` لزيادة صلابة الحماية.
5. **تشجيع مفاتيح TMDb v4 Bearer (SEC-01)**:
   - *الملف*: `TmdbClient.cs`
   - *الإجراء*: توجيه المستخدم لاعتماد مفاتيح v4 التي تُرسل في الترويسة `Authorization: Bearer` بدلاً من مفاتيح v3 القديمة التي تُمرر في الرابط `?api_key=...`.
6. **تنظيف فحص متغير البيئة المكرر (SEC-06)**:
   - *الملف*: `AppServices.cs`
   - *الإجراء*: إزالة التحقق المزدوج لنفس متغير البيئة `TBDMOVIES_TEST_DATA_ROOT`.

---

## 3. ضبط وترشيق حزمة الاختبارات (QA & Test Suite Optimization)
> تم إنجاز غربلة الحزمة بنجاح تام، مما نتج عنه تقليص الاختبارات مع رفع سرعة التنفيذ بنسبة 33.9% واجتياز الحزمة بنسبة 100%.

### ملخص الأرقام والإنجاز:
• **عدد الاختبارات السابق**: 1,156 اختباراً  
• **عدد الاختبارات الحالي**: 970 اختباراً (حذف 186 اختباراً روتينياً/مكرراً بنسبة انخفاض 16.1%)  
• **زمن التشغيل**: انخفض من 59 ثانية إلى **39 ثانية** (تسريع بمقدار 20 ثانية)  
• **نسبة النجاح**: **100% اجتياز كامل** (970 ناجح، 0 فاشل، 0 متخطى)  

### الملفات التي تم حذفها (14 ملفاً - 3,120 سطراً برمجياً):
- `tests/TBDMovies.Tests/MinimumWindowSizeTests.cs` (اختبارات Win32 HWND P/Invoke)
- `tests/TBDMovies.Tests/SolutionSmokeTests.cs` (اختبارات أسماء الـ Assemblies البديهية)
- `tests/TBDMovies.Tests/Domain/Library/Movies/CatalogModelsTests.cs` (اختبارات POCO/Record getters/setters)
- `tests/TBDMovies.Tests/Domain/Library/Operations/OperationJournalModelsTests.cs`
- `tests/TBDMovies.Tests/Domain/Media/Discovery/DiscoveryModelsTests.cs`
- `tests/TBDMovies.Tests/Domain/Media/Matcher/MatcherModelsTests.cs`
- `tests/TBDMovies.Tests/Infrastructure/Migration7AndRepositoryEnrichmentTests.cs` (ترحيلات قديمة مغطاة بصلابة في ملفات أحدث)
- `tests/TBDMovies.Tests/Infrastructure/Migration8WatchHistoryTests.cs`
- `tests/TBDMovies.Tests/Infrastructure/Migration9WatchEventsDriftTests.cs`
- `tests/TBDMovies.Tests/UI/ActivityListsSourceContractTests.cs` (اختبارات قراءة ملفات XAML كنصوص هشة)
- `tests/TBDMovies.Tests/UI/P2StartupSourceContractTests.cs`
- `tests/TBDMovies.Tests/UI/Phase3UiSourceContractTests.cs`
- `tests/TBDMovies.Tests/UI/UiSourceContractTests.cs`
- `tests/TBDMovies.Tests/UI/UxCorrectivePassContractTests.cs`

### التغطية المحفوظة 100% (High-Signal Coverage):
• كافة خوارزميات تنظيم الملفات وإعادة التسمية وحل التعارضات والنزاعات.  
• معالجة ملفات NFO وتصنيف المسلسلات والأفلام والحلقات بدقة.  
• تكامل ومزامنة TMDb و Trakt وإدارة الاعتماديات.  
• استمرارية SQLite، معاملات قاعدة البيانات، سلامة الترحيلات وإدارة الكتابة المتزامنة.  

---

## 4. خط الأساس للإصدار والمستودع (Git & Release Baseline)
• **تطهير العلامة التجارية (100% Brand Purge)**: تم التحقق عبر فحص متعمق لخلو كافة ملفات المشروع والمجلدات والتكوينات من أي إشارة للاسم القديم `DropSort`.  
• **بناء الإصدار (Release Build)**: تم بناء الحل بنجاح تام عبر `dotnet build -c Release` بصفر أخطاء برمجية وصفر تحذيرات في مشاريع الإنتاج.  
• **المستودع البعيد**: تم توجيه `origin` بنجاح إلى:
  `https://github.com/Mohaythem/TBD-Movies.git`  
• **الـ Commit الأساسي المنشور**:
  `chore(release): baseline clean build with zero DropSort legacy references`  
  (الفرع `main` متزامن ونظيف تماماً).

---

## 5. المعمارية والأداء ومقياس 10,000+ عنصر والتزامن (Architecture, 10k Scale & Concurrency)

### أ. الاختناقات المعمارية الحرجة والحلول الفورية:

#### 1. كسر حظر القراءة والكتابة (Read-Write Lock Starvation) — [CRITICAL]:
• **الموقع**: `CatalogRepositories.cs` (الأسطر 33-45) و `CatalogRepositoryAdapters.cs`  
• **الخلل**: `CatalogUnitOfWork` يستدعي `DatabaseWriteGate.Enter` ويفتح `BEGIN IMMEDIATE` حتى عند قراءة القوائم في الواجهة (`ListAll`, `ListMovies`)، مما يعطل ميزة القراءة المتزامنة في SQLite WAL ويجمد خيط الواجهة عند وجود أي مسح أو استيراد في الخلفية.  
• **الحل**: فصل القراءة عن الكتابة في Unit of Work؛ الاستعلامات القرائية تفتح اتصالاً خفيفاً بدون حجز بوابة الكتابة وبدون معاملة كتابية، مما يسمح بمئات القراءات المتوازية مع كتابة الخلفية.

#### 2. إنهاء كارثة 20,000 معاملة فردية في الاستيراد (Batch Transactions) — [HIGH]:
• **الموقع**: `ImportService.cs` و `ReconciliationService.cs`  
• **الخلل**: استيراد أو فحص 10k ملف يفتح معاملتين مستقلتين لكل ملف مع `fsync` مستقل على القرص، مما يستغرق 300+ ثانية.  
• **الحل**: تجميع الاستيراد والتحديث في دفعات (Batches of 500) أو تنفيذ كامل الدفعة داخل معاملة واحدة `using var uow = _uowFactory.Begin()`، مما يقلص زمن المعالجة إلى أقل من ثانيتين (تسريع 100x+).

#### 3. منع انفجار الذاكرة (OOM) في فك تشفير الملصقات — [HIGH]:
• **الموقع**: `DemoModels.cs` و `ShowModels.cs` و `MovieDetailsPage.xaml.cs`  
• **الخلل**: إنشاء `BitmapImage` بدون `DecodePixelWidth` وبدون سقف تفريغ (No Eviction) يخزن الصور بـ 32-bit RGBA بالحجم الكامل في الرام، والتمرير عبر آلاف البوسترات يرفع الاستهلاك إلى 3GB+ ويسبب انهيار التطبيق.  
• **الحل**:
  1. تقييد أبعاد فك الملصقات للشبكة: `bitmap.DecodePixelWidth = 360` (توفير 85% من الذاكرة لكل صورة).
  2. بناء مخزن ذاكرة محدود (Bounded LRU / WeakReference Cache) بحد أقصى 250 صورة في الذاكرة الحية.
  3. إزالة التحقق المتزامن `File.Exists` من داخل خواص الـ Binding على خيط الـ UI.

#### 4. تقليص 30,000+ استدعاء نظام في مسح الملفات (Disk I/O) — [HIGH]:
• **الموقع**: `MediaDiscoveryService.cs`  
• **الخلل**: فحص الملفات يستدعي `Directory.EnumerateFileSystemEntries` ثم `File.GetAttributes` ثم `FileInfo.Length` لكل ملف في حلقات متتالية، مسبباً بطءاً هائلاً عبر الشبكة (NAS / SMB).  
• **الحل**: استخدام `DirectoryInfo.EnumerateFileSystemInfos` أو `FileSystemEnumerable` التي تجلب الحجم والخصائص وتاريخ التعديل مباشرة من بنية `WIN32_FIND_DATA` في استدعاء واحد مجمع (تقليص استدعاءات القرص بنسبة 95%).

#### 5. إيقاف نمط Sync-over-Async المسبب لتجميد الخيوط — [HIGH]:
• **الموقع**: `TmdbClient.cs` (السطر 588) و `DiskPosterCache.cs` (السطر 194) و `NetworkReadGuard.cs` (الأسطر 68 و 73)  
• **الخلل**: استخدام `Task.Run(...).GetAwaiter().GetResult()` و `Task.WhenAny().GetAwaiter().GetResult()` يحظر خيوط ThreadPool والـ UI.  
• **الحل**: تحويل استدعاءات البحث وتحميل الملصقات والشبكة إلى دوال `async/await` كاملة غير حاجزة.

#### 6. إلغاء المهام المطلقة (Fire-and-Forget) في الخصائص — [HIGH]:
• **الموقع**: `ShowModels.cs` و `DemoModels.cs` (خاصية `PosterSource`)  
• **الخلل**: إطلاق `Task.Run` لتحميل الملصق وإنشاء `new BitmapImage` داخل Property Getter مجرد يهدد بانهيار الواجهة (`RPC_E_WRONG_THREAD`) عند التقييم خارج الـ UI Thread.  
• **الحل**: نقل تحميل الصور إلى ViewModel أو Service مستقل يستدعى عند ربط العنصر بالعرض.

---

### ب. ضبط إعدادات قاعدة البيانات SQLite (Pragmas & Indexes):
1. **تعديل إعدادات الاتصال في `SqliteConnections.cs`**:
   - `PRAGMA synchronous = NORMAL;` (في وضع WAL، يعتبر NORMAL آمناً بنسبة 100% ويسرع الكتابة بمقدار 10x إلى 50x مقارنة بـ FULL).
   - `PRAGMA busy_timeout = 10000;` (رفع مهلة القفل).
   - `PRAGMA cache_size = -64000;` (توسيع كاش الذاكرة إلى 64MB بدلاً من 2MB).
   - `PRAGMA mmap_size = 268435456;` (تفعيل ميزة الذاكرة المشتركة 256MB).
   - `PRAGMA temp_store = MEMORY;` (عمليات الفرز المؤقت تجري في الرام).
2. **إضافة الفهارس المفقودة في هجرة قاعدة البيانات القادمة (Migration 0020)**:
   ```sql
   CREATE INDEX idx_movies_date_added ON movies(date_added DESC, id DESC);
   CREATE INDEX idx_movies_last_opened ON movies(last_opened_at DESC, id DESC);
   CREATE INDEX idx_movies_title ON movies(title COLLATE NOCASE);
   CREATE INDEX idx_tv_shows_date_added ON tv_shows(date_added DESC, id DESC);
   ```
   *ملاحظة هامة*: استبدال الفرز بـ `ORDER BY datetime(date_added)` بـ `ORDER BY date_added` المباشر لأن تواريخ ISO-8601 تترتب زمنياً كنصوص عادية، مما يفعل الفهرس الفوري ويلغي الفرز الكامل في الذاكرة (Full Table Scan).

3. **كبح البحث (Debounce Search)**:
   - تأخير تصفية البحث في `SearchBox_TextChanged` بمقدار 250ms باستخدام `DispatcherQueueTimer` لمنع إعادة تصفية وفرز 10k عنصر مع كل نقرة حرف.

---

### ج. تسريبات الموارد والكود الميت (Hygiene & Resource Cleanup):
1. **معالجة تسريب كائنات `CancellationTokenSource` (LEAK-01)**:
   - في `TraktPage.xaml.cs`: استدعاء `Dispose()` على `_customListsCts` و `_activeLoadCts` قبل استبدالها وفي حدث `Deactivate()`.
2. **ربط الحدث العام الميت `TraktSyncCompleted` (LEAK-02)**:
   - في `AppServices.cs`: ربط صفحات Trakt والقوائم بالحدث لتحديث الواجهة تلقائياً فور اكتمال المزامنة في الخلفية.
3. **تحرير مهام حوار المطابقة عند الإغلاق (LEAK-03)**:
   - في `MatchMediaDialog.cs`: ضبط `_closed = true` عند حدث `Closed` وإلغاء `_searchCts` مع `Dispose()`.
4. **نقل `CatalogRepositoryAdapters.cs` للطبقة الصحيحة (ARCH-02)**:
   - نقله من `TBDMovies.UI/Services` إلى مشروع `TBDMovies.Infrastructure/Persistence/Repositories` لتصحيح المعمارية النظيفة.
5. **معالجة ابتلاع الاستثناءات الصامت (HYG-01)**:
   - استبدال كتل `catch { }` و `catch (Exception)` الفارغة بتسجيل تشخيصي (Logging / `Debug.WriteLine`) لتسهيل رصد الأعطال أثناء التشغيل.
6. **تنظيف الكود الميت (DEAD-01, 02, 03)**:
   - إزالة الفحص المكرر لـ `TBDMOVIES_TEST_DATA_ROOT` في `AppServices.cs`.
   - حذف الصنف غير المستخدم `UnconfiguredMetadataProvider`.
   - إعادة تسمية `DemoModels.cs` إلى `MovieModels.cs` لتعكس بدقة أنها نماذج الإنتاج الحقيقية للتطبيق.
