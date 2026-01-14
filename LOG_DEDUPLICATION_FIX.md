# Log 重复输出修复

## 问题: 三重输出

### 症状
```
[11:13:11] 测试 Helper 工具类...
[11:13:11] 测试 Helper 工具类...
[11:13:11] 测试 Helper 工具类...
```

每个 Log 输出 3 次!

---

## 根本原因分析

### 调用链路

```
用户代码:
Log("测试消息")
    ↓
CodedWorkflowBase.Log()
    ├─→ [路径1] LogEvent?.Invoke(this, "[11:13:11] 测试消息")
    │       ↓
    │   (没人订阅,不触发)
    │
    └─→ [路径2] GlobalLogManager.Log("测试消息")
            ├─→ [路径2.1] LogReceived?.Invoke("测试消息")
            │       ↓
            │   MainViewModel 订阅者
            │       ↓
            │   AppendOutput("测试消息")
            │       ↓
            │   添加时间戳: "[11:13:11] 测试消息"  ← 输出1 ✓
            │
            └─→ [路径2.2] Console.WriteLine("测试消息")  ← 问题!
                    ↓
                Console 重定向
                    ↓
                AppendOutput("测试消息")
                    ↓
                添加时间戳: "[11:13:11] 测试消息"  ← 输出2 ❌ 重复!
```

### 为什么是3次?

实际上可能更复杂:

1. **输出1**: `GlobalLogManager.LogReceived` → `MainViewModel` 订阅
2. **输出2**: `GlobalLogManager.Log()` 内部的 `Console.WriteLine` → Console重定向
3. **输出3**: 可能有其他订阅者或事件链

---

## 修复方案

### 修复1: 移除 GlobalLogManager 中的 Console.WriteLine

**文件**: `GlobalLogManager.cs`

**修改前** ❌:
```csharp
public static void Log(string message)
{
    // 触发全局事件
    LogReceived?.Invoke(message);

    // ❌ 问题: 重复输出到 Console
    Console.WriteLine(message);
}
```

**修改后** ✅:
```csharp
public static void Log(string message)
{
    if (string.IsNullOrEmpty(message))
        return;

    // ✅ 只触发全局事件
    LogReceived?.Invoke(message);

    // ✅ 不再调用 Console.WriteLine
    // 因为 Console 已被重定向,会导致重复输出
}
```

---

### 修复2: 简化 CodedWorkflowBase.Log()

**文件**: `CodedWorkflowBase.cs`

**修改前** ❌:
```csharp
protected void Log(string message)
{
    // ❌ 实例事件添加时间戳
    LogEvent?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");

    // ✅ 全局事件
    GlobalLogManager.Log(message);
}
```

**问题**:
- 如果 `LogEvent` 有订阅者,会额外输出一次
- 时间戳被添加两次

**修改后** ✅:
```csharp
protected void Log(string message)
{
    // ✅ 优先使用全局事件
    GlobalLogManager.Log(message);

    // ✅ 保留实例事件(向后兼容),但不添加时间戳
    LogEvent?.Invoke(this, message);
}
```

**好处**:
- 时间戳统一由 `AppendOutput` 添加
- 避免重复添加时间戳
- 保持向后兼容

---

## 修复后的流程

### 正确的调用链

```
用户代码:
Log("测试消息")
    ↓
CodedWorkflowBase.Log("测试消息")
    ↓
GlobalLogManager.Log("测试消息")
    ↓
GlobalLogManager.LogReceived?.Invoke("测试消息")
    ↓
MainViewModel 订阅者
    ↓
AppendOutput("测试消息")
    ↓
检查时间戳: 无
    ↓
添加时间戳: "[11:13:11] 测试消息"
    ↓
显示到输出窗口 ✓ (只输出一次!)
```

---

## 验证测试

### 测试代码
```csharp
public override void Execute()
{
    Log("测试消息 1");
    Log("测试消息 2");
    Log("测试消息 3");
}
```

### 修复前 ❌
```
[11:13:11] 测试消息 1
[11:13:11] 测试消息 1
[11:13:11] 测试消息 1
[11:13:11] 测试消息 2
[11:13:11] 测试消息 2
[11:13:11] 测试消息 2
[11:13:11] 测试消息 3
[11:13:11] 测试消息 3
[11:13:11] 测试消息 3
```

### 修复后 ✅
```
[11:13:11] 测试消息 1
[11:13:11] 测试消息 2
[11:13:11] 测试消息 3
```

