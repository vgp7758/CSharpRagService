using System;
using System.Collections.Generic;
using System.IO;

namespace BitRPC.Serialization
{
    public interface ITypeHandler
    {
        int HashCode { get; }
        void Write(object obj, StreamWriter writer);
        object Read(StreamReader reader);
    }

    public interface IBufferSerializer
    {
        void RegisterHandler<T>(ITypeHandler handler);
        ITypeHandler GetHandler(Type type);
        void InitHandlers();
    }

    public class BitMask
    {
        private uint[] _masks;
        private int _size;

        public BitMask() : this(1)
        {
        }

        public BitMask(int size)
        {
            _size = size;
            _masks = new uint[size];
        }

        /// <summary>
        /// 获取BitMask的大小
        /// </summary>
        public int Size => _size;

        public void SetBit(int index, bool value)
        {
            int maskIndex = index / 32;
            int bitIndex = index % 32;

            if (maskIndex >= _size)
            {
                Array.Resize(ref _masks, maskIndex + 1);
                _size = maskIndex + 1;
            }

            if (value)
            {
                _masks[maskIndex] |= (1u << bitIndex);
            }
            else
            {
                _masks[maskIndex] &= ~(1u << bitIndex);
            }
        }

        public bool GetBit(int index)
        {
            int maskIndex = index / 32;
            int bitIndex = index % 32;

            if (maskIndex >= _size)
            {
                return false;
            }

            return (_masks[maskIndex] & (1u << bitIndex)) != 0;
        }

        public void Write(StreamWriter writer)
        {
            writer.WriteInt32(_size);
            for (int i = 0; i < _size; i++)
            {
                writer.WriteUInt32(_masks[i]);
            }
        }

        public void Read(StreamReader reader)
        {
            _size = reader.ReadInt32();
            _masks = new uint[_size];
            for (int i = 0; i < _size; i++)
            {
                _masks[i] = reader.ReadUInt32();
            }
        }

