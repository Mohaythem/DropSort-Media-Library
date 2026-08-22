# 🎬 DropSort Media Library

## الفكرة

**DropSort Media Library** هو برنامج Desktop محلي للـ Windows يجمع بين:

- **File Organizer** لتنظيم الملفات تلقائيًا.
    
- **Local Movie & TV Library** شبيهة بفكرة Letterboxd/Plex لكن مبنية حول الملفات الموجودة فعليًا على الجهاز.
    
- **Media Tracker** لتسجيل الأفلام والمسلسلات والمشاهدة والتقييمات.
    
- **Storage Manager** لمعرفة مكان وحجم وجودة الـ Media الموجودة على الهاردات.
    

الهدف الأساسي:

> أي فيلم أو مسلسل يدخل الجهاز، البرنامج يكتشفه، يفهمه، ينظمه، يربطه بمعلوماته، ثم يضيفه تلقائيًا إلى مكتبة محلية يمكن البحث والتصفح والتشغيل منها.

---

# 🔄 Main Workflow

```text
New File
   ↓
File Watcher
   ↓
Media Detection
   ↓
Filename Parsing
   ↓
Movie / TV Matching
   ↓
Metadata Lookup
   ↓
Safety / Confidence Check
   ↓
File Organization
   ↓
SQLite Database
   ↓
Media Library
```

مثال:

```text
Downloads/
└── The.Dark.Knight.2008.1080p.BluRay.x264.mkv
```

البرنامج يستنتج:

```text
Title: The Dark Knight
Year: 2008
Type: Movie
Quality: 1080p
Source: BluRay
Codec: x264
```

ويقترح:

```text
Current:
C:\Downloads\The.Dark.Knight.2008.1080p.BluRay.x264.mkv

Destination:
D:\Movies\The Dark Knight (2008)\The Dark Knight (2008).mkv
```

ثم يضيف الفيلم تلقائيًا إلى الـ Library.

---

# 📁 DropSort Engine

DropSort هو المحرك المسؤول عن التعامل الفعلي مع الملفات.

مسؤول عن:

- Watch folders.
    
- Detect new files.
    
- Detect finished downloads.
    
- Rename.
    
- Move.
    
- Organize.
    
- Detect duplicates.
    
- Match subtitles.
    
- Track file locations.
    
- Undo operations.
    
- Detect missing files.
    

يجب فصل الـ File Engine عن الـ Media Library بحيث يمكن مستقبلًا استخدام DropSort لتنظيم:

```text
Movies
TV Shows
Music
Documents
Images
Archives
Projects
```

بدون ارتباط كامل بالـ Media Module.

---

# 🎬 Movies

عند اكتشاف فيلم:

```text
Interstellar.2014.1080p.BluRay.mkv
```

يتم استخراج:

```text
Title
Year
Quality
Resolution
Codec
Source
```

ثم البحث عن الفيلم في Movie Metadata API.

يتم تخزين:

```text
Title
Original Title
Year
Poster
Overview
Genres
Runtime
Director
Cast
Rating
External IDs
File Path
File Size
Quality
Date Added
Watch Status
User Rating
```

---

# 📺 TV Shows

مثال:

```text
Better.Call.Saul.S03E05.1080p.mkv
```

يتم اكتشاف:

```text
Show: Better Call Saul
Season: 03
Episode: 05
```

ثم التنظيم:

```text
TV Shows/
└── Better Call Saul/
    └── Season 03/
        ├── Better Call Saul - S03E01.mkv
        ├── Better Call Saul - S03E02.mkv
        └── Better Call Saul - S03E05.mkv
```

البرنامج يستطيع كذلك اكتشاف الحلقات الناقصة:

```text
Season 03

E01 ✓
E02 ✓
E03 ✓
E04 ✓
E05 ✓
E06 ❌ Missing
E07 ✓
```

---

# 📝 Subtitles

اكتشاف ملفات:

```text
.srt
.ass
.vtt
```

ومحاولة ربطها بالفيلم أو الحلقة الصحيحة.

مثال:

```text
The Dark Knight (2008)/
├── The Dark Knight (2008).mkv
├── The Dark Knight (2008).ar.srt
└── The Dark Knight (2008).en.srt
```

داخل الـ Library:

```text
Subtitles

Arabic  ✓
English ✓
```

إذا لم يستطع البرنامج تحديد الفيلم:

```text
Unmatched Subtitle

random_subtitle.srt

Possible Match:
The Dark Knight (2008)

[ Match ]
```

---

# 🌐 Metadata

البرنامج لا يحتاج إلى تحميل قاعدة بيانات أفلام كاملة.

يستخدم External API للحصول على معلومات الفيلم عند اكتشافه.

بعد الحصول عليها يتم تخزينها في Local Cache.

بالتالي التصميم:

**Offline-first**

الإنترنت مطلوب أساسًا من أجل:

- Metadata lookup.
    
- Poster download.
    
- Rating refresh.
    
- External links.
    

أما الوظائف الأساسية فتعمل Offline.

