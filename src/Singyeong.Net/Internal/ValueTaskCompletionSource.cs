using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Singyeong.Internal
{
    internal class ValueTaskCompletionSource<TResult>
        : IValueTaskSource<TResult>
    {
        // MUTABLE STRUCT! Do not make readonly.
        private ManualResetValueTaskSourceCore<TResult> _core;
        private int _wasSet;
        private CancellationToken _currentCancelToken;

        public bool RunContinuationsAsynchronously
        {
            get => _core.RunContinuationsAsynchronously;
            set => _core.RunContinuationsAsynchronously = value;
        }

        public void Reset()
        {
            if (!TryReset())
                throw new InvalidOperationException(
                    "ValueTask was not completed");
        }

        public bool TryReset()
        {
            var wasSet = Interlocked.Exchange(ref _wasSet, 0) == 1;

            if (wasSet)
                _core.Reset();

            return wasSet;
        }

        public void SetResult(TResult result)
        {
            if (!TrySetResult(result))
                throw new InvalidOperationException(
                    "ValueTask was already completed");
        }

        public bool TrySetResult(TResult result)
        {
            var wasUnset = Interlocked.Exchange(ref _wasSet, 1) == 0;

            if (wasUnset)
                _core.SetResult(result);

            return wasUnset;
        }

        public void SetException(Exception exception)
        {
            if (!TrySetException(exception))
                throw new InvalidOperationException(
                    "ValueTask was already completed");
        }

        public bool TrySetException(Exception exception)
        {
            var wasUnset = Interlocked.Exchange(ref _wasSet, 1) == 0;

            if (wasUnset)
                _core.SetException(exception);

            return wasUnset;
        }

        public TResult GetResult(short token)
            => _core.GetResult(token);

        public ValueTaskSourceStatus GetStatus(short token)
            => _core.GetStatus(token);

        public void OnCompleted(Action<object?> continuation, object? state,
            short token, ValueTaskSourceOnCompletedFlags flags)
            => _core.OnCompleted(continuation, state, token, flags);

        public ValueTask<TResult> WaitAsync(
            CancellationToken cancellationToken = default)
        {
            if (!cancellationToken.CanBeCanceled)
                return new ValueTask<TResult>(this, _core.Version);

            _currentCancelToken = cancellationToken;
            return SlowPath(this, cancellationToken);

            static async ValueTask<TResult> SlowPath(
                ValueTaskCompletionSource<TResult> instance,
                CancellationToken cancellationToken)
            {
                // Needs to refer by name or else will cause CS8422
                await using var registration = cancellationToken.Register(
                    ValueTaskCompletionSource<TResult>.SetCanceled, instance);
                return await new ValueTask<TResult>(instance,
                    instance._core.Version);
            }
        }

        private static void SetCanceled(object? state)
        {
            Debug.Assert(state is ValueTaskCompletionSource<TResult>);
            var instance = (ValueTaskCompletionSource<TResult>)state;

            _ = instance.TrySetException(
                new OperationCanceledException(
                    instance._currentCancelToken));
        }
    }
}