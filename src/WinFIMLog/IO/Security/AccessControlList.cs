using System;
using System.Buffers;
using System.Security.Principal;

namespace WinFIMLog.IO.Security
{
    /// <summary>
    /// A typed, pool-backed access control list. Instances own their rented ACE buffer and must
    /// be disposed after the caller has emitted or persisted the evidence.
    /// </summary>
    public sealed class AccessControlList : IDisposable
    {
        private AccessControlEntry[]? entries;
        private bool disposed;

        public AccessControlList(int initialCapacity = 4)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
            if (initialCapacity is not 0)
            {
                entries = ArrayPool<AccessControlEntry>.Shared.Rent(initialCapacity);
            }
        }

        public int Count { get; private set; }
        public SecurityIdentifier? Owner { get; set; }
        public SecurityIdentifier? PrimaryGroupOfOwner { get; set; }

        /// <summary>
        /// Gets a non-allocating view of the captured ACEs.
        /// </summary>
        public ReadOnlyMemory<AccessControlEntry> Entries
        {
            get
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return entries is { } buffer ? buffer.AsMemory(0, Count) : ReadOnlyMemory<AccessControlEntry>.Empty;
            }
        }

        internal void Add(AccessControlEntry entry)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var buffer = entries;
            if (buffer is null)
            {
                buffer = ArrayPool<AccessControlEntry>.Shared.Rent(4);
                entries = buffer;
            }
            else if (Count == buffer.Length)
            {
                var expanded = ArrayPool<AccessControlEntry>.Shared.Rent(checked(buffer.Length * 2));
                buffer.AsSpan(0, Count).CopyTo(expanded);
                ArrayPool<AccessControlEntry>.Shared.Return(buffer, clearArray: true);
                entries = expanded;
                buffer = expanded;
            }

            buffer[Count++] = entry;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            var buffer = entries;
            entries = null;
            Count = 0;
            Owner = null;
            PrimaryGroupOfOwner = null;
            if (buffer is not null)
            {
                ArrayPool<AccessControlEntry>.Shared.Return(buffer, clearArray: true);
            }
        }
    }
}