---

# 🖼️ Media Library

واجهة شبيهة بمكتبات Streaming:

```text
MY LIBRARY

[Poster]       [Poster]       [Poster]

Interstellar   Inception      The Dark Knight
2014           2010           2008
★ 8.7          ★ 8.8          ★ 9.0
```

Filters:

```text
Movies
TV Shows

Watched
Unwatched
Watching
Favorites

Genre
Year
Rating
Quality
```

Search متاح على كامل المكتبة.

---

# 🎞️ Movie Page

مثال:

```text
THE DARK KNIGHT

2008 • Crime • Drama • 2h 32m

IMDb: 9.0

Director:
Christopher Nolan

File:
D:\Movies\The Dark Knight (2008)\The Dark Knight (2008).mkv

Quality:
1080p

Size:
12.4 GB
```

Actions:

```text
▶ Play

Mark as Watched
Favorite
Rate
Open Folder
Rename
Move
Open External Page
Remove From Library
```

---

# ▶️ Playback

البرنامج **لن يحتوي على Video Player خاص به في البداية**.

عند الضغط على:

```text
Play
```

يتم تشغيل الملف باستخدام:

- Windows Default Player
    
- VLC
    
- MPC-HC
    
- mpv
    
- أو Player يحدده المستخدم.
    

البرنامج مسؤول عن:

**Organization + Library + Tracking**

وليس Video Decoding.

---

# 🧠 Matching & Confidence System

ممنوع تحريك ملف اعتمادًا على Match غير موثوق.

مثال:

```text
Interstellar.2014.1080p.mkv

Match:
Interstellar (2014)

Confidence:
99%
```

يمكن تنظيمه تلقائيًا.

لكن:

```text
interstellar.final.movie.mkv

Possible Match:

Interstellar (2014)       71%
Interstellar 5555 (2003)  18%
```

يتم إرساله إلى:

## Review Queue

ولا يتم تحريك الملف حتى يراجعه المستخدم.

---

# 🛡️ File Safety

سلامة الملفات جزء أساسي من Architecture.

قبل أي File Operation:

```text
Validate Source
Validate Destination
Check Path
Check Existing File
Check Permissions
Check Duplicate
Create Operation Record
Execute
Verify
```

لا يسمح بعمليات خارج المسارات المسموحة بدون موافقة.

---

# ↩️ Undo System

كل عملية يتم تسجيلها.

مثال:

```text
Moved:

C:\Downloads\interstellar.mkv

→

D:\Movies\Interstellar (2014)\Interstellar (2014).mkv
```

ويكون متاح:

```text
UNDO
```

History يحتفظ بـ:

```text
Original Path
New Path
Original Name
New Name
Operation
Timestamp
Status
```

---

# 👯 Duplicate Detection

قبل إضافة Media جديد يتم التأكد هل الفيلم موجود بالفعل.

مثال:

```text
Possible Duplicate

Existing:
Interstellar
1080p
8.3 GB

New:
Interstellar
2160p
22.7 GB
```

Actions:

```text
Keep Both
Replace Existing
Keep Existing
Ignore
```

يمكن استخدام:

- Metadata ID.
    
- File size.
    
- Filename.
    
- Hash.
    

لتمييز:

**Same Movie**

عن:

**Exact Duplicate File**

---

# ❌ Missing Files

إذا تم حذف أو نقل ملف خارج البرنامج:

```text
Inception

⚠ File Missing

Last Known Location:
D:\Movies\Inception (2010)\Inception.mkv
```

Actions:

```text
Locate File
Relink
Remove From Library
```

لا يتم حذف سجل الفيلم تلقائيًا.

---

# 💾 Storage Dashboard

البرنامج يستطيع تحليل مساحة الـ Media.

مثال:

```text
Movies          438
TV Episodes    1726

Total Size     3.84 TB
```

Breakdown:

```text
Movies         2.1 TB
TV Shows       1.6 TB
Other          140 GB
```

حسب الجودة:

```text
4K             1.4 TB
1080p          2.0 TB
720p           310 GB
Other          130 GB
```

ويمكن عرض أكبر الملفات والأفلام والمسلسلات استهلاكًا للمساحة.

---

# 👁️ Watch Tracking

Local watch tracking بدون Account.

Statuses:

```text
Unwatched
Watching
Watched
```

مع:

```text
Favorite
Watch Later
User Rating
Watch Date
```

مثال:

```text
Interstellar

Watched:
10 Aug 2026

My Rating:
9/10
```

---

# 📚 Collections

Manual Collections:

```text
Favorites
Watch Later
Marvel
Batman
Breaking Bad Universe
```

Automatic Collections ممكن تعتمد على Metadata:

```text
Christopher Nolan

Batman Begins
The Dark Knight
The Dark Knight Rises
Inception
Interstellar
Dunkirk
Tenet
Oppenheimer
```

---

# 🔍 Initial Library Scan

أول تشغيل:

