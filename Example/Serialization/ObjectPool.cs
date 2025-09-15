using System;
using System.Collections.Concurrent;

namespace BitRPC.Serialization
{
    /// <summary>
    /// 高性能对象池，用于减少频繁的内存分配和垃圾回收
    /// </summary>
    /// <typeparam name="T">池化对象类型</typeparam>
    public class ObjectPool<T> where T : class, new()
    {
        private readonly ConcurrentBag<T> _pool;
        private readonly Func<T> _factory;
        private readonly Action<T> _reset;
        private readonly int _maxSize;

        /// <summary>
        /// 创建对象池
        /// </summary>
        /// <param name="factory">对象创建工厂</param>
        /// <param name="reset">对象重置方法</param>
        /// <param name="initialSize">初始大小</param>
        /// <param name="maxSize">最大大小</param>
        public ObjectPool(Func<T> factory = null, Action<T> reset = null, int initialSize = 32, int maxSize = 1024)
        {
            _factory = factory ?? (() => new T());
            _reset = reset;
            _maxSize = maxSize;
            _pool = new ConcurrentBag<T>();

            // 预分配对象
            for (int i = 0; i < initialSize; i++)
            {
                _pool.Add(_factory());
            }
        }

        /// <summary>
        /// 从池中获取对象
        /// </summary>
        /// <returns>池化对象</returns>
        public T Get()
        {
            if (_pool.TryTake(out var item))
            {
                return item;
            }
            return _factory();
        }

        /// <summary>
        /// 返回对象到池中
        /// </summary>
        /// <param name="item">要返回的对象</param>
        public void Return(T item)
        {
            if (item == null) return;

            // 重置对象状态
            _reset?.Invoke(item);

            // 如果池未满，则返回对象
            if (_pool.Count < _maxSize)
            {
                _pool.Add(item);
            }
        }

        /// <summary>
        /// 获取池中当前对象数量
        /// </summary>
        public int Count => _pool.Count;

        /// <summary>
        /// 清空池
        /// </summary>
        public void Clear()
        {
            while (_pool.TryTake(out _)) { }
        }
    }

    /// <summary>
    /// BitMask对象池，按size分组管理
    /// </summary>
    public static class BitMaskPool
    {
        private static readonly Dictionary<int, ObjectPool<BitMask>> _pools = new Dictionary<int, ObjectPool<BitMask>>();
        private static readonly object _lock = new object();

        /// <summary>
        /// 获取指定size的BitMask对象
        /// </summary>
        /// <param name="size">BitMask的大小</param>
        /// <returns>BitMask实例</returns>
        public static BitMask Get(int size = 1)
        {
            lock (_lock)
            {
                if (!_pools.TryGetValue(size, out var pool))
                {
                    // 为新的size创建对象池
                    pool = new ObjectPool<BitMask>(
                        factory: () => new BitMask(size),
                        reset: mask => mask.Clear(),
                        initialSize: Math.Min(64, size * 8), // 根据size调整初始大小
                        maxSize: Math.Min(1024, size * 32) // 根据size调整最大大小
                    );
                    _pools[size] = pool;
                }
                return pool.Get();
            }
        }

        /// <summary>
        /// 返回BitMask对象到池中
        /// </summary>
        /// <param name="mask">BitMask实例</param>
        public static void Return(BitMask mask)
        {
            if (mask == null) return;

            lock (_lock)
            {
                if (_pools.TryGetValue(mask.Size, out var pool))
                {
                    pool.Return(mask);
                }
            }
        }

        /// <summary>
        /// 使用BitMask的便捷方法
        /// </summary>
        /// <param name="size">BitMask的大小</param>
        /// <param name="action">使用BitMask的操作</param>
        public static void Use(int size, Action<BitMask> action)
        {
            var mask = Get(size);
            try
            {
                action(mask);
            }
            finally
            {
                Return(mask);
            }
        }

        /// <summary>
        /// 使用BitMask的便捷方法（带返回值）
        /// </summary>
        /// <param name="size">BitMask的大小</param>
        /// <param name="func">使用BitMask的函数</param>
        /// <returns>函数结果</returns>
        public static T Use<T>(int size, Func<BitMask, T> func)
        {
            var mask = Get(size);
            try
            {
                return func(mask);
            }
            finally
            {
                Return(mask);
            }
        }

        /// <summary>
        /// 获取池统计信息
        /// </summary>
        /// <returns>所有池的统计信息</returns>
        public static Dictionary<int, (int Count, int MaxSize)> GetStats()
        {
            lock (_lock)
            {
                var stats = new Dictionary<int, (int Count, int MaxSize)>();
                foreach (var kvp in _pools)
                {
                    var pool = kvp.Value;
                    var maxSize = pool.GetType().GetField("_maxSize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(pool) as int? ?? 1024;
                    stats[kvp.Key] = (pool.Count, maxSize);
                }
                return stats;
            }
        }

        /// <summary>
        /// 清空所有池
        /// </summary>
        public static void ClearAll()
        {
            lock (_lock)
            {
                foreach (var pool in _pools.Values)
                {
                    pool.Clear();
                }
                _pools.Clear();
            }
        }
    }

    /// <summary>
    /// 序列化上下文，包含对象池管理
    /// </summary>
    public class SerializationContext : IDisposable
    {
        private readonly Stack<BitMask> _activeMasks = new Stack<BitMask>();

        /// <summary>
        /// 获取BitMask对象
        /// </summary>
        /// <returns>BitMask实例</returns>
        public BitMask GetBitMask()
        {
            var mask = BitMaskPool.Get();
            _activeMasks.Push(mask);
            return mask;
        }

        /// <summary>
        /// 释放所有活跃的BitMask对象
        /// </summary>
        public void Dispose()
        {
            while (_activeMasks.Count > 0)
            {
                BitMaskPool.Return(_activeMasks.Pop());
            }
        }

        /// <summary>
        /// 执行序列化操作
        /// </summary>
        /// <param name="action">序列化操作</param>
        public static void Serialize(Action<SerializationContext> action)
        {
            using (var context = new SerializationContext())
            {
                action(context);
            }
        }

        /// <summary>
        /// 执行序列化操作（带返回值）
        /// </summary>
        /// <param name="func">序列化函数</param>
        /// <returns>序列化结果</returns>
        public static T Serialize<T>(Func<SerializationContext, T> func)
        {
            using (var context = new SerializationContext())
            {
                return func(context);
            }
        }
    }
}