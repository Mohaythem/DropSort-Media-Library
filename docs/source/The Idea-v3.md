# 🎬 DropSort Media Library — Source of Truth v3

## الفكرة

**DropSort Media Library** هو برنامج Windows Desktop محلي لإدارة مكتبة الأفلام والملفات الحقيقية الموجودة على الجهاز، مع فصل واضح بين:

- **هوية الفيلم**.
- **الملفات المسجلة المرتبطة به**.
- **مكان الملفات وحالتها**.
- **الـMetadata والبوسترات**.
- **الحالة الشخصية للمستخدم** مثل Like / Blacklist / Watchlist / Watch History.
- **عمليات تنظيم الملفات واستعادتها**.

التطبيق **Local-first**: المكتبة نفسها، البحث، التشغيل، التفضيلات، سجل المشاهدة، والتنظيم المحلي لا تعتمد على الإنترنت.

الإنترنت يستخدم فقط عندما تكون هناك حاجة لخدمة خارجية، مثل جلب Metadata أو Poster من TMDB.

الهدف الأساسي:

> أي فيلم يدخل الجهاز يمكن لـDropSort تسجيله محليًا، فهمه وتنظيمه وربطه بالـMetadata عند توفر الإنترنت، ثم عرضه وإدارته وتشغيله من مكتبة محلية مستقرة وآمنة.

---

# 🧭 Product Principles

## 1. Local-first

المكتبة لا تعتمد على TMDB لكي تعمل.

```text
No Internet
↓
Library still opens
Local search still works
Play still works
Like / Blacklist / Watchlist still work
Watch History still works
Filesystem operations still work
```

TMDB هو **enrichment layer** وليس مصدر تشغيل المكتبة.

---

## 2. No Full Library Refresh on Startup

عند فتح DropSort:

```text
Start App
↓
Initialize / migrate local database
↓
Load current local state
↓
Display Library
```

لا يتم عمل filesystem scan شامل للمكتبة عند كل Startup.

لا يتم إعادة بناء كل الكروت أو إعادة جلب كل البيانات من TMDB عند فتح التطبيق.

الهدف:

```text
Startup = Load
not
Startup = Full Refresh
```

أي تغيير صغير في فيلم أو ملف يجب أن يؤدي إلى تحديث هذا العنصر فقط قدر الإمكان، وليس إعادة تحميل المكتبة بالكامل.

هذه قاعدة معمارية أساسية، وليست مجرد تحسين أداء.

---

## 3. Registered File ≠ Currently Available File

وجود `MediaFile` في DropSort يعني أن الملف **مسجل ضمن المكتبة**، وليس أن التطبيق قام للتو بإثبات وجوده على الهارد.

الحالة المنطقية للملف تكون مثل:

```text
Registered MediaFile
├── Available
└── Missing
```

الفيلم يظل داخل المكتبة طالما لديه MediaFile مسجل، حتى لو أصبح الملف مفقودًا.

---

# 🎬 Movie Identity and Library Membership

يتم الفصل بين:

```text
Movie
MediaFile
Personal State
Historical State
Metadata
Filesystem State
```

`MovieId` هو هوية الفيلم المحلية المستقرة.

`MediaFileId` هو هوية سجل ملف معين، ولا يتم استبداله بالـPath.

قاعدة العضوية:

```text
Movie is in the active Library
<=>
Movie has at least one registered MediaFile
```

الحالة `Missing` لا تحذف الـMediaFile تلقائيًا.

---

# ❌ Missing Files — السلوك المعتمد

DropSort **لا يفحص المكتبة كلها تلقائيًا عند Startup** بحثًا عن الملفات المفقودة.

التحقق يكون عند الحاجة.

مثال:

```text
User presses Play / Open Movie
↓
DropSort checks the selected MediaFile
```

لو الملف موجود:

```text
Available
→ Open normally
```

لو الملف غير موجود:

```text
Missing
→ Update only this MediaFile/movie state
→ Keep the movie in Library
```

