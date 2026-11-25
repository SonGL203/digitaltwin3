using System.Threading.Channels;
using Warehouse.Api.Models;
using Warehouse.Api.ViewModels;

namespace Warehouse.Api.Services
{
    public class AllocationJob(AllocationProgress start)
    {
        public const int MaxShownPlacements = 1000;
        public const int MaxShownRejected = 100;

        private readonly object _lock = new();
        private AllocationProgress _progress = start;
        private readonly List<PlacementViewModel> _placements = new();
        private readonly List<RejectionViewModel> _rejected = new();

        public int RoomId { get; } = start.RoomId;
        public int BatchId { get; } = start.BatchId;

        public AllocationProgress Progress
        {
            get { lock (_lock) return _progress; }
        }

        public bool IsFinished
        {
            get { lock (_lock) return IsFinishedStatus(_progress.Status); }
        }

        public static bool IsFinishedStatus(string status)
        {
            return status == "Done" || status == "Failed";
        }

        public void Update(AllocationProgress progress)
        {
            lock (_lock) _progress = progress;
        }

        public void AddRound(AllocationProgress progress, List<PlacementViewModel> placements, List<RejectionViewModel> rejected)
        {
            lock (_lock)
            {
                _progress = progress;
                foreach (var placement in placements)
                {
                    if (_placements.Count >= MaxShownPlacements) break;
                    _placements.Add(placement);
                }
                foreach (var rejection in rejected)
                {
                    if (_rejected.Count >= MaxShownRejected) break;
                    _rejected.Add(rejection);
                }
            }
        }

        public AllocationJobViewModel Snapshot()
        {
            lock (_lock)
            {
                return ToViewModel(_progress, new List<PlacementViewModel>(_placements), new List<RejectionViewModel>(_rejected));
            }
        }

        public static AllocationJobViewModel ToViewModel(
            AllocationProgress p, List<PlacementViewModel> placements, List<RejectionViewModel> rejected)
        {
            return new AllocationJobViewModel(
                p.BatchId, p.Status, p.Total, p.Placed, p.RejectedCount, placements, rejected, p.Error);
        }
    }

    public record WorkItem(int BatchId, AllocationJob? Allocation, bool Import);

    public class AllocationQueue
    {
        private readonly Channel<WorkItem> _channel = Channel.CreateUnbounded<WorkItem>();
        private readonly Dictionary<int, AllocationJob> _jobs = new();
        private readonly HashSet<int> _importing = new();

        public ChannelReader<WorkItem> Reader => _channel.Reader;

        public void PushImport(int batchId)
        {
            lock (_importing)
            {
                if (!_importing.Add(batchId)) return;
            }
            _channel.Writer.TryWrite(new WorkItem(batchId, null, true));
        }

        public void PushImportAndAllocate(AllocationJob job)
        {
            lock (_importing) _importing.Add(job.BatchId);
            _channel.Writer.TryWrite(new WorkItem(job.BatchId, job, true));
        }

        public void FinishImport(int batchId)
        {
            lock (_importing) _importing.Remove(batchId);
        }

        public bool IsImporting(int batchId)
        {
            lock (_importing) return _importing.Contains(batchId);
        }

        public AllocationJob? FindRunning(int batchId)
        {
            lock (_jobs)
            {
                if (_jobs.TryGetValue(batchId, out var job) && !job.IsFinished) return job;
                return null;
            }
        }

        public AllocationJob? Find(int batchId)
        {
            lock (_jobs)
            {
                _jobs.TryGetValue(batchId, out var job);
                return job;
            }
        }

        public bool TryAdd(AllocationJob job)
        {
            lock (_jobs)
            {
                if (_jobs.TryGetValue(job.BatchId, out var existing) && !existing.IsFinished) return false;
                _jobs[job.BatchId] = job;
                return true;
            }
        }

        public void Push(AllocationJob job)
        {
            _channel.Writer.TryWrite(new WorkItem(job.BatchId, job, false));
        }
    }
}
