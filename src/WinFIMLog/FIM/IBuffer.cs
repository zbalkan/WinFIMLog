using System.Collections.Generic;
using System.Threading.Tasks;

namespace WinFIMLog.FIM
{
    public interface IBuffer<T>
        where T : IChange
    {
        Task Add(T change);

        Task AddRange(IEnumerable<T> changes);

        public int Count();

        public bool HasNext();

        public List<T> Take(int count);

        public List<T> TakeAll();
    }
}