ويظهر مثلًا:

```text
Inception

⚠ File Missing

Last Known Location:
D:\Movies\Inception (2010)\Inception.mkv
```

Actions المستقبلية:

```text
Locate File
Relink
Check Again
Remove Missing Entry
```

لا يتم حذف الفيلم أو سجل الملف تلقائيًا لمجرد أن المسار غير موجود.

`Check Library` يكون **عملية اختيارية** لفحص المكتبة كلها، وليس شيئًا إجباريًا عند فتح التطبيق.

---

# 🔄 Main Movie Workflow

```text
User selects / DropSort discovers media
   ↓
Local Media Detection
   ↓
Filename Parsing
   ↓
Register local Movie + MediaFile
   ↓
Movie is available in local Library
   ↓
Optional Metadata Matching / Enrichment
   ↓
Optional File Organization
   ↓
SQLite
   ↓
Library UI
```

المهم:

```text
Local registration
!=
Metadata success
```

الفيلم لا يجب أن يفشل في الإضافة فقط لأن TMDB أو الإنترنت فشل.

---

# 📴 Adding Movies Without Internet

لو المستخدم أضاف:

```text
D:\Movies\The.Matrix.1999.1080p.mkv
```

ولا يوجد إنترنت، يمكن لـDropSort إنشاء حالة محلية مبدئية:

```text
MovieId: local stable ID
MediaFileId: local stable ID
Fallback Title: The Matrix
Year Hint: 1999
Metadata: Pending
External TMDB Identity: None
Poster: None / Cached if available
```

ويظهر الفيلم في المكتبة فورًا.

عند عودة الإنترنت:

```text
Pending Metadata
↓
TMDB lookup
↓
Confident Match
→ attach ExternalMovieIdentity
→ store Metadata
→ fetch/cache Poster
```

لو المطابقة غير مؤكدة:

```text
Needs Match
```

ويتم حلها لاحقًا بدون حذف الفيلم من المكتبة.

---

# 🌐 Metadata

الـMetadata ليست شرطًا لوجود الفيلم في المكتبة.

حالات منطقية ممكنة:

```text
Pending
Ready
Failed
NeedsMatch
```

أمثلة للفشل:

```text
No Internet
TMDB Timeout
API Error
Rate Limit
Invalid/Expired Credential
Movie Not Found
Ambiguous Match
```

كلها يجب أن تعني:

```text
Movie registration: SUCCESS
Metadata enrichment: PENDING / FAILED
```

ولا تعني:

```text
Delete Movie
Delete MediaFile
Undo successful local registration
```

---

# 🖼️ Poster State

Poster download مستقل عن Metadata الأساسية.

مثال:

```text
Metadata: Ready
Poster: Pending
```

فشل تحميل الصورة لا يحول الفيلم كله إلى Metadata failure.

بعد تحميل الـPoster يتم الاحتفاظ به في cache محلي حتى تظل المكتبة قابلة للاستخدام Offline.

---

# 🔍 Search

البحث الأساسي داخل DropSort **Local فقط**.

```text
Library Search
Personal Library Search
```

يبحثان في SQLite / local projections.

لا يتم إرسال كل Search query إلى TMDB.

النتيجة:

- البحث سريع.
- يعمل بدون إنترنت.
- لا يوجد network latency أثناء الكتابة.
- لا يوجد اعتماد على API لعرض مكتبة المستخدم.

TMDB search يستخدم فقط في workflows منفصلة مثل:

```text
Add Movies matching
Manual match
Metadata correction
Metadata enrichment
```

---

# 📁 DropSort Filesystem Engine

المحرك مسؤول عن التعامل الآمن مع الملفات الحقيقية.

مسؤول عن:

```text
Move
Rename
Organize
Relink
Verification
Recovery
Undo support foundation
Location tracking
```

الفكرة الأساسية:

```text
authorized intent
→ validation
→ filesystem mutation
→ filesystem verification
→ durable operation state
→ application-state commit
```

