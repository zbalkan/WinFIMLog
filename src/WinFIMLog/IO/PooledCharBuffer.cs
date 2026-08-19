using System;
using System.Buffers;
using System.Globalization;

namespace WinFIMLog.IO
{
    /// <summary>
    /// A stack-backed character builder that rents a larger array only when its initial span is exceeded.
    /// Any rented buffer is cleared and returned on disposal.
    /// </summary>
    internal ref struct PooledCharBuffer
    {
        private Span<char> buffer;
        private char[]? rented;
        private int length;

        public PooledCharBuffer(Span<char> initialBuffer)
        {
            if (initialBuffer.IsEmpty)
            {
                throw new ArgumentException("The initial buffer must not be empty.", nameof(initialBuffer));
            }

            buffer = initialBuffer;
        }

        public void Append(scoped ReadOnlySpan<char> value)
        {
            EnsureCapacity(value.Length);
            value.CopyTo(buffer[length..]);
            length += value.Length;
        }

        public void Append(char value)
        {
            EnsureCapacity(1);
            buffer[length++] = value;
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

        public override string ToString() => new(buffer[..length]);

        public void Dispose()
        {
            var array = rented;
            rented = null;
            length = 0;
            if (array is not null)
            {
                ArrayPool<char>.Shared.Return(array, clearArray: true);
            }
        }

        private void EnsureCapacity(int additionalLength)
        {
            if (additionalLength <= buffer.Length - length)
            {
                return;
            }

            var requiredLength = checked(length + additionalLength);
            var expanded = ArrayPool<char>.Shared.Rent(Math.Max(requiredLength, checked(buffer.Length * 2)));
            buffer[..length].CopyTo(expanded);

            var previous = rented;
            buffer = expanded;
            rented = expanded;
            if (previous is not null)
            {
                ArrayPool<char>.Shared.Return(previous, clearArray: true);
            }
        }
    }
}
