using System.Runtime.CompilerServices;

namespace Karpik.Jobs;

public struct JobHandleMethodBuilder
{
    private JobCompletion _completion;
    private CancellationTokenSource _cts;
    private JobHandle _jobHandle;
    
    public static JobHandleMethodBuilder Create()
    {
        return new JobHandleMethodBuilder();
    }

    public JobHandle Task
    {
        get
        {
            if (_completion == null)
            {
                _completion = new JobCompletion(1);
                _cts = new CancellationTokenSource();
                _jobHandle = new JobHandle(_completion, _cts);
            }
            return _jobHandle;
        }
    }

    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        stateMachine.MoveNext();
    }

    public void SetResult()
    {
        if (_completion is null)
        {
            _completion = new JobCompletion(1);
            _cts = new CancellationTokenSource();
            _jobHandle = new JobHandle(_completion, _cts);
        }

        _completion.Signal();
    }

    public void SetException(Exception exception)
    {
        if (_completion == null)
        {
            _completion = new JobCompletion(1);
            _cts = new CancellationTokenSource();
            _jobHandle = new JobHandle(_completion, _cts);
        }

        _completion.SetException(exception); 
        _completion.Signal();
    }

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        awaiter.OnCompleted(stateMachine.MoveNext);
    }

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        awaiter.OnCompleted(stateMachine.MoveNext);
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine)
    {
        
    }
}

public struct JobHandleMethodBuilder<T>
{
    private JobCompletion<T> _completion;
    private CancellationTokenSource _cts;
    private JobHandle<T> _jobHandle;

    public static JobHandleMethodBuilder<T> Create() => new();

    public JobHandle<T> Task
    {
        get
        {
            if (_completion == null)
            {
                _completion = new JobCompletion<T>(1);
                _cts = new CancellationTokenSource();
                _jobHandle = new JobHandle<T>(_completion, _cts);
            }
            return _jobHandle;
        }
    }

    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        stateMachine.MoveNext();
    }

    public void SetResult(T result)
    {
        if (_completion == null)
        {
            _completion = new JobCompletion<T>(1);
            _cts = new CancellationTokenSource();
            _jobHandle = new JobHandle<T>(_completion, _cts);
        }

        _completion.SetResult(result);
        _completion.Signal();
    }

    public void SetException(Exception exception)
    {
        if (_completion == null)
        {
            _completion = new JobCompletion<T>(1);
            _cts = new CancellationTokenSource();
            _jobHandle = new JobHandle<T>(_completion, _cts);
        }

        _completion.SetException(exception);
        _completion.Signal();
    }

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        awaiter.OnCompleted(stateMachine.MoveNext);
    }

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        awaiter.OnCompleted(stateMachine.MoveNext);
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine) { }
}