لا يتم اعتبار العملية ناجحة لأن filesystem/API call رجع بدون exception فقط.

---

# 🛡️ File Safety

سلامة ملفات المستخدم جزء أساسي من Architecture.

المحرك يجب أن يراعي:

```text
Authorized roots
No silent overwrite
Same-file detection
Physical-file identity
Hard-link aliases
Reparse points
Symlinks / Junctions
Case-only rename
Destination collision
Locked files
Permission failures
Cross-volume operations
Partial failure
Cancellation
Restart recovery
```

Path النصي ليس هو هوية الملف.

```text
MediaFileId
!= Path
!= Physical File Identity
```

---

# 📦 Movie Asset Bundles

عملية تنظيم فيلم لا تعني دائمًا Video واحد فقط.

يمكن أن تكون:

```text
1 Primary Media Asset
+ 0..N Explicit Companion Assets
```

مثال:

```text
The Dark Knight (2008).mkv
The Dark Knight (2008).ar.srt
The Dark Knight (2008).en.srt
movie.nfo
```

تعتبر Logical Operation واحدة إذا الـdiscovery/import layer قررت أن هذه الملفات مرتبطة ببعض.

الـFilesystem Engine نفسه لا يخمن sidecars عشوائيًا.

---

# 📁 Folder Move Policy

القرار النهائي بين:

```text
Move whole folder
```

و:

```text
Move explicit movie asset bundle
```

يتم اتخاذه في مرحلة Discovery / Import.

قاعدة المنتج:

لو Folder يحتوي على فيلم واحد ومجموعة ملفات مرتبطة به فقط، يمكن اعتباره Movie Container.

لكن لو يحتوي على أكثر من فيديو مستقل:

```text
Movie A.mkv
Movie A.srt
Movie B.mkv
Movie B.srt
```

لا يتم نقل الفولدر كله كأنه فيلم واحد.

---

# 🔁 Filesystem Operation Lifecycle

كل عملية ملفات لها هوية وحالة مستقرة.

النموذج المنطقي:

```text
Planned
→ Validated
→ Executing
→ FileSystemVerified
→ Committed
```

وحالات failure/recovery:

```text
Failed
Cancelled
RecoveryRequired
```

أهم قاعدة:

```text
FileSystemVerified
!=
Committed
```

ممكن الملفات تتحرك وتُتحقق بنجاح، ثم يفشل تحديث SQLite.

في هذه الحالة DropSort لا يدعي أن العملية مكتملة، بل يحتفظ بحقيقة أن Recovery مطلوب.

---

# 💽 Same-Volume vs Cross-Volume

النقل داخل نفس الـVolume يختلف عن النقل بين أقراص مختلفة.

مثال Same Volume:

```text
D:\Downloads
→
D:\Movies
```

مثال Cross Volume:

```text
D:\Downloads
→
E:\Movies
```

في Cross Volume، فلسفة الأمان:

```text
Copy
→ Flush
→ Verify destination independently
→ Finalize destination without overwrite
→ Verify final destination
→ Verify source identity
→ Remove source
```

الأصل لا يتم حذفه قبل التأكد من الوجهة.

---

# 🗃️ Persistence

SQLite هي قاعدة البيانات المحلية ومصدر الحقيقة للحالة المسجلة داخل التطبيق.

المفاهيم الأساسية تشمل:

```text
movies
external_movie_identities
media_files
personal_preferences
watchlist_memberships
watch_events
historical_movies
media_file_locations
filesystem_operations
filesystem_operation_assets
schema_migrations
```

الأسماء الفعلية للجداول في تنفيذ Python الحالي يمكن أن تختلف أثناء الترحيل، لكن العقود المنطقية السابقة هي المطلوب الحفاظ عليه.

الـPersistence لا يجب أن يجبر الـUI على Full Reload للمكتبة مع كل تغيير صغير.

يفضل أن تكون التغييرات مرتبطة بهويات مستقرة وتسمح بقراءة/تحديث عنصر محدد.

---

# 🧹 Clear Library Data

