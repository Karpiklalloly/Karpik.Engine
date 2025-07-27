using System;
using System.Runtime.CompilerServices;

namespace Karpik.StatAndAbilities
{
    //DragonECS-based
    public class StatPool<T>
        where T : struct, IStat
    {
        public static StatPool<T> Instance { get; } = new();
    
        private int[] _mapping;// index = entityID / value = itemIndex;/ value = 0 = no entityID
        private T[] _items;
        private int[] _recycledItems;
        private int _itemsCount;
        private int _recycledItemsCount = 0;

        public StatPool()
        {
            _mapping = new int[4];
            _items = new T[4];
            _recycledItems = new int[4];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Add(int entityID)
        {
            ValidateMapping(entityID);
            ref int itemIndex = ref _mapping[entityID];
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (itemIndex > 0)
            {
                throw new Exception(entityID.ToString());
            }
#endif
            if (_recycledItemsCount > 0)
            {
                itemIndex = _recycledItems[--_recycledItemsCount];
                _itemsCount++;
            }
            else
            {
                itemIndex = ++_itemsCount;
                if (itemIndex >= _items.Length)
                {
                    Array.Resize(ref _items, _items.Length << 1);
                }
            }
            ref var result = ref _items[itemIndex];
            EnableStat(ref result);
            return ref result;
        }
    
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int entityID)
        {
            ValidateMapping(entityID);
#if (DEBUG && !DISABLE_DEBUG)
            if (!Has(entityID)) { throw new Exception(entityID.ToString()); }
#endif
            return ref _items[_mapping[entityID]];
        }
    
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly T Read(int entityID)
        {
#if (DEBUG && !DISABLE_DEBUG)
            if (!Has(entityID)) { throw new Exception(entityID.ToString()); }
#endif
            return ref _items[_mapping[entityID]];
        }
        
        public ref T TryAddOrGet(int entityID)
        {
            ValidateMapping(entityID);
            ref int itemIndex = ref _mapping[entityID];
            if (itemIndex <= 0)
            {
                if (_recycledItemsCount > 0)
                {
                    itemIndex = _recycledItems[--_recycledItemsCount];
                    _itemsCount++;
                }
                else
                {
                    itemIndex = ++_itemsCount;
                    if (itemIndex >= _items.Length)
                    {
                        Array.Resize(ref _items, _items.Length << 1);
                    }
                }
                EnableStat(ref _items[itemIndex]);
            }
            return ref _items[itemIndex];
        }
    
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Has(int entityID)
        {
            ValidateMapping(entityID);
            return _mapping[entityID] > 0;
        }
    
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Del(int entityID)
        {
            ref int itemIndex = ref _mapping[entityID];
#if (DEBUG && !DISABLE_DEBUG)
            if (itemIndex <= 0) { throw new Exception(entityID.ToString()); }
#endif
            DisableStat(ref _items[itemIndex]);
            if (_recycledItemsCount >= _recycledItems.Length)
            {
                Array.Resize(ref _recycledItems, _recycledItems.Length << 1);
            }
            _recycledItems[_recycledItemsCount++] = itemIndex;
            itemIndex = 0;
            _itemsCount--;
        }
    
        public void TryDel(int entityID)
        {
            if (Has(entityID))
            {
                Del(entityID);
            }
        }
    
        public void Copy(int fromEntityID, int toEntityID)
        {
            ValidateMapping(fromEntityID);
            ValidateMapping(toEntityID);
#if (DEBUG && !DISABLE_DEBUG)
            if (!Has(fromEntityID)) { throw new Exception(fromEntityID.ToString());; }
#endif
            CopyComponent(ref Get(fromEntityID), ref TryAddOrGet(toEntityID));
        }
    
        public void ClearAll()
        {
            _recycledItemsCount = 0;
            if (_itemsCount <= 0) { return; }
            for (int i = 0; i < _mapping.Length; i++)
            {
                TryDel(i);
            }
        }

        private void EnableStat(ref T stat)
        {
            stat.Init();
        }
    
        private void DisableStat(ref T stat)
        {
            stat.DeInit();
        }
    
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CopyComponent(ref T from, ref T to)
        {
            to = from;
        }

        private void ValidateMapping(int entityID)
        {
            if (_mapping.Length <= entityID)
            {
                Array.Resize(ref _mapping, _mapping.Length << 1);
            }
        }
    }
}