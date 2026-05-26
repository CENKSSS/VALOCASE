using System.Collections.Generic;
using UnityEngine;

namespace ValoCase.Pooling
{
    public sealed class ObjectPool<T> where T : Component
    {
        readonly T _prefab;
        readonly Transform _parent;
        readonly Stack<T> _inactive = new();
        readonly HashSet<T> _active = new();

        public ObjectPool(T prefab, Transform parent, int prewarm = 0)
        {
            _prefab = prefab;
            _parent = parent;
            for (var i = 0; i < prewarm; i++) _inactive.Push(CreateInstance());
        }

        public T Get()
        {
            var item = _inactive.Count > 0 ? _inactive.Pop() : CreateInstance();
            item.gameObject.SetActive(true);
            if (item is IPoolable poolable) poolable.OnSpawned();
            _active.Add(item);
            return item;
        }

        public void Release(T item)
        {
            if (item == null || !_active.Remove(item)) return;
            if (item is IPoolable poolable) poolable.OnDespawned();
            item.gameObject.SetActive(false);
            _inactive.Push(item);
        }

        T CreateInstance()
        {
            var instance = Object.Instantiate(_prefab, _parent);
            instance.gameObject.SetActive(false);
            return instance;
        }
    }
}