        /// <summary>
        /// 清空BitMask，重置所有位为0
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _size; i++)
            {
                _masks[i] = 0;
            }
        }
    }

    public class BitMaskPool
    {
        private static readonly Queue<BitMask> _pool = new Queue<BitMask>();
        private static readonly object _lock = new object();

        public static BitMask Get(int size)
        {
            lock (_lock)
            {
                if (_pool.Count > 0)
                {
                    var mask = _pool.Dequeue();
                    mask.Clear();
                    return mask;
                }
                return new BitMask(size);
            }
        }

        public static void Return(BitMask mask)
        {
            lock (_lock)
            {
                _pool.Enqueue(mask);
            }
        }
    }

    public class StreamWriter
    {
        private readonly MemoryStream _stream;
        private readonly BinaryWriter _writer;

        public StreamWriter()
        {
            _stream = new MemoryStream();
            _writer = new BinaryWriter(_stream);
        }

        public void WriteInt32(int value)
        {
            _writer.Write(value);
        }

        public void WriteInt64(long value)
        {
            _writer.Write(value);
        }

        public void WriteUInt32(uint value)
        {
            _writer.Write(value);
        }

        public void WriteFloat(float value)
        {
            _writer.Write(value);
        }

        public void WriteDouble(double value)
        {
            _writer.Write(value);
        }

        public void WriteBool(bool value)
        {
            _writer.Write(value);
        }

        public void WriteString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteInt32(-1);
            }
            else
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(value);
                WriteInt32(bytes.Length);
                _writer.Write(bytes);
            }
        }

        public void WriteBytes(byte[] bytes)
        {
            WriteInt32(bytes.Length);
            _writer.Write(bytes);
        }

        public void WriteList<T>(List<T> list, Action<T> writeAction)
        {
            WriteInt32(list.Count);
            foreach (var item in list)
            {
                writeAction(item);
            }
        }

        public void WriteObject(object obj)
        {
            if (obj == null)
            {
                WriteInt32(-1);
                return;
            }

            var serializer = BufferSerializer.Instance;
            var handler = serializer.GetHandler(obj.GetType());
            if (handler != null)
            {
                WriteInt32(handler.HashCode);
                handler.Write(obj, this);
            }
            else
            {
                WriteInt32(-1);
            }
        }

        public byte[] ToArray()
        {
            _writer.Flush();
            return _stream.ToArray();
        }
    }

    public class StreamReader
    {
        private readonly MemoryStream _stream;
        private readonly BinaryReader _reader;

        public StreamReader(byte[] data)
        {
            _stream = new MemoryStream(data);
            _reader = new BinaryReader(_stream);
        }

        public int ReadInt32()
        {
            return _reader.ReadInt32();
        }

        public long ReadInt64()
        {
            return _reader.ReadInt64();
        }

        public uint ReadUInt32()
        {
            return _reader.ReadUInt32();
        }

        public float ReadFloat()
        {
            return _reader.ReadSingle();
        }

        public double ReadDouble()
        {
            return _reader.ReadDouble();
        }

        public bool ReadBool()
        {
            return _reader.ReadBoolean();
        }

        public string ReadString()
        {
            var length = ReadInt32();
            if (length == -1)
            {
                return null;
            }
            var bytes = _reader.ReadBytes(length);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        public byte[] ReadBytes()
        {
            var length = ReadInt32();
            return _reader.ReadBytes(length);
        }

        public List<T> ReadList<T>(Func<T> readFunc)
        {
            var count = ReadInt32();
            var list = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(readFunc());
            }
            return list;
        }

        public object ReadObject()
        {
            var hashCode = ReadInt32();
            if (hashCode == -1)
            {
                return null;
            }

            var serializer = BufferSerializer.Instance;
            var handler = serializer.GetHandlerByHashCode(hashCode);
            if (handler != null)
            {
                return handler.Read(this);
            }
            return null;
        }
    }

    public class BufferSerializer : IBufferSerializer
    {
        public static readonly BufferSerializer Instance = new BufferSerializer();

        private readonly Dictionary<Type, ITypeHandler> _handlers;
        private readonly Dictionary<int, ITypeHandler> _handlersByHashCode;

        private BufferSerializer()
        {
            _handlers = new Dictionary<Type, ITypeHandler>();
            _handlersByHashCode = new Dictionary<int, ITypeHandler>();
            InitHandlers();
        }

        public void RegisterHandler<T>(ITypeHandler handler)
        {
            var type = typeof(T);
            _handlers[type] = handler;
            _handlersByHashCode[handler.HashCode] = handler;
        }

        public ITypeHandler GetHandler(Type type)
        {
            if (_handlers.TryGetValue(type, out var handler))
            {
                return handler;
            }
            return null;
        }

        public ITypeHandler GetHandlerByHashCode(int hashCode)
        {
            if (_handlersByHashCode.TryGetValue(hashCode, out var handler))
            {
                return handler;
            }
            return null;
        }

        public void InitHandlers()
        {
            RegisterHandler<int>(new Int32Handler());
            RegisterHandler<long>(new Int64Handler());
            RegisterHandler<float>(new FloatHandler());
            RegisterHandler<double>(new DoubleHandler());
            RegisterHandler<bool>(new BoolHandler());
            RegisterHandler<string>(new StringHandler());
            RegisterHandler<byte[]>(new BytesHandler());
        }

        public byte[] Serialize<T>(T obj)
        {
            var writer = new StreamWriter();
            var handler = GetHandler(typeof(T));
            if (handler != null)
            {
                handler.Write(obj, writer);
            }
            return writer.ToArray();
        }

        public T Deserialize<T>(byte[] data)
        {
            var reader = new StreamReader(data);
            var handler = GetHandler(typeof(T));
            if (handler != null)
            {
                return (T)handler.Read(reader);
            }
            return default(T);
        }
    }

    public class Int32Handler : ITypeHandler
    {
        public int HashCode => typeof(int).GetHashCode();

        public void Write(object obj, StreamWriter writer)
        {
            writer.WriteInt32((int)obj);
        }

        public object Read(StreamReader reader)
        {
            return reader.ReadInt32();
        }
    }

    public class Int64Handler : ITypeHandler
    {
        public int HashCode => typeof(long).GetHashCode();

        public void Write(object obj, StreamWriter writer)
        {
            writer.WriteInt64((long)obj);
        }

        public object Read(StreamReader reader)
        {
            return reader.ReadInt64();
        }
    }

    public class FloatHandler : ITypeHandler
    {
        public int HashCode => typeof(float).GetHashCode();

        public void Write(object obj, StreamWriter writer)
        {
            writer.WriteFloat((float)obj);
        }

        public object Read(StreamReader reader)
        {
            return reader.ReadFloat();
        }
    }

    public class DoubleHandler : ITypeHandler
    {
        public int HashCode => typeof(double).GetHashCode();

        public void Write(object obj, StreamWriter writer)
        {
            writer.WriteDouble((double)obj);
        }

        public object Read(StreamReader reader)
        {
            return reader.ReadDouble();
        }
    }

    public class BoolHandler : ITypeHandler
    {
        public int HashCode => typeof(bool).GetHashCode();

        public void Write(object obj, StreamWriter writer)
        {
            writer.WriteBool((bool)obj);
        }

        public object Read(StreamReader reader)
        {
            return reader.ReadBool();
        }
    }

    public class StringHandler : ITypeHandler
    {
        public int HashCode => typeof(string).GetHashCode();

        public void Write(object obj, StreamWriter writer)
        {
            writer.WriteString((string)obj);
        }

        public object Read(StreamReader reader)
        {
            return reader.ReadString();
        }
    }

    public class BytesHandler : ITypeHandler
    {
        public int HashCode => typeof(byte[]).GetHashCode();

        public void Write(object obj, StreamWriter writer)
        {
            writer.WriteBytes((byte[])obj);
        }

        public object Read(StreamReader reader)
        {
            return reader.ReadBytes();
        }
    }
}