`Clear Library Data` تعني إزالة **الحالة النشطة داخل DropSort**.

بعد نجاحها:

```text
Library active state = empty
MediaFiles = 0
Active MediaFile locations = 0
Liked = empty
Blacklisted = empty
Watchlist = empty
Visible Watch History = empty
```

لكن:

```text
Actual movie files on disk = untouched
```

يمكن الاحتفاظ بـHistorical/Tombstone أو Recovery evidence إذا كانت مطلوبة، لكن تظل inert ولا تظهر كـactive cards أو active queries.

---

# 🎞️ Movie Library

واجهة المكتبة تعرض الأفلام المسجلة محليًا.

يتم تحميل الـstate من SQLite.

مثال:

```text
MY LIBRARY

[Poster]       [Poster]       [Poster]

Interstellar   Inception      The Dark Knight
2014           2010           2008
```

الـPosters تأتي من cache محلي إذا كانت موجودة.

لا يتم عمل TMDB request لمجرد فتح المكتبة.

---

# 🎞️ Movie Details

Movie Details يمكن أن تعرض:

```text
Title
Original Title
Year
Overview
Genres
Runtime
External IDs
Poster
Media Files
File Availability
File Size
Quality
Date Added
Personal State
Watch History
```

والـActions:

```text
Play
Open Folder
Like
Blacklist
Watchlist
Mark Watched
Move
Rename
Locate Missing File
Relink
Remove From Library
Open External Page
```

---

# ▶️ Playback

DropSort لا يحتاج Video Player داخلي في البداية.

عند:

```text
Play
```

يستخدم:

```text
Windows Default Player
VLC
MPC-HC
mpv
or configured external player
```

قبل التشغيل يتم فحص الـMediaFile المطلوب فقط.

لو مفقود:

```text
Mark Missing
→ Keep Movie in Library
→ Do not full-refresh Library
```

---

# 👁️ Personal Library

الحالة الشخصية منفصلة عن الـMediaFile state.

تشمل:

```text
Liked
Blacklisted
Watchlist
Watch History
```

لكن `Clear Library Data` يزيل الـactive personal state حسب عقد المنتج.

الـHistorical data وحدها لا تستطيع إعادة الفيلم إلى أي active projection.

---

# 👁️ Watch Tracking

Watch history event-based.

يعني الفيلم يمكن أن يكون له أكثر من Watch Event.

مثال:

```text
Interstellar

Watched:
10 Aug 2026
15 Sep 2026
```

مع stable `WatchEventId`.

---

# 🧠 Matching & Confidence

عند توفر الإنترنت، يمكن لـDropSort مطابقة filename مع TMDB.

مثال:

```text
Interstellar.2014.1080p.mkv

Possible Match:
Interstellar (2014)

Confidence:
99%
```

لو المطابقة غير مؤكدة:

```text
Needs Review
```

ولا يتم اتخاذ filesystem mutation غير آمنة بناءً على match ضعيف.

الفشل في Matching لا يمنع Local registration للملف إذا الـworkflow يسمح بذلك.

---

# 👯 Duplicate Detection

يجب الفصل بين:

```text
Same Movie
```

و:

```text
Same Physical/Exact File
```

يمكن استخدام:

```text
External Metadata ID
MediaFileId
Physical File Identity
File Size
Filename
Hash when needed
```

ولا يتم استخدام Path وحده كهوية.

---

# ↩️ Recovery / Undo Foundation

الـFilesystem Journal يحتفظ بالحقيقة عن عمليات النقل/التنظيم.

يمكن استخدامه لاحقًا لبناء Undo / Recovery.

المعلومات المنطقية تشمل:

```text
Operation ID
Assets
Original locations
Destination locations
Physical identities
Verification evidence
State
Failure / recovery information
```

Undo الكامل UI/workflow ليس شرطًا للمرحلة الأولى، لكن الأساس يجب أن يسمح به.

---

# 🔎 Check Library

`Check Library` عملية اختيارية.

ليست Startup Refresh.

