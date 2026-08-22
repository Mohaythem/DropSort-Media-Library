# 🧩 Codex Skills Plan

المشروع لن يستخدم كل Skills الموجودة في `claude-skills`. سنستخدم فقط ما يخدم **DropSort Media Library** بشكل مباشر، لتجنب تشويش الـ context والـ overengineering.

Source Repository:

`https://github.com/alirezarezvani/claude-skills`

---

# ✅ Skills هنستخدمها

## 1. `senior-architect`

**Path:**

```text
engineering-team/skills/senior-architect/SKILL.md
```

**هنستخدمها في:**

- تصميم Architecture المشروع.
    
- تحديد حدود الـ modules.
    
- منع coupling غير الضروري.
    
- مراجعة dependencies بين:
    
    - File Engine
        
    - Media Matcher
        
    - Metadata
        
    - Library
        
    - Database
        
    - UI
        
- تسجيل Architecture Decisions.
    

مهمة خصوصًا في بداية المشروع وأي refactor كبير.

---

## 2. `database-designer`

**Path:**

```text
engineering/skills/database-designer/SKILL.md
```

**هنستخدمها في:**

- تصميم SQLite schema.
    
- العلاقات بين:
    
    - movies
        
    - tv_shows
        
    - seasons
        
    - episodes
        
    - media_files
        
    - subtitles
        
    - collections
        
    - watch_history
        
    - file_operations
        
    - metadata_cache
        
- Indexes.
    
- Migrations.
    
- Database integrity.
    

---

## 3. `tdd-guide`

**Path:**

```text
engineering-team/skills/tdd-guide/SKILL.md
```

**هنستخدمها في:**

اختبار الأجزاء الحساسة قبل وبعد تنفيذها، خصوصًا:

```text
Move
Rename
Undo
File Recovery
Duplicate Detection
Media Parsing
Media Matching
Database Operations
```

الـ testing الأساسي للمشروع سيكون Python / pytest وليس React testing.

---

## 4. `code-reviewer`

**Path:**

```text
engineering-team/skills/code-reviewer/SKILL.md
```

**هنستخدمها في:**

- مراجعة تغييرات كل Worker.
    
- Python code review.
    
- اكتشاف code smells.
    
- Complexity.
    
- SOLID violations.
    
- Security mistakes.
    
- Large files/functions.
    
- تقييم مخاطر الـ PR قبل الدمج.
    

لا يتم دمج كود Worker تلقائيًا لمجرد أنه انتهى.

---

## 5. `named-persona-adversarial-review`

**Path:**

```text
engineering-team/skills/named-persona-adversarial-review/SKILL.md
```

**هنستخدمها في:**

مراجعة عدائية بعد الـ normal code review.

الهدف هو محاولة إيجاد:

```text
Data Loss
Broken Undo
Unsafe File Operations
Architecture Problems
Unexpected Edge Cases
Bad UX Decisions
Hidden Failure Modes
```

أي مشكلة يمكن أن تسبب فقدان ملفات تعتبر Critical أو Blocker.

---

## 6. `dependency-auditor`

**Path:**

```text
engineering/skills/dependency-auditor/
```

**هنستخدمها في:**

- مراجعة Python dependencies.
    
- اكتشاف dependencies قديمة أو غير ضرورية.
    
- مراجعة supply-chain risks.
    
- تقليل حجم البرنامج.
    
- مراجعة dependencies قبل releases.
    

ليست Skill تعمل طوال الوقت، بل تستخدم عند إضافة dependency جديدة أو قبل Release.

---

# 🛡️ Skills لإدارة الـ Skills نفسها

دول مش لبناء Features التطبيق مباشرة.

## 7. `skill-security-auditor`

**Path:**

```text
engineering/skills/skill-security-auditor/SKILL.md
```

**وظيفتها:**

فحص أي Skill خارجية قبل تثبيتها.

تفحص:

```text
Dangerous Commands
Prompt Injection
Unexpected Network Calls
Filesystem Abuse
Suspicious Dependencies
Hidden Executables
Credential Access
```

تستخدم كـ security gate قبل إضافة Skills جديدة للمشروع.

---

## 8. `skill-tester`

**Path:**

```text
engineering/skills/skill-tester/SKILL.md
```

**وظيفتها:**

اختبار الـ Skills نفسها، خصوصًا الـ custom DropSort Skills التي سنكتبها.

تفحص:

```text
SKILL.md Structure
Scripts
Syntax
Runtime
Documentation
Quality
Security Score
```

---

# 🧠 Custom DropSort Skills

هنكتب Skills خاصة بالمشروع لأن الـ generic Skills لا تعرف قواعد DropSort نفسها.

المكان المقترح:

```text
.codex/
└── skills/
```

---