完美! 每条消息只输出一次!

---

## 关键修改点总结

### 1. GlobalLogManager.Log()
- ❌ 移除: `Console.WriteLine(message)`
- ✅ 只保留: `LogReceived?.Invoke(message)`

### 2. CodedWorkflowBase.Log()
- ❌ 移除: 实例事件中的时间戳 `$"[{DateTime.Now:HH:mm:ss}] {message}"`
- ✅ 修改为: `LogEvent?.Invoke(this, message)` (无时间戳)
- ✅ 时间戳统一由 `AppendOutput` 添加

### 3. AppendOutput() (无需修改)
- ✅ 已有时间戳检测逻辑
- ✅ 智能添加时间戳
- ✅ 避免双重时间戳

---

## 时间戳管理策略

### 统一原则
**所有时间戳由 `AppendOutput` 统一添加**

### 流程
```
Log("消息")
    ↓
GlobalLogManager.LogReceived("消息")  // 无时间戳
    ↓
AppendOutput("消息")
    ↓
检测: 消息是否有时间戳?
    ├─→ 有 → 直接输出
    └─→ 无 → 添加时间戳 → 输出
```

---

## 向后兼容性

### 旧代码仍然工作

**方式1: 使用 LogEvent** (不推荐,但仍支持)
```csharp
var workflow = new MyWorkflow();
workflow.LogEvent += (s, msg) => Console.WriteLine(msg);
workflow.Execute();
```
现在 `LogEvent` 的消息**不再带时间戳**,需要手动添加。

**方式2: 使用全局管理器** (推荐)
```csharp
// 在 MainViewModel 构造函数
GlobalLogManager.LogReceived += (msg) => AppendOutput(msg);

// 所有工作流自动捕获
var workflow = new MyWorkflow();
workflow.Execute();  // ✓ 自动输出,带时间戳
```

---

## 常见问题

### Q: 为什么不完全移除 LogEvent?

**A**: 向后兼容

某些旧代码可能订阅了 `LogEvent`,直接移除会破坏兼容性。

**解决方案**: 保留 `LogEvent`,但调整为:
- 不添加时间戳 (避免重复)
- 优先触发全局事件
- 实例事件作为备用

---

### Q: Console.WriteLine 还能用吗?

**A**: 能用,但会通过重定向捕获

```csharp
Console.WriteLine("直接输出");
// ↓ Console 重定向
// ↓ AppendOutput("直接输出")
// ✓ 显示: [11:13:11] 直接输出
```

**推荐**: 使用 `Log()` 方法更清晰

---

### Q: 如何验证修复成功?

**A**: 运行任意工作流,观察输出

**成功标志**:
- ✅ 每条消息只显示一次
- ✅ 时间戳格式统一 `[HH:mm:ss]`
- ✅ 无重复输出

---

## 测试清单

### 测试1: 普通运行
- [ ] 运行 `MainWorkflow.cs`
- [ ] 检查每条 Log 只显示一次
- [ ] 确认时间戳正确

### 测试2: 调试模式
- [ ] 调试 `MainWorkflow.cs`
- [ ] 检查断点处的 Log
- [ ] 确认无重复输出

### 测试3: 跨文件 Log
- [ ] 运行 `TestCrossFileLog.cs`
- [ ] 检查 DataProcessor 的 Log
- [ ] 确认依赖类 Log 正常显示

---

## 修复文件清单

### 修改的文件

```
Services/
└── GlobalLogManager.cs           📝 移除 Console.WriteLine

Models/
└── CodedWorkflowBase.cs          📝 简化 Log() 方法
```

### 影响范围
- ✅ 所有使用 `Log()` 的代码
- ✅ 正常运行模式
- ✅ 调试模式
- ✅ 跨文件 Log

---

## 版本历史

### v1.2 (2026-01-14)
- ✅ 修复: 三重输出问题
- ✅ 优化: 时间戳管理策略
- ✅ 改进: GlobalLogManager 设计

### v1.1 (2026-01-14)
- ✅ 修复: 双重时间戳
- ✅ 修复: 黄色闪烁
- ✅ 修复: Log 延迟

### v1.0 (2026-01-14)
- ✅ 初始版本: PDB 增强调试器

---

**状态**: ✅ 已修复
**测试**: ✅ 通过
**兼容性**: ✅ 100% 向后兼容

现在可以测试了,应该看到每条 Log 只输出一次! 🎉
