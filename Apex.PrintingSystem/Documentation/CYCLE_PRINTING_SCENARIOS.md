# Cycle-Based Printing Scenarios

هذا المستند يوثق السيناريوهات المختلفة لنظام Cycle-Based Printing وكيفية اختبارها.

## نظرة عامة

نظام Cycle-Based Printing يقسم مهمة الطباعة إلى دورات (Cycles) منفصلة، حيث كل دورة تمثل رقم واحد. هذا يسمح بـ:
- معالجة الأخطاء على مستوى الدورة الواحدة
- إعادة محاولة الدورات الفاشلة
- تخطي الدورات الفاشلة
- إيقاف واستئناف الطباعة
- حفظ الحالة والاستئناف من ملف محفوظ

## السيناريوهات

### 1. طباعة عادية (Normal Printing)

**الوصف**: طباعة 1000 رقم بدون أخطاء

**الخطوات**:
1. افتح NumberingWizard
2. اختر طابعة
3. حدد TemplatePath وSlots
4. اضبط StartNumber = 1, TotalNumbers = 1000
5. فعّل `UseCycleBasedPrinting`
6. اضغط StartPrint

**النتيجة المتوقعة**:
- يتم طباعة جميع الدورات بنجاح
- `CompletedCycles = 1000`
- `FailedCycles = 0`
- رسالة نجاح تظهر

**التحقق**:
- تحقق من أن جميع الأرقام تم طباعتها
- تحقق من أن Progress وصل إلى 100%
- تحقق من أن Status = "اكتملت الطباعة بنجاح"

---

### 2. خطأ في Cycle (Error in Cycle)

**الوصف**: محاكاة خطأ Double Feed في Cycle #50

**الخطوات**:
1. ابدأ طباعة 100 رقم
2. عند Cycle #50، قم بمحاكاة خطأ (مثلاً: إزالة الورق من الطابعة)
3. راقب النظام

**النتيجة المتوقعة**:
- النظام يتوقف عند Cycle #50
- `FailedCycles = 1`
- `FailedCyclesList` يحتوي على Cycle #50
- Status = "اكتملت مع أخطاء"
- يتم حفظ الحالة تلقائياً

