namespace AvalonFlow;

public interface IFlowProcess
{
    string Key { get; set; }
    DateTime StartTime { get; set; }
    DateTime EndTime { get; set; }
    TimeSpan Duration => EndTime - StartTime;
    TaskCompletionSource<bool> Completion { get; }
    Func<CancellationToken, Task>? Work { get; }
}