عند تشغيلها يدويًا:

```text
Registered MediaFiles
↓
Filesystem verification
↓
Available / Missing / Changed
```

وتحدث الحالات حسب النتائج.

لكن المستخدم لا يجب أن ينتظر Check Library في كل Startup.

ولا يجب أن يؤدي Progress الخاص بـCheck Library إلى إعادة تحميل المكتبة في كل تحديث.

---

# 💾 Storage Dashboard

Feature لاحقة يمكن أن تعتمد على الـMediaFiles المسجلة:

```text
Movies
Total Size
Drive Usage
Quality Breakdown
Largest Files
Missing Files
```

ولا تحتاج إلى network لكي تعمل على البيانات المحلية المتاحة.

---

# 📚 Collections

يمكن دعم:

```text
Manual Collections
Automatic Metadata-based Collections
```

لكنها ليست جزءًا من الـcore filesystem identity.

---

# 📺 TV Shows

TV Shows هدف مستقبلي بعد Movies MVP.

التصميم المستقبلي يمكن أن يدعم:

```text
Show
Season
Episode
MediaFile
Subtitle/Companion Assets
Episode Watch History
Missing Episodes
```

لكن النسخة الحالية تركز أولًا على Movies.

---

# 🏗️ Current Implementation Direction

التنفيذ المعتمد من الآن هو **مشروع DropSort الحالي المكتوب بـPython**.

الـStack الأساسي:

```text
Python 3
PySide6
SQLite
Windows 11
pytest
PyInstaller
```

ولا يوجد قرار حالي لإعادة كتابة المشروع بـC# أو .NET.

أي عمل سابق على Clean C# Rewrite يعتبر مسارًا متوقفًا وغير معتمد كمصدر التنفيذ الحالي، إلا إذا تم إحياؤه صراحة في المستقبل.

---

# 🧱 Architecture Direction for the Python Project

لن يتم عمل Rewrite جديد من الصفر.

سيتم تطوير وتنظيف مشروع Python الحالي تدريجيًا مع الحفاظ على الأجزاء الصحيحة وإعادة تشكيل الأجزاء المخالفة للعقود الجديدة.

الاتجاه المنطقي:

```text
UI / PySide6
      ↓
Application / Use Cases
      ↓
Domain / Core Models
      ↓
Persistence + Filesystem + External Services
```

الهدف ليس فرض أسماء مجلدات أو Classes محددة من البداية، بل فرض حدود واضحة بين:

```text
UI state
Domain identity
Persistence
Filesystem operations
TMDB integration
Background work
```

يجب منع الـUI من الاعتماد على side effects عشوائية أو global refreshes.

ويجب منع TMDB أو filesystem verification من التحكم في عضوية الفيلم بشكل غير مباشر.

---

# 🔧 Existing Python Project Strategy

المشروع الحالي هو نقطة البداية.

لا نقوم بـ:

```text
Delete everything
Start over
Rebuild every screen
Rewrite every subsystem at once
```

بل نقوم بـ:

```text
Inspect current implementation
↓
Identify correct behavior
↓
Identify conflicting behavior
↓
Preserve working code where safe
↓
Refactor targeted boundaries
↓
Add tests around contracts
↓
Migrate behavior incrementally
```

أي تعديل يجب أن يكون له سبب واضح مرتبط بعقد المنتج أو defect حقيقي.

---

# ⚠️ First Technical Priority — Startup / Refresh Stability

أول مشكلة معمارية يجب حسمها في مشروع Python الحالي هي أي coupling يؤدي إلى:

```text
Startup
→ reconciliation / scan / status update
→ repository reload
→ DTO recreation
→ card rebuild
→ repaint / flicker
```

السلوك المطلوب:

```text
Startup
→ open DB
→ migrate if needed
→ query local library once
→ render current state
```

بعد ذلك:

```text
Background status/progress
→ update only relevant status UI
```

وليس:

```text
Background status/progress
→ refresh entire Library
```

أي تحديث فيلم واحد:

```text
Movie X changed
→ query/update Movie X
→ update Movie X UI
```

وليس:

```text
Movie X changed
→ reload all movies
→ recreate all cards
```

---

# 🧩 Incremental UI Contract

الـUI يجب أن يتعامل مع stable identity.

مثال:

```text
MovieId
→ existing MovieCard
```

إذا تغير:

```text
Poster
Like state
Watchlist state
Availability
Metadata
```

يتم تحديث الكارت المتأثر فقط قدر الإمكان.

إعادة إنشاء جميع الكروت مسموحة فقط في حالات واضحة مثل:

```text
Initial load
Explicit major query/filter reset when necessary
Clear Library Data
Schema/data migration requiring full projection rebuild
```

وليست الاستجابة الافتراضية لكل signal.

---

# 📴 Offline-First Contract

بعد تسجيل الفيلم محليًا، الوظائف الأساسية لا تعتمد على الإنترنت:

```text
Browse Library
Local Search
Play
Open Folder
Like
Blacklist
Watchlist
Watch Tracking
Move / Rename / Organize
Check Library
Storage Analysis
Missing File handling
```

الإنترنت مطلوب فقط للوظائف الخارجية مثل:

```text
TMDB matching
Metadata enrichment
Poster download
Metadata refresh
External links
```

---

# 🚀 Example — Online

يتم إضافة:

```text
The.Departed.2006.1080p.BluRay.mkv
```

DropSort:

1. يسجل الملف محليًا.
2. ينشئ/يربط MovieId.
3. يضيفه إلى المكتبة.
4. يحلل filename.
5. يحاول TMDB matching.
6. يحصل على Metadata إذا نجح.
7. يخزن Metadata/External Identity.
8. يحمل Poster إلى cache.
9. ينظم الملفات بأمان إذا طلب الـworkflow ذلك.
10. يعرض الفيلم في Library.

---

# 📴 Example — Offline

يتم إضافة:

```text
The.Departed.2006.1080p.BluRay.mkv
```

بدون إنترنت:

1. يسجل MediaFile محليًا.
2. ينشئ Movie محلي.
3. يستخرج fallback title/year إن أمكن.
4. يظهر الفيلم في Library.
5. `Metadata = Pending`.
6. لا يتم حذف الفيلم أو رفض إضافته.
7. عند عودة الإنترنت يمكن عمل enrichment لاحقًا.

---

# ❌ Example — Missing on Play

المستخدم يضغط Play:

```text
Interstellar
```

DropSort يتحقق من الـMediaFile المحدد.

لو المسار غير موجود:

```text
MediaFile Availability → Missing
```

والنتيجة:

```text
Movie remains in Library
No global scan
No full Library refresh
Only the affected movie/file state changes
```

---

# 🧪 Testing Direction

الاختبارات يجب أن تغطي عقود المنتج وليس فقط نجاح الدوال منفردة.

أمثلة أساسية:

```text
Movie remains active when registered MediaFile is Missing
Local registration succeeds when TMDB fails
Startup does not require filesystem scan
Startup does not require TMDB
Play checks only selected MediaFile
Missing detection does not delete MediaFile
Check Library is explicit/manual
Clear Library Data does not delete physical files
Historical state does not appear in active queries
MediaFileId remains stable after path change
No silent overwrite
Cross-volume source is not removed before destination verification
Progress updates do not trigger full Library reload
Single-movie update does not require full collection reconstruction
```

الـregression tests الخاصة بالـflicker/refresh مهمة بقدر اختبارات الـfilesystem.

---

# 🎯 Final Vision

**DropSort Media Library** هو مدير مكتبة Media محلي للـWindows يربط:

```text
Stable Movie Identity
        ↓
Registered Physical Media
        ↓
Safe File Management
        ↓
Optional Metadata Enrichment
        ↓
Local Library
        ↓
Personal Tracking
```

الفلسفة:

> ملفات المستخدم ومكتبته المحلية تظل تحت تحكمه حتى بدون إنترنت.