```text
Where are your Movies?

D:\Movies

Where are your TV Shows?

D:\TV Shows

Watch Downloads?

C:\Users\...\Downloads
```

ثم:

```text
Scan Library
```

مثال للنتيجة:

```text
1,842 Files Found

438 Movies
27 TV Shows
1,244 Episodes

31 Files Need Review
12 Possible Duplicates
```

بعدها يتم بناء الـ Library تلقائيًا.

---

# 🗃️ Database

SQLite Local Database.

الجداول الرئيسية المتوقعة:

```text
movies
tv_shows
seasons
episodes
media_files
subtitles
collections
collection_items
watch_history
file_operations
metadata_cache
watched_folders
settings
```

---

# 🏗️ Architecture

```text
                 Desktop UI
                   PySide6
                      │
                      ▼
               Library Service
                      │
       ┌──────────────┼──────────────┐
       │              │              │
       ▼              ▼              ▼
 File Watcher    Media Matcher    Metadata API
  watchdog       Parser/Matcher       │
       │              │              ▼
       ▼              │        Metadata Cache
 File Engine          │
       │              │
       └──────────────┼──────────────┘
                      ▼
                    SQLite
```

Core modules:

```text
core/
    file_engine
    safety
    operations
    watcher

media/
    parser
    matcher
    movies
    tv
    subtitles

metadata/
    providers
    cache

library/
    movies
    shows
    collections
    watch_history

database/
    repositories
    models

ui/
    library
    movie_details
    tv_details
    review_queue
    storage
    settings
```

---

# 🖥️ Technology

الهدف برنامج Windows Desktop عادي.

```text
Python
PySide6 / Qt
SQLite
watchdog
HTTP Client
External Movie Metadata API
```

لا نحتاج في البداية إلى:

```text
Docker
PostgreSQL
Redis
Kafka
Kubernetes
Web Server
```

التطبيق:

```text
DropSort.exe
```

Local-first وبدون Infrastructure إضافية.

---

# 📴 Offline First

بعد جلب Metadata أول مرة، الوظائف التالية تعمل بدون إنترنت:

```text
Browse Library
Search
Play
Move
Rename
Organize
Collections
Watch Tracking
Storage Analysis
File Management
```

Metadata فقط يتم تحديثها عند توفر الإنترنت.

---

# 🚀 Example End-to-End Workflow

يتم تنزيل:

```text
The.Departed.2006.1080p.BluRay.mkv
```

DropSort:

1. يكتشف الملف.
    
2. ينتظر انتهاء الكتابة/التحميل.
    
3. يحلل Filename.
    
4. يكتشف أنه Movie.
    
5. يستخرج `The Departed (2006)`.
    
6. يبحث عن Metadata.
    
7. يحسب Match Confidence.
    
8. يفحص وجود Duplicate.
    
9. ينشئ File Operation Plan.
    
10. ينقل الفيلم إلى:
    

```text
Movies/
└── The Departed (2006)/
    ├── The Departed (2006).mkv
    └── The Departed (2006).ar.srt
```

11. يسجل العملية في History.
    
12. يضيف الفيلم إلى SQLite.
    
13. يخزن Poster والـ Metadata.
    
14. يظهر الفيلم تلقائيًا في المكتبة.
    

النتيجة:

```text
The Departed
2006
★ 8.5

Unwatched

▶ PLAY
```

---

# ⭐ نقطة تميز المشروع

المشروع ليس مجرد File Organizer.

وليس مجرد Movie Library.

الفكرة هي الربط بين:

```text
Physical Files
      ↓
Automatic Organization
      ↓
Media Recognition
      ↓
Metadata
      ↓
Local Library
      ↓
Watch Tracking
```

أي أن **الملفات الحقيقية الموجودة على الهارد هي مصدر المكتبة الأساسي**.

---

# 🛣️ Development Plan

## V1 — Movies MVP

```text
Folder Scan
Movie Detection
Filename Parsing
Metadata Matching
SQLite Library
Poster Grid
Movie Details
Play
Open Folder
Safe Move/Rename
```

## V2 — Automation

```text
Folder Watcher
Automatic Organization
Confidence System
Review Queue
Undo
Operation History
Duplicate Detection
```

## V3 — TV Shows

```text
Show Detection
Season/Episode Parsing
TV Library
Episode Tracking
Missing Episodes
Subtitle Matching
```

## V4 — Library Features

```text
Watched / Unwatched
Ratings
Favorites
Watch Later
Collections
Search
Advanced Filters
```

## V5 — Advanced

```text
Storage Dashboard
Duplicate Analysis
Library Health
Metadata Refresh
Relink Missing Files
Better Matching
Multiple Drives
Backup / Restore Database
```

---

# 🎯 Final Vision

**DropSort Media Library** يصبح مدير مكتبة Media محلي كامل:

> Drop a movie on the computer and forget about it.

البرنامج يتولى:

**Detect → Understand → Match → Organize → Track → Display → Play**

مع الحفاظ على أن كل الملفات والـ Library والـ Watch History تحت تحكم المستخدم محليًا.
