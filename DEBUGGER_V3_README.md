# DebuggerServiceV3 - 改进的实时调试器

## 核心改进

### 1. 使用 TaskCompletionSource 替代 AutoResetEvent
**问题**: V2 使用 `AutoResetEvent.WaitOne()` 阻塞线程,容易导致死锁
**解决**: V3 使用 `TaskCompletionSource<bool>.Task.Wait()` 更现代的异步等待机制

```csharp
// V2 (旧)
private AutoResetEvent _stepEvent = new AutoResetEvent(false);
_stepEvent.WaitOne(); // 阻塞

// V3 (新)
private TaskCompletionSource<bool> _pauseSignal;
_pauseSignal = new TaskCompletionSource<bool>();
_pauseSignal.Task.Wait(); // 现代异步等待
```

### 2. 行号映射修复
**问题**: V2 插桩后行号与原始代码不匹配,断点打在错误位置
**解决**: V3 记录 `插桩后行号 -> 原始行号` 的映射关系

```csharp
private Dictionary<int, int> _lineMapping = new Dictionary<int, int>();

// 示例:
// 原始代码:
// 10: int x = 1;
// 11: Console.WriteLine(x);

// 插桩后:
// 10: __debugCallback?.Invoke(10);
// 11: int x = 1;
// 12: __debugCallback?.Invoke(11);
// 13: Console.WriteLine(x);

// 映射关系:
// _lineMapping[10] = 10  (回调指向原始第10行)
// _lineMapping[12] = 11  (回调指向原始第11行)
```

### 3. 更清晰的线程模型
- **执行线程**: 后台线程运行工作流代码
- **暂停机制**: 使用 `TaskCompletionSource` 在后台线程中等待
- **UI更新**: 通过 `SynchronizationContext.Post` 调度到UI线程

## 使用方法

### 在 MainViewModel 中集成

```csharp
// 1. 初始化
private readonly DebuggerServiceV3 _debugger;

public MainViewModel()
{
    _debugger = new DebuggerServiceV3();
    _debugger.CurrentLineChanged += OnDebuggerCurrentLineChanged;
    _debugger.BreakpointHit += OnDebuggerBreakpointHit;
    _debugger.DebugSessionEnded += OnDebugSessionEnded;
    _debugger.VariablesUpdated += OnVariablesUpdated;
}

// 2. 开始调试
private async Task ExecuteStartDebugAsync()
{
    var breakpoints = GetBreakpointsFromUI?.Invoke() ?? new List<int>();
    _debugger.SetBreakpoints(breakpoints);

    IsDebugging = true; // 先设置状态,避免按钮不可点击

    bool success = await _debugger.StartDebuggingAsync(Code, _compiler);
    if (!success)
    {
        IsDebugging = false;
    }
}

// 3. 单步执行
private async Task ExecuteStepOverAsync()
{
    await _debugger.StepOverAsync();
}

// 4. 继续执行
private async Task ExecuteContinueAsync()
{
    await _debugger.ContinueAsync();
}

// 5. 停止调试
private void ExecuteStopDebug()
{
    _debugger.StopDebugging();
    IsDebugging = false;
}
```

## 关键特性

### ✅ 实时暂停
代码执行到断点时会**立即暂停**,不是事后回放

### ✅ 准确行号
显示和暂停在正确的原始代码行号

### ✅ 变量查看
暂停时可以查看当前所有变量的值

### ✅ 无死锁
使用 `TaskCompletionSource` 避免了 `AutoResetEvent` 的死锁问题

## 测试步骤

1. 在 MainViewModel 中将 `DebuggerServiceV2` 替换为 `DebuggerServiceV3`
2. 在代码编辑器中设置断点(点击行号左侧)
3. 按 F9 开始调试
4. 代码会执行并在第一行暂停
5. 按 F10 单步执行,或 F5 继续到下一个断点
6. 观察状态栏和变量窗口的更新

## 已知限制

1. **仍需后台线程**: 工作流代码在后台线程执行,使用 `.Wait()` 暂停
2. **可能的UI延迟**: 短暂的 50ms 延迟确保UI有时间更新
3. **仅支持 Execute 方法**: 只插桩 `Execute` 方法内的代码

## 对比 V2

| 特性 | V2 | V3 |
|------|----|----|
| 暂停机制 | AutoResetEvent | TaskCompletionSource ✅ |
| 行号映射 | 无(错位) | 有(准确) ✅ |
| 死锁风险 | 中等 | 低 ✅ |
| 代码复杂度 | 中 | 中 |

## 下一步优化方向

如果 V3 仍有问题,可以考虑:
1. 使用 `SemaphoreSlim` 替代 `TaskCompletionSource`
2. 将执行移到UI线程,使用 `Dispatcher.Yield()` 让出控制权
3. 集成 Roslyn Scripting API 做语句级别的执行控制
