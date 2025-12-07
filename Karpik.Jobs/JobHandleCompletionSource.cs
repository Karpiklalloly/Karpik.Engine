namespace Karpik.Jobs;

public class JobHandleCompletionSource<T>
{
    public JobHandle<T> JobHandle { get; }

    private readonly JobCompletion<T> _jobCompletion;
    
    public JobHandleCompletionSource()
    {
        _jobCompletion = new JobCompletion<T>(1);
        JobHandle = new JobHandle<T>(_jobCompletion, null);
    }

    public void SetResult(T result)
    {
        _jobCompletion.SetResult(result);
        _jobCompletion.Signal();
    }
    
    public void SetException(Exception exception)
    {
        _jobCompletion.SetException(exception);
        _jobCompletion.Signal();
    }
}