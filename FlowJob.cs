namespace AvalonFlow;

public class FlowJob : IFlowProcess
{
    public string Key { get; set; } = string.Empty;

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    public TimeSpan Duration => EndTime - StartTime;

    public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Func<CancellationToken, Task>? Work { get; set; }
}
