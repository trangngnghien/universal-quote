using System;
using System.Collections.Generic;

namespace WallpaperQuote.Core
{
    /// <summary>
    /// Random không lặp (W3.2): xáo trộn cả danh sách, lấy lần lượt, hết vòng mới xáo lại.
    /// Phần tử đầu vòng mới luôn khác phần tử vừa hiển thị.
    /// </summary>
    public sealed class ShuffleBag<T> where T : class
    {
        private readonly Random _random = new Random();
        private readonly IEqualityComparer<T> _comparer;
        private List<T> _source = new List<T>();
        private readonly Queue<T> _queue = new Queue<T>();

        public ShuffleBag(IEqualityComparer<T>? comparer = null)
        {
            _comparer = comparer ?? EqualityComparer<T>.Default;
        }

        public T? Current { get; private set; }
        public int Count => _source.Count;

        /// <summary>Thay danh sách nguồn. Vòng hiện tại được xáo lại từ danh sách mới.</summary>
        public void SetItems(IEnumerable<T> items)
        {
            _source = new List<T>(items);
            _queue.Clear();
        }

        /// <summary>Lấy phần tử kế tiếp, khác phần tử đang hiển thị nếu có thể. Null nếu danh sách rỗng.</summary>
        public T? Next()
        {
            if (_source.Count == 0)
            {
                Current = null;
                return null;
            }
            if (_queue.Count == 0) Refill();

            var next = _queue.Dequeue();
            if (_source.Count > 1 && Current != null && _comparer.Equals(next, Current))
            {
                if (_queue.Count == 0) Refill();
                var other = _queue.Dequeue();
                _queue.Enqueue(next);
                next = other;
            }
            Current = next;
            return next;
        }

        private void Refill()
        {
            var items = new List<T>(_source);
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = _random.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
            if (items.Count > 1 && Current != null && _comparer.Equals(items[0], Current))
                (items[0], items[items.Count - 1]) = (items[items.Count - 1], items[0]);
            foreach (var item in items) _queue.Enqueue(item);
        }
    }
}
