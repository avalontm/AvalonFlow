# AvalonFlow

**AvalonFlow** is a lightweight, thread-safe, asynchronous queue manager designed to process tasks sequentially by key. It’s ideal for managing processes that must run one at a time per resource (e.g., per user, device, or session) while still allowing parallelism across different keys.

---

## ✨ Features

- 🔄 Per-key sequential task execution
- ⚙️ Configurable timeout per process
- ✅ Manual or automatic queue processing
- 📌 Finish or cancel tasks programmatically
- 🧵 Thread-safe with `ConcurrentDictionary` and `ConcurrentQueue`
- 📦 .NET Standard 2.0+ and .NET 6/7/8 compatible

---

## 📦 Installation

You can install AvalonFlow via [NuGet](https://www.nuget.org/packages/AvalonFlow):

```bash
dotnet add package AvalonFlow
using AvalonFlow;

// Create the queue service with optional timeout and logging
var flowQueue = new AvalonFlowQueueService<MyProcess>(maxSeconds: 10, autoStart: true, onLog: Console.WriteLine);

// Enqueue a task
await flowQueue.Enqueue("client-123", new MyProcess
{
    Work = async token =>
    {
        // Simulate work
        await Task.Delay(3000, token);
    }
});

// Optionally call FinishProcess("client-123") to complete early

public class MyProcess : IFlowProcess
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public Func<CancellationToken, Task>? Work { get; set; }
    public TaskCompletionSource<bool> Completion { get; } = new();
}
```

🛠️ API Highlights
```
Enqueue(string key, T process)              // Adds a process to a key-based queue
StartProcessing(string key)                // Starts processing manually if autoStart is false
FinishProcess(string key)                  // Completes current task early
CancelProcessing(string key)              // Cancels the current process
GetLastProcessTask(string key)            // Gets the currently running task for that key
```

🧱 Supported Platforms
✅ .NET Standard 2.0+

✅ .NET Core 3.1

✅ .NET 5/6/7/8

✅ Windows, Linux, macOS
