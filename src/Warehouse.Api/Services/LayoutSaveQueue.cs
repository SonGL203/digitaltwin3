using System.Threading.Channels;
using Warehouse.Api.ViewModels;

namespace Warehouse.Api.Services
{
    /// <summary>
    /// Một lần Lưu nháp mặt bằng chạy nền. Status: Queued → Running → Done / Conflict / Failed.
    /// Total/Built: số kệ phải sinh tầng, khung và số kệ đã sinh xong (để trang 2D hiện tiến độ).
    /// </summary>
    public class LayoutSaveJob(int roomId, SaveLayoutRequest request)
    {
        private readonly object _lock = new();
        private string _status = "Queued";
        private int _total;
        private int _built;
        private string? _error;

        public int RoomId { get; } = roomId;
        public SaveLayoutRequest Request { get; } = request;

        public bool IsFinished
        {
            get { lock (_lock) return IsFinishedStatus(_status); }
        }

        public static bool IsFinishedStatus(string status)
        {
            return status == "Done" || status == "Conflict" || status == "Failed";
        }

        public void Start()
        {
            lock (_lock) _status = "Running";
        }

        public void Progress(int built, int total)
        {
            lock (_lock)
            {
                _built = built;
                _total = total;
            }
        }

        /// <summary>error = null: lưu xong; Conflict: dữ liệu đổi trong lúc chờ (người khác lưu trước, kệ còn hàng…); Failed: lỗi máy chủ.</summary>
        public void Finish(string status, string? error)
        {
            lock (_lock)
            {
                _status = status;
                _error = error;
            }
        }

        public LayoutSaveJobViewModel Snapshot()
        {
            lock (_lock) return new LayoutSaveJobViewModel(RoomId, _status, _total, _built, _error);
        }
    }

    /// <summary>
    /// Hàng đợi Lưu nháp của trang 2D (LayoutSaveWorker đọc ra, chạy lần lượt). Mỗi kho chỉ một lần lưu đang chạy;
    /// lần lưu gần nhất được giữ lại để trang 2D hỏi kết quả. Nằm trong bộ nhớ: khởi động lại server thì mất,
    /// nhưng lưu là một transaction nên không có kho nào bị lưu dở.
    /// </summary>
    public class LayoutSaveQueue
    {
        private readonly Channel<LayoutSaveJob> _channel = Channel.CreateUnbounded<LayoutSaveJob>();
        private readonly Dictionary<int, LayoutSaveJob> _jobs = new();

        public ChannelReader<LayoutSaveJob> Reader => _channel.Reader;

        public LayoutSaveJob? Find(int roomId)
        {
            lock (_jobs)
            {
                _jobs.TryGetValue(roomId, out var job);
                return job;
            }
        }

        /// <summary>Thêm vào hàng đợi; false khi kho này đang có lần lưu chưa xong.</summary>
        public bool TryPush(LayoutSaveJob job)
        {
            lock (_jobs)
            {
                if (_jobs.TryGetValue(job.RoomId, out var existing) && !existing.IsFinished) return false;
                _jobs[job.RoomId] = job;
            }
            _channel.Writer.TryWrite(job);
            return true;
        }
    }
}