والسلوك المطلوب:

```text
Register locally
→ Enrich when possible
→ Organize safely
→ Track persistently
→ Display locally
→ Play directly
```

بدون اعتماد على refresh شامل عند Startup، وبدون حذف الفيلم تلقائيًا إذا أصبح ملفه مفقودًا، وبدون جعل نجاح TMDB شرطًا لوجود الفيلم في المكتبة.

---

# 🛣️ Development Direction

## المرحلة الأولى — فهم وتصحيح المشروع الحالي

قبل إضافة Features كبيرة جديدة:

```text
Inspect existing Python implementation
↓
Map current data flow
↓
Inspect Startup behavior
↓
Inspect Library loading
↓
Inspect refresh/rebuild mechanisms
↓
Inspect MediaFile persistence
↓
Inspect TMDB coupling
↓
Inspect Check Library behavior
↓
Inspect filesystem operation flow
```

ثم يتم تصنيف كل جزء:

```text
Already correct
Needs modification
Must be removed
Missing entirely
```

---

## المرحلة الثانية — Core Contracts

الأولوية:

```text
Stable Movie / MediaFile identity
SQLite consistency
Registered vs Available separation
Missing file behavior
Clear Library Data
Local-first registration
Incremental repository/update APIs
```

---

## المرحلة الثالثة — Filesystem Safety

```text
Move / Rename safety
Operation lifecycle
Same-volume behavior
Cross-volume behavior
Collision handling
Identity verification
Recovery foundation
Asset bundles
```

---

## المرحلة الرابعة — Metadata / Import

```text
Filename parsing
Local registration
TMDB enrichment
Needs Match
Poster cache
Offline behavior
Duplicate handling
```

---

## المرحلة الخامسة — User Workflows and UI

```text
Library
Movie Details
Add Movies
Personal Library
Watch History
Check Library
Settings
Storage Dashboard
```

مع الالتزام الدائم بـ:

```text
No unnecessary full refresh
Stable widgets/items where possible
Incremental UI updates
No network dependency for local browsing
```

---

## المرحلة السادسة — Reliability / Release

```text
Recovery testing
Crash/restart scenarios
Filesystem stress tests
Performance tests
UI refresh/flicker regression tests
Packaging
PyInstaller
Windows release verification
```

---

# ✅ Source of Truth Rules

هذا الملف هو **Product / Architecture Source of Truth** للاتجاه الحالي لـDropSort.

عند وجود تعارض بينه وبين مستندات أقدم:

```text
The Idea-v3.md wins
```

خصوصًا ضد أي مستند قديم يفترض:

```text
C# rewrite
.NET
WinUI 3
Startup full reconciliation
Automatic deletion of missing media
TMDB as required for registration
Full Library reload after every change
```

لكن هذا الملف لا يعني أن كل implementation detail الحالي يجب حذفه.

القاعدة:

```text
Preserve correct existing Python code
Change only what conflicts with the product contracts or contains a concrete defect
```

---

# 🔒 Non-Negotiable Principles

```text
Python / PySide6 remains the active implementation
Local-first
Stable identity
Registered file != currently available file
Missing does not mean deleted
TMDB is optional enrichment
Startup = load, not full refresh
Check Library is explicit/manual
Incremental updates
No unnecessary full UI reconstruction
Safe filesystem operations
No silent overwrite
Explicit recovery state
Clear Library Data never deletes physical movie files
Historical state is inert
SQLite is the local source of recorded application state
```

---

# 📌 Current Immediate Goal

الهدف المباشر ليس إضافة Feature جديدة أو إعادة تصميم الواجهة.

الهدف هو:

> جعل مشروع Python الحالي يطابق هذه العقود بشكل موثوق، مع إعطاء الأولوية لإزالة أسباب الـfull refresh / UI flicker وفصل Startup عن filesystem reconciliation وTMDB.

بعد تثبيت هذا الأساس، يتم استكمال Features المنتج فوق قاعدة مستقرة.
