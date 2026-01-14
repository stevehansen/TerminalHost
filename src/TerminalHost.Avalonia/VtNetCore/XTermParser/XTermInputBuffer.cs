namespace VtNetCore.XTermParser
{
    using System;
    using System.Collections.Generic;

    public class XTermInputBuffer
    {
        // Use a dynamically growing buffer instead of constant array reallocation
        private byte[] _buffer = Array.Empty<byte>();
        private int _length;

        public byte[] Buffer => _buffer;

        private class StreamState
        {
            public int Position { get; set; }
            public EMode Mode { get; set; }
        }

        private List<StreamState> StateStack { get; set; } = new List<StreamState>();

        public int Position { get; set; } = 0;

        public enum EMode
        {
            Raw,
            UTF8,
            USASCII,
            C0,
            UK
        };

        public EMode Mode { get; set; } = EMode.UTF8;

        public byte[] Stacked
        {
            get
            {
                if (StateStack.Count == 0)
                    return Array.Empty<byte>();
                var first = StateStack[0];
                var count = Position - first.Position;
                if (count <= 0)
                    return Array.Empty<byte>();
                var result = new byte[count];
                Array.Copy(_buffer, first.Position, result, 0, count);
                return result;
            }
        }

        public void Add(byte[] data)
        {
            if (data == null || data.Length == 0)
                return;
            Add(data, 0, data.Length);
        }

        /// <summary>
        /// Adds data to the buffer without creating intermediate array copies.
        /// </summary>
        public void Add(byte[] data, int offset, int count)
        {
            if (data == null || count == 0)
                return;

            // Ensure capacity
            var requiredLength = _length + count;
            if (requiredLength > _buffer.Length)
            {
                // Grow by doubling or to required size, whichever is larger
                var newSize = Math.Max(_buffer.Length * 2, requiredLength);
                // Cap at a reasonable size to avoid excessive memory
                newSize = Math.Min(newSize, Math.Max(requiredLength, 1024 * 1024)); // 1MB max
                var newBuffer = new byte[newSize];
                if (_length > 0)
                {
                    Array.Copy(_buffer, 0, newBuffer, 0, _length);
                }
                _buffer = newBuffer;
            }

            Array.Copy(data, offset, _buffer, _length, count);
            _length += count;
        }

        public void PushState()
        {
            StateStack.Add(
                new StreamState
                {
                    Position = Position,
                    Mode = Mode
                }
            );
        }

        public void PopState()
        {
            if (StateStack.Count < 1)
                throw new Exception("There are no more states to pop");

            var last = StateStack.Last();
            StateStack.RemoveAt(StateStack.Count - 1);

            Position = last.Position;
            Mode = last.Mode;
        }

        public void PopAllStates()
        {
            if (StateStack.Count < 1)
                return;

            var first = StateStack.First();
            StateStack.Clear();

            Position = first.Position;
            Mode = first.Mode;
        }

        public void RemoveTailState()
        {
            if (StateStack.Count < 1)
                throw new Exception("There are no more states to remove");

            StateStack.RemoveAt(StateStack.Count - 1);
        }

        public void RemoveHeadState()
        {
            if (StateStack.Count < 1)
                throw new Exception("There are no more states to remove");

            StateStack.RemoveAt(0);
        }

        public void Commit()
        {
            StateStack.Clear();
        }

        public void Flush()
        {
            if (StateStack.Count > 0)
                throw new Exception("The buffer should not be flushed when it is holding a state");

            // Efficiently compact the buffer by shifting remaining data to the start
            if (Position > 0 && Position < _length)
            {
                var remaining = _length - Position;
                Array.Copy(_buffer, Position, _buffer, 0, remaining);
                _length = remaining;
            }
            else if (Position >= _length)
            {
                // All data consumed, just reset
                _length = 0;
            }
            Position = 0;
        }

        public bool AtEnd
        {
            get
            {
                return _length == 0 || Position >= _length;
            }
        }

        public int Remaining
        {
            get
            {
                return _length - Position;
            }
        }

        public byte PeekAhead(int skip)
        {
            if (skip >= Remaining)
                throw new IndexOutOfRangeException("Buffer does not contain enough data to process request");

            return Buffer[Position + skip];
        }

        public char Read(bool utf8=false)
        {
            if(utf8)
                return ReadUtf8();
            return ReadRaw();
        }

        public char ReadRaw()
        {
            var result = (char)PeekAhead(0);
            Position++;
            return result;
        }

        // Buffer for high surrogate from 4-byte UTF-8 sequences (emoji)
        private char? _pendingHighSurrogate;

        public char ReadUtf8()
        {
            // If we have a pending high surrogate from a previous 4-byte read, return the low surrogate now
            if (_pendingHighSurrogate.HasValue)
            {
                var lowSurrogate = _pendingHighSurrogate.Value;
                _pendingHighSurrogate = null;
                return lowSurrogate;
            }

            int first = PeekAhead(0);
            if ((first & 0x80) == 0x00)
            {
                Position++;
                return (char)first;
            }

            if ((first & 0xE0) == 0xC0)
            {
                int second = PeekAhead(1);
                if ((second & 0xC0) == 0x80)
                {
                    Position += 2;
                    return (char)(((first & 0x1F) << 6) | (second & 0x3F));
                }
            }

            if ((first & 0xF0) == 0xE0)
            {
                int second = PeekAhead(1);
                int third = PeekAhead(2);

                if (
                    ((second & 0xC0) == 0x80) &&
                    ((third & 0xC0) == 0x80)
                )
                {
                    Position += 3;
                    return (char)(((first & 0x0F) << 12) | ((second & 0x3F) << 6) | (third & 0x3F));
                }
            }

            if ((first & 0xF8) == 0xF0)
            {
                int second = PeekAhead(1);
                int third = PeekAhead(2);
                int fourth = PeekAhead(3);

                if (
                    ((second & 0xC0) == 0x80) &&
                    ((third & 0xC0) == 0x80) &&
                    ((fourth & 0xC0) == 0x80)
                )
                {
                    Position += 4;
                    // 4-byte UTF-8 decodes to codepoints > 0xFFFF, need surrogate pair
                    int codepoint = ((first & 0x07) << 18) | ((second & 0x3F) << 12) | ((third & 0x3F) << 6) | (fourth & 0x3F);

                    // Convert to surrogate pair
                    codepoint -= 0x10000;
                    char highSurrogate = (char)(0xD800 + (codepoint >> 10));
                    char lowSurrogate = (char)(0xDC00 + (codepoint & 0x3FF));

                    // Store low surrogate to return on next call
                    _pendingHighSurrogate = lowSurrogate;
                    return highSurrogate;
                }
            }

            throw new ArgumentException("The incoming data is not a proper UTF-8 character");
        }
    }
}