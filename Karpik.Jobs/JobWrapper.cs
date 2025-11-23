namespace Karpik.Jobs;

internal sealed class JobWrapper
{
    public Action Action;
    public Action<int, int> ParallelAction;
    public CancellationTokenSource Cts;
    public bool IsParallel;
    public int StartIndex;
    public int EndIndex;
    public Action OnCompleted;
    public JobCompletion Completion;
}