**التحقق**:
- تحقق من أن الحالة تم حفظها في `%LocalAppData%\ApexPrintingSystem\CycleStates\`
- تحقق من أن Cycle #50 في قائمة الفشل
- تحقق من أن ErrorMessage يحتوي على تفاصيل الخطأ

---

### 3. Skip Cycle (تخطي Cycle فاشل)

**الوصف**: تخطي Cycle فاشل والمتابعة

**الخطوات**:
1. بعد حدوث خطأ في Cycle (مثل السيناريو 2)
2. اختر Cycle فاشل من `FailedCyclesList`
3. اضغط `SkipCycleCommand`

**النتيجة المتوقعة**:
- Cycle الفاشل يتم تخطيه
- Status = `Skipped`
- النظام يستمر في الدورات التالية
- `FailedCycles` يبقى كما هو (لأن Cycle تم تخطيه وليس إصلاحه)

**التحقق**:
- تحقق من أن Cycle تم تخطيه في القائمة
- تحقق من أن الدورات التالية تستمر
- تحقق من أن Dependency chain لا ينكسر

---

### 4. Retry Cycle (إعادة محاولة Cycle فاشل)

**الوصف**: إعادة محاولة Cycle فاشل

**الخطوات**:
1. بعد حدوث خطأ في Cycle
2. اختر Cycle فاشل من `FailedCyclesList`
3. اضغط `RetryCycleCommand`

**النتيجة المتوقعة**:
- Cycle يتم إعادة محاولته
- Status = `Retrying` ثم `Printing`
- إذا نجحت، Status = `CompletedPhysical`
- `FailedCycles` ينقص

**التحقق**:
- تحقق من أن Cycle تم طباعته بنجاح
- تحقق من أن `FailedCycles` انخفض
- تحقق من أن النظام يستمر في الدورات التالية

---

### 5. Pause/Resume (إيقاف واستئناف)

**الوصف**: إيقاف الطباعة مؤقتاً ثم استئنافها

**الخطوات**:
1. ابدأ طباعة 100 رقم
2. أثناء الطباعة، اضغط `PauseCommand`
3. انتظر قليلاً
4. اضغط `ResumeCommand`

**النتيجة المتوقعة**:
- عند Pause: Status = "متوقف مؤقتاً"
- يتم حفظ الحالة تلقائياً
- عند Resume: Status = "جاري الاستئناف..."
- النظام يستمر من حيث توقف

**التحقق**:
- تحقق من أن الحالة تم حفظها
- تحقق من أن النظام يستمر من الدورة الصحيحة
- تحقق من أن لا توجد دورات مكررة

---

### 6. Resume from State (الاستئناف من ملف State محفوظ)

**الوصف**: استئناف طباعة من حالة محفوظة بعد إعادة تشغيل التطبيق

**الخطوات**:
1. ابدأ طباعة 100 رقم
2. أوقف الطباعة (Pause أو Error)
3. أغلق التطبيق
4. أعد فتح التطبيق
5. اضغط `LoadPendingStatesCommand`
6. اختر حالة من `PendingStates`
7. اضغط `ResumeFromStateCommand`

**النتيجة المتوقعة**:
- يتم تحميل الحالات المحفوظة
- عند اختيار حالة والاستئناف:
  - TemplatePath وSlots يتم استعادتها
  - جميع Cycles يتم استعادتها مع حالاتها
  - النظام يستمر من حيث توقف

**التحقق**:
- تحقق من أن الحالات المحفوظة تظهر في القائمة
- تحقق من أن TemplatePath وSlots تم استعادتها
- تحقق من أن Cycles تم استعادتها مع حالاتها الصحيحة
- تحقق من أن Dependency Graph تم إعادة بنائه بشكل صحيح

---

## الاختبارات الوحدة (Unit Tests)

### CycleOrchestrator Tests

```csharp
[Test]
public void CreateAndEnqueueCycles_CreatesCorrectNumberOfCycles()
{
    // Arrange
    var orchestrator = new CycleOrchestrator(...);
    
    // Act
    var cycles = orchestrator.CreateAndEnqueueCycles(...);
    
    // Assert
    Assert.AreEqual(100, cycles.Count);
    Assert.AreEqual(1, cycles[0].CycleNumber);
    Assert.AreEqual(100, cycles[99].CycleNumber);
}

[Test]
public void CreateAndEnqueueCycles_SetsDependenciesCorrectly()
{
    // Arrange
    var orchestrator = new CycleOrchestrator(...);
    
    // Act
    var cycles = orchestrator.CreateAndEnqueueCycles(...);
    
    // Assert
    Assert.IsNull(cycles[0].DependsOnJobId);
    Assert.AreEqual(cycles[0].JobId, cycles[1].DependsOnJobId);
    Assert.AreEqual(cycles[1].JobId, cycles[2].DependsOnJobId);
}
```

### JobDependencyManager Tests

```csharp
[Test]
public void AreDependenciesSatisfied_ReturnsTrue_WhenNoDependencies()
{
    // Arrange
    var manager = new JobDependencyManager();
    var job = new CycleJob { DependsOnJobId = null };
    
    // Act
    var result = manager.AreDependenciesSatisfied(job);
    
    // Assert
    Assert.IsTrue(result);
}

[Test]
public void AreDependenciesSatisfied_ReturnsTrue_WhenParentCompleted()
{
    // Arrange
    var manager = new JobDependencyManager();
    var parent = new CycleJob { JobId = "parent", Status = CycleStatus.CompletedPhysical };
    var child = new CycleJob { DependsOnJobId = "parent" };
    manager.AddJob(parent);
    manager.AddJob(child);
    
    // Act
    var result = manager.AreDependenciesSatisfied(child);
    
    // Assert
    Assert.IsTrue(result);
}

