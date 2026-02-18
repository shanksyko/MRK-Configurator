using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Mieruka.Core.Contracts;
using Serilog;
using WinForms = System.Windows.Forms;

namespace Mieruka.App.Services.Ui;

/// <summary>
/// Coordinates temporary suspension of WinForms binding pipelines while complex UI updates are performed.
/// </summary>
internal sealed class BindingBatchService : IBindingService
{
    private static readonly ILogger Logger = Log.ForContext<BindingBatchService>();

    private readonly Lock _gate = new();
    private readonly List<IBatchParticipant> _participants = new();
    private int _batchDepth;

    /// <inheritdoc />
    public void Track(BindingSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_gate)
        {
            if (_participants.Any(p => p.Supports(source)))
            {
                return;
            }

            _participants.Add(new BindingSourceParticipant(source));
        }
    }

    /// <inheritdoc />
    public void Track<T>(BindingList<T> list)
    {
        ArgumentNullException.ThrowIfNull(list);

        lock (_gate)
        {
            if (_participants.Any(p => p.Supports(list)))
            {
                return;
            }

            _participants.Add(new BindingListParticipant<T>(list));
        }
    }

    /// <inheritdoc />
    public IDisposable BeginBatch()
    {
        EnterBatch();
        return new BatchScope(this);
    }

    private void EnterBatch()
    {
        lock (_gate)
        {
            _batchDepth++;
            if (_batchDepth > 1)
            {
                return;
            }

            foreach (var participant in _participants.ToArray())
            {
                try
                {
                    participant.Suspend();
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "BindingBatchSuspendFailed");
                }
            }
        }
    }

    private void ExitBatch()
    {
        lock (_gate)
        {
            if (_batchDepth == 0)
            {
                return;
            }

            _batchDepth--;
            if (_batchDepth > 0)
            {
                return;
            }

            for (var index = _participants.Count - 1; index >= 0; index--)
            {
                var participant = _participants[index];
                try
                {
                    participant.Resume();
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "BindingBatchResumeFailed");
                }
            }
        }
    }

    private interface IBatchParticipant
    {
        bool Supports(object candidate);

        void Suspend();

        void Resume();
    }

    private sealed class BindingSourceParticipant : IBatchParticipant
    {
        private readonly BindingSource _source;
        private int _suspended;

        public BindingSourceParticipant(BindingSource source)
        {
            _source = source;
        }

        public bool Supports(object candidate) => ReferenceEquals(candidate, _source);

        public void Suspend()
        {
            if (Interlocked.Exchange(ref _suspended, 1) == 1)
            {
                return;
            }

            try
            {
                _source.SuspendBinding();
            }
            catch (ObjectDisposedException)
            {
                Interlocked.Exchange(ref _suspended, 0);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "BindingSourceSuspendFailed");
                Interlocked.Exchange(ref _suspended, 0);
            }
        }

        public void Resume()
        {
            if (Interlocked.Exchange(ref _suspended, 0) == 0)
            {
                return;
            }

            try
            {
                _source.ResumeBinding();
            }
            catch (ObjectDisposedException)
            {
                // Ignore disposal races.
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "BindingSourceResumeFailed");
            }
        }
    }

    private sealed class BindingListParticipant<T> : IBatchParticipant
    {
        private readonly BindingList<T> _list;
        private bool _previousState;
        private bool _initialized;

        public BindingListParticipant(BindingList<T> list)
        {
            _list = list;
        }

        public bool Supports(object candidate) => ReferenceEquals(candidate, _list);

        public void Suspend()
        {
            if (_initialized)
            {
                return;
            }

            _previousState = _list.RaiseListChangedEvents;
            _list.RaiseListChangedEvents = false;
            _initialized = true;
        }

        public void Resume()
        {
            if (!_initialized)
            {
                return;
            }

            _list.RaiseListChangedEvents = _previousState;
            _initialized = false;

            if (_previousState)
            {
                try
                {
                    _list.ResetBindings();
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "BindingListResetFailed");
                }
            }
        }
    }

    private sealed class BatchScope : IDisposable
    {
        private BindingBatchService? _owner;

        public BatchScope(BindingBatchService owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.ExitBatch();
        }
    }
}
