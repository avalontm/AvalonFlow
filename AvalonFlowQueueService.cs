using System.Collections.Concurrent;

namespace AvalonFlow
{
    public class AvalonFlowQueueService<T> where T : IFlowProcess
    {
        private readonly ConcurrentDictionary<string, ConcurrentQueue<T>> _queues = new();
        private readonly ConcurrentDictionary<string, bool> _processingStatus = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _currentCompletions = new();
        private readonly ConcurrentDictionary<string, Task> _lastRunningTask = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();
        private readonly int _maxSeconds;
        private readonly Action<string>? _log;
        private readonly bool _autoStart;

        /// <summary>
        /// Initializes a new instance of the AvalonFlowQueueService.
        /// </summary>
        /// <param name="maxSeconds">Maximum time in seconds a process can run before timing out. Default is 10 seconds.</param>
        /// <param name="autoStart">Indicates whether queue processing should start automatically when a new process is enqueued. Default is true.</param>
        /// <param name="onLog">Optional action to receive log messages.</param>

        public AvalonFlowQueueService(int maxSeconds = 10, bool autoStart = true, Action<string>? onLog = null)
        {
            _maxSeconds = maxSeconds;
            _log = onLog;
            _autoStart = autoStart;
        }

        /// <summary>
        /// Enqueues a new process into the queue associated with the specified key.
        /// If autoStart is enabled and no process is currently running for that key,
        /// it will immediately start processing the next item in the queue.
        /// </summary>
        /// <param name="key">The unique identifier for the queue.</param>
        /// <param name="process">The process to enqueue and potentially execute.</param>
        /// <returns>A Task representing the processing of the queue (if autoStart is enabled), or a completed Task.</returns>
        public Task Enqueue(string key, T process)
        {
            var queue = _queues.GetOrAdd(key, _ => new ConcurrentQueue<T>());
            queue.Enqueue(process);
            _log?.Invoke($"[{key}] Enqueued process.");

            return _autoStart ? ProcessQueue(key) : Task.CompletedTask;
        }

        /// <summary>
        /// Gets the task representing the last running process for the given key.
        /// </summary>
        /// <param name="key">The key of the queue whose last process task is requested.</param>
        /// <returns>The task of the last running process if it exists; otherwise, null.</returns>
        public Task? GetLastProcessTask(string key)
        {
            return _lastRunningTask.TryGetValue(key, out var task) ? task : null;
        }

        private Task ProcessQueue(string key)
        {
            // Si ya se está procesando, no hacemos nada
            if (!_processingStatus.TryAdd(key, true))
            {
                return _lastRunningTask.GetValueOrDefault(key) ?? Task.CompletedTask;
            }

            var task = Task.Run(async () =>
            {
                try
                {
                    if (!_queues.TryGetValue(key, out var queue))
                    {
                        _log?.Invoke($"[{key}] Queue not found.");
                        return;
                    }

                    if (!queue.TryDequeue(out var process))
                    {
                        _log?.Invoke($"[{key}] Queue was empty.");
                        return;
                    }

                    _log?.Invoke($"[{key}] Starting single process...");

                    process.StartTime = DateTime.Now;

                    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _currentCompletions[key] = completion;

                    var workTask = process.Work != null ? process.Work(CancellationToken.None) : Task.CompletedTask;
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(_maxSeconds));

                    var finished = await Task.WhenAny(workTask, completion.Task, timeoutTask);

                    if (finished == timeoutTask)
                    {
                        _log?.Invoke($"[{key}] Process timeout.");
                    }
                    else if (finished == completion.Task)
                    {
                        _log?.Invoke($"[{key}] Process finished via FinishProcess.");
                    }
                    else
                    {
                        _log?.Invoke($"[{key}] Work task completed.");
                        completion.TrySetResult(true);
                    }

                    process.EndTime = DateTime.Now;
                    process.Completion.TrySetResult(true);
                    _currentCompletions.TryRemove(key, out _);
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[{key}] Error: {ex.Message}");
                }
                finally
                {
                    _log?.Invoke($"[{key}] Process complete.");
                    _processingStatus.TryRemove(key, out _);
                }
            });

            _lastRunningTask[key] = task;
            return task;
        }

        /// <summary>
        /// Starts processing the queue associated with the specified key.
        /// </summary>
        /// <param name="key">The key of the queue to start processing.</param>
        /// <returns>A task that represents the queue processing operation.</returns>

        public Task StartProcessing(string key)
        {
            return ProcessQueue(key);
        }

        /// <summary>
        /// Marks the current process associated with the specified key as finished, completing the corresponding task.
        /// </summary>
        /// <param name="key">The key of the process to finish.</param>

        public void FinishProcess(string key)
        {
            if (_currentCompletions.TryGetValue(key, out var completion))
            {
                _log?.Invoke($"[{key}] FinishProcess called.");
                completion.TrySetResult(true);
            }
            else
            {
                _log?.Invoke($"[{key}] FinishProcess called but no active process found.");
            }
        }

        /// <summary>
        /// Cancels the ongoing processing for the specified key by requesting cancellation of the token.
        /// </summary>
        /// <param name="key">The key whose processing should be cancelled.</param>

        public void CancelProcessing(string key)
        {
            if (_cancellationTokens.TryRemove(key, out var cts))
            {
                _log?.Invoke($"[{key}] CancelProcessing called.");
                cts.Cancel();
            }
        }
    }
}