## `dropsort-project-rules`

```text
.codex/skills/dropsort-project-rules/SKILL.md
```

يحتوي القواعد العامة للمشروع:

- Windows Desktop application.
    
- Local-first.
    
- No Docker.
    
- SQLite.
    
- PySide6.
    
- Modular architecture.
    
- UI لا تنفذ filesystem operations مباشرة.
    
- File Engine منفصل عن Media Library.
    
- لا نضيف infrastructure بدون سبب واضح.
    

---

## `dropsort-file-safety`

```text
.codex/skills/dropsort-file-safety/SKILL.md
```

أهم Skill خاصة بالمشروع.

قواعد مثل:

```text
Never automatically delete original media.

Never overwrite an existing destination.

Every move or rename must be reversible.

Never operate outside approved roots.

Validate paths before any filesystem operation.

Create an operation record before execution.

Verify the result after execution.

Database updates happen only after filesystem verification.

Interrupted operations must be recoverable.
```

---

## `dropsort-media-matching`

```text
.codex/skills/dropsort-media-matching/SKILL.md
```

قواعد فهم الأفلام والمسلسلات:

```text
Filename Parsing
Movie Detection
TV Detection
Season/Episode Detection
Metadata Matching
Confidence Scoring
Ambiguous Matches
Review Queue
Subtitle Matching
Duplicate Matching
```

قاعدة أساسية:

```text
Low-confidence matches must never trigger automatic file movement.
```

---

## `dropsort-testing`

```text
.codex/skills/dropsort-testing/SKILL.md
```

يحدد حالات الاختبار الخاصة بالمشروع.

أمثلة:

```text
Destination already exists
Source disappears during move
Disk becomes unavailable
Permission denied
Interrupted operation
Duplicate file
Duplicate movie with different quality
Wrong metadata match
Malformed filename
Subtitle without media
Database write failure
Undo after application restart
Moved file outside application
Missing external drive
```

---

## `dropsort-review`

```text
.codex/skills/dropsort-review/SKILL.md
```

Checklist إلزامية قبل دمج أي Feature.

يسأل مثلًا:

```text
Can this lose a user file?

Can this overwrite something?

Can this escape approved folders?

Is the operation reversible?

What happens if the app crashes halfway?

Does SQLite remain consistent?

Does this introduce unnecessary coupling?

Are there tests for failure paths?

Does low-confidence matching require review?

Does the UI expose a dangerous operation too easily?
```

---

# ❌ Skills مش هنستخدمها حاليًا

## `senior-qa`

مش مناسب للمشروع الحالي لأن الجزء الأساسي منه موجه إلى:

```text
React
Next.js
Jest
React Testing Library
Playwright
```

إحنا Desktop Python/PySide6، لذلك `tdd-guide` أنسب.

---

## DevOps Skills

مش محتاجين حاليًا:

```text
senior-devops
Docker
Kubernetes
Terraform
Helm
Cloud Architecture
```

لأن المشروع:

```text
Windows Desktop
Local-first
Single application
```

---

## Backend / API Infrastructure Skills

مش محتاجين حاليًا:

```text
senior-backend
microservices
API gateway
distributed systems
Redis
Kafka
```

التطبيق لا يحتاج Backend Server.

External Movie API مجرد provider داخل البرنامج.

---

## Cloud Skills

مش محتاجين:

```text
AWS
Azure
GCP
Cloud Databases
Cloud Storage
```

الهدف أن تكون المكتبة والملفات والـ tracking محلية.

---

## ML / AI Skills

مش محتاجين ML حاليًا.

Media matching يبدأ بقواعد deterministic + metadata APIs + confidence scoring.

لا نضيف AI/ML إلا لو ظهر لاحقًا use-case واضح يبرر التعقيد.

---

## Web Frontend Skills

مش محتاجين:

```text
React
Next.js
Vue
Angular
Web UI
```

الواجهة ستكون PySide6 / Qt.

---

# 📌 Final Skill Set

## Core Development

```text
senior-architect
database-designer
tdd-guide
code-reviewer
named-persona-adversarial-review
dependency-auditor
```

## Skill Management

```text
skill-security-auditor
skill-tester
```

## DropSort Custom Skills

```text
dropsort-project-rules
dropsort-file-safety
dropsort-media-matching
dropsort-testing
dropsort-review
```

---

# 🎯 Rule

لا نضيف Skill جديدة إلا إذا كان هناك **احتياج حقيقي في المشروع** لا تغطيه الـ Skills الحالية.

الهدف ليس امتلاك أكبر عدد من Skills.

الهدف هو أن يكون لكل Skill:

```text
Clear Responsibility
Clear Trigger
Clear Benefit
```

وأي Skill لا تحقق ذلك لا تدخل المشروع.