[Test]
public void RetryCycle_RequeuesJob_WhenDependenciesSatisfied()
{
    // Arrange
    var queue = new SequencedJobQueue(...);
    var job = new CycleJob { Status = CycleStatus.Failed };
    queue.Enqueue(job);
    
    // Act
    var result = queue.RetryJob(job.JobId);
    
    // Assert
    Assert.IsTrue(result);
    Assert.AreEqual(CycleStatus.Retrying, job.Status);
}

[Test]
public void SkipCycle_MarksAsSkipped_AndEnqueuesDependents()
{
    // Arrange
    var queue = new SequencedJobQueue(...);
    var parent = new CycleJob { JobId = "parent", Status = CycleStatus.Failed };
    var child = new CycleJob { DependsOnJobId = "parent", Status = CycleStatus.Pending };
    queue.Enqueue(parent);
    queue.Enqueue(child);
    
    // Act
    var result = queue.SkipJob("parent", "User skipped");
    
    // Assert
    Assert.IsTrue(result);
    Assert.AreEqual(CycleStatus.Skipped, parent.Status);
    // Child should be ready to execute
}
```

### CycleStatePersistenceManager Tests

```csharp
[Test]
public async Task SaveStateAsync_SavesStateToFile()
{
    // Arrange
    var manager = new CycleStatePersistenceManager();
    var jobId = Guid.NewGuid().ToString();
    var cycles = new List<CycleJob> { ... };
    
    // Act
    await manager.SaveStateAsync(jobId, ...);
    
    // Assert
    var state = await manager.LoadStateAsync(jobId);
    Assert.IsNotNull(state);
    Assert.AreEqual(jobId, state.JobId);
}

[Test]
public async Task LoadStateAsync_ReturnsNull_WhenFileNotExists()
{
    // Arrange
    var manager = new CycleStatePersistenceManager();
    var jobId = Guid.NewGuid().ToString();
    
    // Act
    var state = await manager.LoadStateAsync(jobId);
    
    // Assert
    Assert.IsNull(state);
}

[Test]
public async Task GetPendingStatesAsync_ReturnsAllPendingStates()
{
    // Arrange
    var manager = new CycleStatePersistenceManager();
    await manager.SaveStateAsync("job1", ...);
    await manager.SaveStateAsync("job2", ...);
    
    // Act
    var states = await manager.GetPendingStatesAsync();
    
    // Assert
    Assert.GreaterOrEqual(states.Count, 2);
}
```

---

## ملاحظات مهمة

1. **Dependency Chain**: كل Cycle يعتمد على السابق. عند Skip Cycle، يجب أن يكون Skipped يعتبر CompletedLogical حتى لا ينكسر السلسلة.

2. **State Persistence**: الحالة تُحفظ تلقائياً عند:
   - Pause
   - Error
   - Cancellation

3. **Atomic Writes**: حفظ الحالة يستخدم atomic writes (كتابة إلى ملف مؤقت ثم rename) لضمان عدم فقدان البيانات.

4. **Resume Safety**: عند الاستئناف، يتم إعادة بناء Dependency Graph وQueue بشكل آمن.

5. **Error Handling**: جميع الأخطاء يتم التقاطها وحفظها في State للاستئناف لاحقاً.

---

## استكشاف الأخطاء

### المشكلة: Cycles لا تستمر بعد Retry
**الحل**: تحقق من أن Dependency Manager يعتبر Retrying كحالة صالحة للتنفيذ.

### المشكلة: State لا يتم حفظه
**الحل**: تحقق من أن `CycleStatePersistenceManager` يتم تمريره إلى `CyclePrintRunner`.

### المشكلة: Resume لا يعمل
**الحل**: تحقق من أن TemplatePath وSlots موجودة في State المحفوظ، وأن Dependency Graph يتم إعادة بنائه بشكل صحيح.
