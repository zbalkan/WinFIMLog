using System;
using System.Buffers;
using System.Globalization;

namespace WinFIMLog.IO
{
    /// <summary>A disposable growable character buffer backed by <see cref="ArrayPool{T}"/>.</summary>
    internal sealed class PooledCharBuffer : IDisposable
    {
        private char[]? buffer;
        private int length;

        public PooledCharBuffer(int initialCapacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
            buffer = ArrayPool<char>.Shared.Rent(initialCapacity);
        }

        public void Append(ReadOnlySpan<char> value)
        {
            EnsureCapacity(value.Length);
            value.CopyTo(buffer!.AsSpan(length));
            length += value.Length;
        }

        public void Append(char value)
        {
            EnsureCapacity(1);
            buffer![length++] = value;
        }

        public void Append(bool value) => Append(value ? "true" : "false");

        public void Append(int value)
        {
            Span<char> characters = stackalloc char[11];
            if (!value.TryFormat(characters, out var written, provider: CultureInfo.InvariantCulture))
            {
                throw new InvalidOperationException("The integer did not fit in the stack buffer.");
            }

            Append(characters[..written]);
        }

        public void Append(long value)
        {
            Span<char> characters = stackalloc char[20];
            if (!value.TryFormat(characters, out var written, provider: CultureInfo.InvariantCulture))
            {
                throw new InvalidOperationException("The integer did not fit in the stack buffer.");
            }

            Append(characters[..written]);
        }

        public void Append(ulong value)
        {
            Span<char> characters = stackalloc char[20];
            if (!value.TryFormat(characters, out var written, provider: CultureInfo.InvariantCulture))
            {
                throw new InvalidOperationException("The integer did not fit in the stack buffer.");
            }

            Append(characters[..written]);
        }

        public void Append(double value)
        {
            Span<char> characters = stackalloc char[32];
            if (!value.TryFormat(characters, out var written, provider: CultureInfo.InvariantCulture))
            {
                throw new InvalidOperationException("The floating-point value did not fit in the stack buffer.");
            }

            Append(characters[..written]);
        }

        public void Append(DateTime value)
        {
            Span<char> characters = stackalloc char[33];
            if (!value.TryFormat(characters, out var written, "O", CultureInfo.InvariantCulture))
            {
                throw new InvalidOperationException("The timestamp did not fit in the stack buffer.");
            }

            Append(characters[..written]);
        }

        public void Append(DateTimeOffset value)
        {
            Span<char> characters = stackalloc char[33];
            if (!value.TryFormat(characters, out var written, "O", CultureInfo.InvariantCulture))
            {
                throw new InvalidOperationException("The timestamp did not fit in the stack buffer.");
            }

            Append(characters[..written]);
        }

        public void AppendHex(uint value)
        {
            Span<char> characters = stackalloc char[8];
            if (!value.TryFormat(characters, out var written, "X8", CultureInfo.InvariantCulture))
            {
                throw new InvalidOperationException("The hexadecimal value did not fit in the stack buffer.");
            }

            Append(characters[..written]);
        }

        public override string ToString()
        {
            ObjectDisposedException.ThrowIf(buffer is null, this);
            return new string(buffer, 0, length);
        }

        public void Dispose()
        {
            var rented = buffer;
            buffer = null;
            length = 0;
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented, clearArray: true);
            }
        }

        private void EnsureCapacity(int additionalLength)
        {
            ObjectDisposedException.ThrowIf(buffer is null, this);
            if (additionalLength <= buffer!.Length - length)
            {
                return;
            }

            var requiredLength = checked(length + additionalLength);
            var expanded = ArrayPool<char>.Shared.Rent(Math.Max(requiredLength, checked(buffer.Length * 2)));
            buffer.AsSpan(0, length).CopyTo(expanded);
            ArrayPool<char>.Shared.Return(buffer, clearArray: true);
            buffer = expanded;
        }
    }
}
